using Jwmv.Core.Models;

namespace Jwmv.Core.Abstractions;

public interface IConfigStore
{
    Task<AppConfig> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(AppConfig config, CancellationToken cancellationToken);
}
