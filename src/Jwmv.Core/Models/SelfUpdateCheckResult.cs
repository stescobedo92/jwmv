namespace Jwmv.Core.Models;

public sealed record SelfUpdateCheckResult
{
    public required string Repository { get; init; }
    public required string CurrentVersion { get; init; }
    public required string TargetVersion { get; init; }
    public required string ReleaseTag { get; init; }
    public required string ReleaseName { get; init; }
    public required Uri ReleasePageUri { get; init; }
    public required string AssetName { get; init; }
    public required Uri AssetDownloadUri { get; init; }
    public bool IsUpdateAvailable { get; init; }
    public bool IsForced { get; init; }
}
