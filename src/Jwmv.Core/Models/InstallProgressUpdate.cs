namespace Jwmv.Core.Models;

public sealed record InstallProgressUpdate
{
    public required InstallPhase Phase { get; init; }
    public required string Status { get; init; }
    public double Percentage { get; init; }
}
