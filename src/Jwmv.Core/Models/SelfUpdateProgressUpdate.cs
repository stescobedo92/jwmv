namespace Jwmv.Core.Models;

public sealed record SelfUpdateProgressUpdate
{
    public required SelfUpdatePhase Phase { get; init; }
    public required string Status { get; init; }
    public double Percentage { get; init; }
}
