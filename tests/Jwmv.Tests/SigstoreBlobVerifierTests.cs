using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Jwmv.Core.Exceptions;
using Jwmv.Infrastructure.Security;

namespace Jwmv.Tests;

public sealed class SigstoreBlobVerifierTests : IDisposable
{
    private const string Repository = "example-org/example-repo";
    private const string GitHubIssuer = "https://token.actions.githubusercontent.com";
    private const string Identity = "https://github.com/example-org/example-repo/.github/workflows/release.yml@refs/tags/v9.9.9";

    private readonly string _blobPath = Path.Combine(Path.GetTempPath(), "jwmv-sigstore-tests", Guid.NewGuid().ToString("n") + ".zip");
    private readonly byte[] _blob = Encoding.UTF8.GetBytes("jwmv release payload");
    private readonly TestChain _chain = TestChain.Create();

    public SigstoreBlobVerifierTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_blobPath)!);
        File.WriteAllBytes(_blobPath, _blob);
    }

    [Fact]
    public void Verify_AcceptsSignatureFromWorkflowOfExpectedRepository()
    {
        var (leaf, key) = _chain.IssueLeaf(Identity, GitHubIssuer);
        var verifier = new SigstoreBlobVerifier(_chain.TrustRoot);

        var result = verifier.Verify(_blobPath, Sign(key), Convert.ToBase64String(Encoding.ASCII.GetBytes(leaf.ExportCertificatePem())), Repository);

        Assert.Equal(Identity, result.SignerIdentity);
        Assert.Equal(GitHubIssuer, result.Issuer);
    }

    [Fact]
    public void Verify_AcceptsRawPemCertificate()
    {
        var (leaf, key) = _chain.IssueLeaf(Identity, GitHubIssuer);
        var verifier = new SigstoreBlobVerifier(_chain.TrustRoot);

        var result = verifier.Verify(_blobPath, Sign(key), leaf.ExportCertificatePem(), Repository);

        Assert.Equal(Identity, result.SignerIdentity);
    }

    [Fact]
    public void Verify_RejectsCertificateIssuedForAnotherRepository()
    {
        var (leaf, key) = _chain.IssueLeaf("https://github.com/attacker/example-repo/.github/workflows/release.yml@refs/tags/v9.9.9", GitHubIssuer);
        var verifier = new SigstoreBlobVerifier(_chain.TrustRoot);

        Assert.Throws<IntegrityVerificationException>(() => verifier.Verify(_blobPath, Sign(key), leaf.ExportCertificatePem(), Repository));
    }

    [Fact]
    public void Verify_RejectsRepositoryNameThatOnlySharesPrefix()
    {
        var (leaf, key) = _chain.IssueLeaf("https://github.com/example-org/example-repo-evil/.github/workflows/release.yml@refs/tags/v9.9.9", GitHubIssuer);
        var verifier = new SigstoreBlobVerifier(_chain.TrustRoot);

        Assert.Throws<IntegrityVerificationException>(() => verifier.Verify(_blobPath, Sign(key), leaf.ExportCertificatePem(), Repository));
    }

    [Theory]
    [InlineData("https://github.com/example-org/example-repo/.github/workflows/ci.yml@refs/heads/main")]
    [InlineData("https://github.com/example-org/example-repo/.github/workflows/release.yaml@refs/tags/v9.9.9")]
    [InlineData("https://github.com/example-org/example-repo/.github/workflows/release.yml.bak@refs/tags/v9.9.9")]
    [InlineData("https://github.com/example-org/example-repo/.github/workflows/release.yml@heads/main")]
    [InlineData("https://github.com/example-org/example-repo/evil/.github/workflows/release.yml@refs/tags/v9.9.9")]
    public void Verify_RejectsWorkflowsOtherThanTheReleaseWorkflow(string identity)
    {
        var (leaf, key) = _chain.IssueLeaf(identity, GitHubIssuer);
        var verifier = new SigstoreBlobVerifier(_chain.TrustRoot);

        Assert.Throws<IntegrityVerificationException>(() => verifier.Verify(_blobPath, Sign(key), leaf.ExportCertificatePem(), Repository));
    }

    [Fact]
    public void Verify_AcceptsTheReleaseWorkflowRunFromABranch()
    {
        const string identity = "https://github.com/example-org/example-repo/.github/workflows/release.yml@refs/heads/main";
        var (leaf, key) = _chain.IssueLeaf(identity, GitHubIssuer);
        var verifier = new SigstoreBlobVerifier(_chain.TrustRoot);

        var result = verifier.Verify(_blobPath, Sign(key), leaf.ExportCertificatePem(), Repository);

        Assert.Equal(identity, result.SignerIdentity);
    }

    [Fact]
    public void Verify_RejectsCertificateFromUnexpectedOidcIssuer()
    {
        var (leaf, key) = _chain.IssueLeaf(Identity, "https://evil.example");
        var verifier = new SigstoreBlobVerifier(_chain.TrustRoot);

        Assert.Throws<IntegrityVerificationException>(() => verifier.Verify(_blobPath, Sign(key), leaf.ExportCertificatePem(), Repository));
    }

    [Fact]
    public void Verify_RejectsCertificateNotChainedToTrustedRoot()
    {
        var otherChain = TestChain.Create();
        var (leaf, key) = otherChain.IssueLeaf(Identity, GitHubIssuer);
        var verifier = new SigstoreBlobVerifier(_chain.TrustRoot);

        Assert.Throws<IntegrityVerificationException>(() => verifier.Verify(_blobPath, Sign(key), leaf.ExportCertificatePem(), Repository));
    }

    [Fact]
    public void Verify_RejectsSelfSignedCertificateWithExpectedIdentity()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=leaf", key, HashAlgorithmName.SHA256);
        TestChain.AddLeafExtensions(request, Identity, GitHubIssuer);
        using var leaf = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(10));
        var verifier = new SigstoreBlobVerifier(_chain.TrustRoot);

        Assert.Throws<IntegrityVerificationException>(() => verifier.Verify(_blobPath, Sign(key), leaf.ExportCertificatePem(), Repository));
    }

    [Fact]
    public void Verify_RejectsTamperedBlob()
    {
        var (leaf, key) = _chain.IssueLeaf(Identity, GitHubIssuer);
        var signature = Sign(key);
        File.WriteAllBytes(_blobPath, Encoding.UTF8.GetBytes("jwmv release payload (tampered)"));
        var verifier = new SigstoreBlobVerifier(_chain.TrustRoot);

        Assert.Throws<IntegrityVerificationException>(() => verifier.Verify(_blobPath, signature, leaf.ExportCertificatePem(), Repository));
    }

    [Fact]
    public void Verify_RejectsMalformedSignature()
    {
        var (leaf, _) = _chain.IssueLeaf(Identity, GitHubIssuer);
        var verifier = new SigstoreBlobVerifier(_chain.TrustRoot);

        Assert.Throws<IntegrityVerificationException>(() => verifier.Verify(_blobPath, "not base64!", leaf.ExportCertificatePem(), Repository));
        Assert.Throws<IntegrityVerificationException>(() => verifier.Verify(_blobPath, Convert.ToBase64String(new byte[] { 1, 2, 3 }), leaf.ExportCertificatePem(), Repository));
    }

    [Fact]
    public void Verify_AcceptsExpiredLeafBecauseFulcioCertificatesAreShortLived()
    {
        var (leaf, key) = _chain.IssueLeaf(Identity, GitHubIssuer, notBefore: DateTimeOffset.UtcNow.AddYears(-1), notAfter: DateTimeOffset.UtcNow.AddYears(-1).AddMinutes(10));
        var verifier = new SigstoreBlobVerifier(_chain.TrustRoot);

        var result = verifier.Verify(_blobPath, Sign(key), leaf.ExportCertificatePem(), Repository);

        Assert.Equal(Identity, result.SignerIdentity);
    }

    [Fact]
    public void PublicGoodTrustRoot_ContainsSigstoreRootAndIntermediate()
    {
        var trustRoot = SigstoreTrustRoot.PublicGood;

        Assert.Contains(trustRoot.Roots, certificate => certificate.Subject.Contains("CN=sigstore", StringComparison.Ordinal) && certificate.Issuer == certificate.Subject);
        Assert.Contains(trustRoot.Intermediates, certificate => certificate.Subject.Contains("sigstore-intermediate", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        _chain.Dispose();
        if (File.Exists(_blobPath))
        {
            File.Delete(_blobPath);
        }
    }

    private string Sign(ECDsa key)
    {
        var data = File.ReadAllBytes(_blobPath);
        return Convert.ToBase64String(key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
    }

    private sealed class TestChain : IDisposable
    {
        private readonly ECDsa _rootKey;
        private readonly ECDsa _intermediateKey;
        private readonly X509Certificate2 _root;
        private readonly X509Certificate2 _intermediate;
        private readonly List<IDisposable> _disposables = [];

        private TestChain(ECDsa rootKey, X509Certificate2 root, ECDsa intermediateKey, X509Certificate2 intermediate)
        {
            _rootKey = rootKey;
            _root = root;
            _intermediateKey = intermediateKey;
            _intermediate = intermediate;
        }

        public SigstoreTrustRoot TrustRoot => new([new X509Certificate2(_root.RawData)], [new X509Certificate2(_intermediate.RawData)]);

        public static TestChain Create()
        {
            var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var rootRequest = new CertificateRequest("O=jwmv-tests, CN=test-root", rootKey, HashAlgorithmName.SHA256);
            rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            rootRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(rootRequest.PublicKey, false));
            var root = rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddYears(-2), DateTimeOffset.UtcNow.AddYears(10));

            var intermediateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var intermediateRequest = new CertificateRequest("O=jwmv-tests, CN=test-intermediate", intermediateKey, HashAlgorithmName.SHA256);
            intermediateRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
            intermediateRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            intermediateRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.3")], false));
            intermediateRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(intermediateRequest.PublicKey, false));
            var intermediate = intermediateRequest.Create(root, DateTimeOffset.UtcNow.AddYears(-2), DateTimeOffset.UtcNow.AddYears(9), [2]);
            var intermediateWithKey = intermediate.CopyWithPrivateKey(intermediateKey);
            intermediate.Dispose();

            return new TestChain(rootKey, root, intermediateKey, intermediateWithKey);
        }

        public (X509Certificate2 Leaf, ECDsa Key) IssueLeaf(string identity, string issuer, DateTimeOffset? notBefore = null, DateTimeOffset? notAfter = null)
        {
            var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=leaf", key, HashAlgorithmName.SHA256);
            AddLeafExtensions(request, identity, issuer);
            var start = notBefore ?? DateTimeOffset.UtcNow.AddMinutes(-1);
            var leaf = request.Create(_intermediate, start, notAfter ?? start.AddMinutes(10), Guid.NewGuid().ToByteArray());
            _disposables.Add(key);
            _disposables.Add(leaf);
            return (leaf, key);
        }

        public static void AddLeafExtensions(CertificateRequest request, string identity, string issuer)
        {
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.3")], false));
            var san = new SubjectAlternativeNameBuilder();
            san.AddUri(new Uri(identity));
            request.CertificateExtensions.Add(san.Build(critical: true));
            request.CertificateExtensions.Add(new X509Extension(new Oid("1.3.6.1.4.1.57264.1.1"), Encoding.ASCII.GetBytes(issuer), false));
            var writer = new AsnWriter(AsnEncodingRules.DER);
            writer.WriteCharacterString(UniversalTagNumber.UTF8String, issuer);
            request.CertificateExtensions.Add(new X509Extension(new Oid("1.3.6.1.4.1.57264.1.8"), writer.Encode(), false));
        }

        public void Dispose()
        {
            foreach (var disposable in _disposables)
            {
                disposable.Dispose();
            }

            _root.Dispose();
            _intermediate.Dispose();
            _rootKey.Dispose();
            _intermediateKey.Dispose();
        }
    }
}
