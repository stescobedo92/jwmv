using System.Text.Json.Serialization;

namespace Jwmv.Core.Models;

public sealed record JavaDistributionPackage
{
    public required string Id { get; init; }
    public required string Alias { get; init; }
    public required string Distribution { get; init; }
    public required string DistributionAlias { get; init; }
    public required string JavaVersion { get; init; }
    public required string DistributionVersion { get; init; }
    public required string ArchiveType { get; init; }
    public required string Architecture { get; init; }
    public required string OperatingSystem { get; init; }
    public required string PackageType { get; init; }
    public required string FileName { get; init; }
    public required Uri DownloadUri { get; init; }
    public Uri? PackageInfoUri { get; init; }
    public bool DirectlyDownloadable { get; init; }
    public bool IsLatestBuildAvailable { get; init; }
    public required string ReleaseStatus { get; init; }
    public required string TermOfSupport { get; init; }
    public long Size { get; init; }

    [JsonIgnore]
    public string DisplayVersion => $"{JavaVersion} ({DistributionAlias})";
}
