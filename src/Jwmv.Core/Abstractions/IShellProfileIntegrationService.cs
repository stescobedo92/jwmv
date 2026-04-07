using Jwmv.Core.Models;

namespace Jwmv.Core.Abstractions;

public interface IShellProfileIntegrationService
{
    string GetProfilePath(ShellKind shellKind);
    Task<string> EnsureIntegratedAsync(ShellKind shellKind, string? profilePath, CancellationToken cancellationToken);
}
