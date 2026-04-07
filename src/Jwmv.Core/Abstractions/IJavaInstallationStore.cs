using Jwmv.Core.Models;

namespace Jwmv.Core.Abstractions;

public interface IJavaInstallationStore
{
    Task<IReadOnlyList<InstalledJavaVersion>> GetInstalledVersionsAsync(CancellationToken cancellationToken);
    Task<InstalledJavaVersion?> FindByAliasAsync(string alias, CancellationToken cancellationToken);
    Task SaveAsync(InstalledJavaVersion installedVersion, CancellationToken cancellationToken);
    Task DeleteAsync(string alias, CancellationToken cancellationToken);
}
