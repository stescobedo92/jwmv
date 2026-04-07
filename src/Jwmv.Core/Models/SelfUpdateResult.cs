namespace Jwmv.Core.Models;

public sealed record SelfUpdateResult
{
    public required string Repository { get; init; }
    public required string PreviousVersion { get; init; }
    public required string TargetVersion { get; init; }
    public required string InstalledAssetName { get; init; }
    public required string ExecutablePath { get; init; }
    public required string UpdaterScriptPath { get; init; }
    public bool RestartScheduled { get; init; }
}
