using Jwmv.Core.Models;

namespace Jwmv.Core.Abstractions;

public interface IJavaVersionManager
{
    Task<IReadOnlyList<JavaDistributionPackage>> ListAvailableAsync(JavaCatalogQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyList<InstalledJavaVersion>> ListInstalledAsync(CancellationToken cancellationToken);
    Task<InstallJavaResult> InstallAsync(InstallJavaRequest request, IProgress<InstallProgressUpdate>? progress, CancellationToken cancellationToken);
    Task UninstallAsync(string identifier, CancellationToken cancellationToken);
    Task<InstalledJavaVersion> SetDefaultAsync(string identifier, CancellationToken cancellationToken);
    Task<ActiveJavaSelection> ResolveCurrentAsync(string? workingDirectory, CancellationToken cancellationToken);
    Task<string?> GetJavaHomeAsync(string? identifier, CancellationToken cancellationToken);
    Task<string> BuildUseShellScriptAsync(string identifier, ShellKind shellKind, CancellationToken cancellationToken);
    Task<string> BuildEnvShellScriptAsync(string? workingDirectory, ShellKind shellKind, CancellationToken cancellationToken);
    Task<string> BuildShellInitScriptAsync(ShellKind shellKind, CancellationToken cancellationToken);
    Task<ProjectJavaConfiguration?> FindProjectConfigurationAsync(string? workingDirectory, CancellationToken cancellationToken);
    Task RefreshCatalogAsync(CancellationToken cancellationToken);
    Task FlushAsync(FlushRequest request, CancellationToken cancellationToken);
    Task<AppConfig> GetConfigAsync(CancellationToken cancellationToken);
}
