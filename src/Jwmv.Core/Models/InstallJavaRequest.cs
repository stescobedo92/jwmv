namespace Jwmv.Core.Models;

public sealed record InstallJavaRequest
{
    public required string Identifier { get; init; }
    public bool SetAsDefault { get; init; }
    public bool ForceCatalogRefresh { get; init; }
}
