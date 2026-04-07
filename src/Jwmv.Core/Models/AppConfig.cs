namespace Jwmv.Core.Models;

public sealed record AppConfig
{
    public string PreferredDistributionAlias { get; init; } = "tem";
    public int CatalogRefreshHours { get; init; } = 6;
    public bool AutoEnvEnabled { get; init; } = true;
    public string DefaultShell { get; init; } = "powershell";
    public string? DefaultJavaAlias { get; init; }
    public string? SelfUpdateRepository { get; init; }
}
