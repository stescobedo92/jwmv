namespace Jwmv.Core.Models;

public sealed record InstallSdkRequest
{
    public required string CandidateName { get; init; }
    public required string Version { get; init; }
    public bool SetAsDefault { get; init; }
    public bool ForceCatalogRefresh { get; init; }

    /// <summary>
    /// Allows installing a package whose catalog entry does not publish a checksum. Off by default so that
    /// downloads are rejected (fail-closed) instead of silently extracted when they cannot be verified.
    /// </summary>
    public bool AllowUnverified { get; init; }
}
