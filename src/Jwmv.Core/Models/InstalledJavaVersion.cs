using Jwmv.Core.Utilities;

namespace Jwmv.Core.Models;

public sealed record InstalledJavaVersion
{
    public required string Alias { get; init; }
    public required string Distribution { get; init; }
    public required string DistributionAlias { get; init; }
    public required string JavaVersion { get; init; }
    public required string DistributionVersion { get; init; }
    public required string InstallDirectory { get; init; }
    public required string JavaHome { get; init; }
    public required string ArchiveType { get; init; }
    public required string PackageFileName { get; init; }
    public required string SourcePackageId { get; init; }
    public DateTimeOffset InstalledAtUtc { get; init; }

    public string DisplayAlias => JavaIdentifier.BuildAlias(JavaVersion, DistributionAlias);
}
