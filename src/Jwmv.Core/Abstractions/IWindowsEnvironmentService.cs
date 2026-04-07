using Jwmv.Core.Models;

namespace Jwmv.Core.Abstractions;

public interface IWindowsEnvironmentService
{
    Task ApplyDefaultAsync(InstalledJavaVersion javaVersion, CancellationToken cancellationToken);
    Task ClearDefaultAsync(CancellationToken cancellationToken);
}
