namespace Jwmv.Core.Models;

public sealed record FlushRequest
{
    public bool IncludeArchives { get; init; }
    public bool IncludeTemp { get; init; }
    public bool IncludeCatalog { get; init; }
}
