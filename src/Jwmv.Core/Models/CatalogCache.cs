namespace Jwmv.Core.Models;

public sealed record CatalogCache
{
    public DateTimeOffset RefreshedAtUtc { get; init; }
    public IReadOnlyList<JavaDistributionPackage> Packages { get; init; } = [];
}
