using Jwmv.Core.Models;

namespace Jwmv.Core.Abstractions;

public interface ICatalogCacheStore
{
    Task<CatalogCache?> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(CatalogCache catalogCache, CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}
