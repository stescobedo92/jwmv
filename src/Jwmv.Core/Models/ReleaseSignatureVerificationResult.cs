namespace Jwmv.Core.Models;

public sealed record ReleaseSignatureVerificationResult
{
    /// <summary>The workflow identity (SAN URI) that signed the artifact, for example
    /// <c>https://github.com/owner/repo/.github/workflows/release.yml@refs/tags/v1.2.3</c>.</summary>
    public required string SignerIdentity { get; init; }

    /// <summary>The OIDC issuer recorded in the signing certificate.</summary>
    public required string Issuer { get; init; }
}
