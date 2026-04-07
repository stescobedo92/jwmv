namespace Jwmv.Core.Models;

public sealed record InstallJavaResult
{
    public required InstalledJavaVersion InstalledVersion { get; init; }
    public bool AlreadyInstalled { get; init; }
    public bool DefaultWasUpdated { get; init; }
}
