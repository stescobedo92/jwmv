using Jwmv.Core.Abstractions;
using Jwmv.Core.Models;

namespace Jwmv.Infrastructure.Storage;

public sealed class JsonCatalogCacheStore(JwmvPaths paths) : ICatalogCacheStore
{
    public Task<CatalogCache?> LoadAsync(CancellationToken cancellationToken)
    {
        paths.EnsureCreated();
        return JsonFileHelper.ReadAsync<CatalogCache>(paths.CatalogCacheFilePath, cancellationToken);
    }

    public Task SaveAsync(CatalogCache catalogCache, CancellationToken cancellationToken)
    {
        paths.EnsureCreated();
        return JsonFileHelper.WriteAsync(paths.CatalogCacheFilePath, catalogCache, cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(paths.CatalogCacheFilePath))
        {
            File.Delete(paths.CatalogCacheFilePath);
        }

        return Task.CompletedTask;
    }
}
