namespace Jwmv.Core.Models;

public sealed record ChecksumVerificationResult
{
    public required bool IsValid { get; init; }
    public required string Expected { get; init; }
    public required string Actual { get; init; }
    public required string Algorithm { get; init; }

    /// <summary>
    /// <see langword="true"/> for legacy algorithms (SHA-1, MD5) that some upstream catalogs still publish.
    /// They still detect corruption, but callers should surface a warning to the user.
    /// </summary>
    public bool IsWeakAlgorithm { get; init; }
}
