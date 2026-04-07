namespace Jwmv.Core.Models;

public sealed record ArchiveExtractionProgress
{
    public int EntriesProcessed { get; init; }
    public int TotalEntries { get; init; }
}
