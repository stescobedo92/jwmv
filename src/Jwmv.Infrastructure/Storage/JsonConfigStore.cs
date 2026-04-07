using Jwmv.Core.Abstractions;
using Jwmv.Core.Models;

namespace Jwmv.Infrastructure.Storage;

public sealed class JsonConfigStore(JwmvPaths paths) : IConfigStore
{
    public async Task<AppConfig> LoadAsync(CancellationToken cancellationToken)
    {
        paths.EnsureCreated();
        return await JsonFileHelper.ReadAsync<AppConfig>(paths.ConfigFilePath, cancellationToken)
            ?? new AppConfig();
    }

    public Task SaveAsync(AppConfig config, CancellationToken cancellationToken)
    {
        paths.EnsureCreated();
        return JsonFileHelper.WriteAsync(paths.ConfigFilePath, config, cancellationToken);
    }
}
