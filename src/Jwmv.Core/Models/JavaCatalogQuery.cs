namespace Jwmv.Core.Models;

public sealed record JavaCatalogQuery
{
    public string? IdentifierFilter { get; init; }
    public bool ForceRefresh { get; init; }
}
