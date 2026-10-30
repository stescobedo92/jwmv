using Jwmv.Core.Models;

namespace Jwmv.Core.Abstractions;

public interface IReleaseSignatureVerifier
{
    /// <summary>
    /// Verifies that <paramref name="filePath"/> was signed by the release workflow of <paramref name="expectedRepository"/>.
    /// </summary>
    /// <param name="filePath">Path of the downloaded artifact.</param>
    /// <param name="signature">Contents of the <c>.sig</c> companion asset (base64 DER ECDSA signature).</param>
    /// <param name="certificate">Contents of the <c>.pem</c> companion asset (PEM or base64-encoded PEM certificate).</param>
    /// <param name="expectedRepository">GitHub <c>owner/repo</c> whose workflow identity must have produced the signature.</param>
    /// <exception cref="Exceptions.IntegrityVerificationException">The signature, certificate chain or signer identity is not acceptable.</exception>
    ReleaseSignatureVerificationResult Verify(string filePath, string signature, string certificate, string expectedRepository);
}
