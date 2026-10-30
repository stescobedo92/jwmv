using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Jwmv.Core;
using Jwmv.Core.Abstractions;
using Jwmv.Core.Exceptions;
using Jwmv.Core.Models;

namespace Jwmv.Infrastructure.Security;

/// <summary>
/// Verifies <c>cosign sign-blob</c> keyless signatures: the ECDSA signature over the SHA-256 digest of the blob,
/// the Fulcio certificate chain, and the signer identity (GitHub Actions workflow of the expected repository).
/// </summary>
public sealed class SigstoreBlobVerifier(SigstoreTrustRoot trustRoot) : IReleaseSignatureVerifier
{
    public const string GitHubActionsIssuer = "https://token.actions.githubusercontent.com";

    private const string SubjectAlternativeNameOid = "2.5.29.17";
    private const string CodeSigningEkuOid = "1.3.6.1.5.5.7.3.3";
    private const string FulcioIssuerOid = "1.3.6.1.4.1.57264.1.1";
    private const string FulcioIssuerV2Oid = "1.3.6.1.4.1.57264.1.8";
    private const int UriGeneralNameTag = 6;

    public SigstoreBlobVerifier() : this(SigstoreTrustRoot.PublicGood)
    {
    }

    public ReleaseSignatureVerificationResult Verify(string filePath, string signature, string certificate, string expectedRepository)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(signature);
        ArgumentException.ThrowIfNullOrWhiteSpace(certificate);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRepository);

        using var leaf = ParseCertificate(certificate);
        var signatureBytes = DecodeSignature(signature);

        VerifySignature(filePath, leaf, signatureBytes);
        VerifyChain(leaf);

        var identity = ReadSubjectAlternativeNameUris(leaf)
            .FirstOrDefault(uri => uri.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
            ?? throw new IntegrityVerificationException("The release certificate does not carry a GitHub workflow identity.");
        VerifyIdentity(identity, expectedRepository);

        var issuer = ReadOidcIssuer(leaf)
            ?? throw new IntegrityVerificationException("The release certificate does not record an OIDC issuer.");
        if (!string.Equals(issuer, GitHubActionsIssuer, StringComparison.Ordinal))
        {
            throw new IntegrityVerificationException($"The release certificate was issued for OIDC issuer '{issuer}', expected '{GitHubActionsIssuer}'.");
        }

        return new ReleaseSignatureVerificationResult
        {
            SignerIdentity = identity,
            Issuer = issuer
        };
    }

    private static X509Certificate2 ParseCertificate(string certificate)
    {
        var text = certificate.Trim();
        if (!text.Contains("-----BEGIN", StringComparison.Ordinal))
        {
            // cosign writes --output-certificate as base64-encoded PEM by default.
            try
            {
                text = Encoding.ASCII.GetString(Convert.FromBase64String(text));
            }
            catch (FormatException)
            {
                throw new IntegrityVerificationException("The release certificate is neither PEM nor base64-encoded PEM.");
            }
        }

        if (!text.Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal))
        {
            throw new IntegrityVerificationException("The release certificate does not contain a PEM certificate block.");
        }

        try
        {
            return X509Certificate2.CreateFromPem(text);
        }
        catch (CryptographicException exception)
        {
            throw new IntegrityVerificationException($"The release certificate could not be parsed: {exception.Message}");
        }
    }

    private static byte[] DecodeSignature(string signature)
    {
        try
        {
            return Convert.FromBase64String(signature.Trim());
        }
        catch (FormatException)
        {
            throw new IntegrityVerificationException("The release signature is not valid base64.");
        }
    }

    private static void VerifySignature(string filePath, X509Certificate2 leaf, byte[] signature)
    {
        using var publicKey = leaf.GetECDsaPublicKey()
            ?? throw new IntegrityVerificationException("The release certificate does not contain an ECDSA public key.");

        byte[] digest;
        using (var stream = File.OpenRead(filePath))
        {
            digest = SHA256.HashData(stream);
        }

        bool valid;
        try
        {
            valid = publicKey.VerifyHash(digest, signature, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (CryptographicException)
        {
            valid = false;
        }

        if (!valid)
        {
            throw new IntegrityVerificationException("The release signature does not match the downloaded file.");
        }
    }

    private void VerifyChain(X509Certificate2 leaf)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid(CodeSigningEkuOid));
        // Fulcio leaf certificates are valid for ten minutes around the signing time, so the chain is evaluated
        // at the moment the certificate was issued instead of "now".
        chain.ChainPolicy.VerificationTime = leaf.NotBefore.AddSeconds(30);

        foreach (var root in trustRoot.Roots)
        {
            chain.ChainPolicy.CustomTrustStore.Add(root);
        }

        foreach (var intermediate in trustRoot.Intermediates)
        {
            chain.ChainPolicy.ExtraStore.Add(intermediate);
        }

        if (chain.Build(leaf))
        {
            return;
        }

        var reasons = chain.ChainStatus
            .Select(status => status.StatusInformation.Trim())
            .Where(reason => !string.IsNullOrWhiteSpace(reason))
            .Distinct(StringComparer.Ordinal);
        throw new IntegrityVerificationException($"The release certificate does not chain to the Sigstore root: {string.Join("; ", reasons)}");
    }

    private static void VerifyIdentity(string identity, string expectedRepository)
    {
        // Repository names are case-insensitive on GitHub; the workflow path and the ref prefix are not.
        var repositoryPrefix = $"https://github.com/{expectedRepository.Trim().Trim('/')}/";
        var workflowPrefix = $".github/workflows/{JwmvConstants.ReleaseWorkflowFileName}@refs/";
        if (!identity.StartsWith(repositoryPrefix, StringComparison.OrdinalIgnoreCase)
            || !identity.AsSpan(repositoryPrefix.Length).StartsWith(workflowPrefix, StringComparison.Ordinal)
            || identity.Length <= repositoryPrefix.Length + workflowPrefix.Length)
        {
            throw new IntegrityVerificationException($"The release was signed by '{identity}', which is not the release workflow of '{expectedRepository}'.");
        }
    }

    private static IReadOnlyList<string> ReadSubjectAlternativeNameUris(X509Certificate2 certificate)
    {
        var extension = certificate.Extensions[SubjectAlternativeNameOid];
        if (extension is null)
        {
            return [];
        }

        var uris = new List<string>();
        try
        {
            var reader = new AsnReader(extension.RawData, AsnEncodingRules.DER);
            var generalNames = reader.ReadSequence();
            while (generalNames.HasData)
            {
                var tag = generalNames.PeekTag();
                if (tag.TagClass == TagClass.ContextSpecific && tag.TagValue == UriGeneralNameTag)
                {
                    uris.Add(generalNames.ReadCharacterString(UniversalTagNumber.IA5String, tag));
                }
                else
                {
                    generalNames.ReadEncodedValue();
                }
            }
        }
        catch (AsnContentException exception)
        {
            throw new IntegrityVerificationException($"The release certificate has a malformed Subject Alternative Name: {exception.Message}");
        }

        return uris;
    }

    private static string? ReadOidcIssuer(X509Certificate2 certificate)
    {
        string? issuer = null;
        var v2 = certificate.Extensions[FulcioIssuerV2Oid];
        if (v2 is not null)
        {
            try
            {
                issuer = new AsnReader(v2.RawData, AsnEncodingRules.DER).ReadCharacterString(UniversalTagNumber.UTF8String);
            }
            catch (AsnContentException exception)
            {
                throw new IntegrityVerificationException($"The release certificate has a malformed OIDC issuer extension: {exception.Message}");
            }
        }

        var v1 = certificate.Extensions[FulcioIssuerOid];
        if (v1 is not null)
        {
            var legacyIssuer = Encoding.UTF8.GetString(v1.RawData);
            if (issuer is not null && !string.Equals(issuer, legacyIssuer, StringComparison.Ordinal))
            {
                throw new IntegrityVerificationException("The release certificate records conflicting OIDC issuers.");
            }

            issuer ??= legacyIssuer;
        }

        return issuer;
    }
}
