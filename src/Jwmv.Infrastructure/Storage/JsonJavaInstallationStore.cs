using Jwmv.Core.Abstractions;
using Jwmv.Core.Models;

namespace Jwmv.Infrastructure.Storage;

public sealed class JsonJavaInstallationStore(JwmvPaths paths) : IJavaInstallationStore
{
    public async Task<IReadOnlyList<InstalledJavaVersion>> GetInstalledVersionsAsync(CancellationToken cancellationToken)
    {
        paths.EnsureCreated();
        var result = new List<InstalledJavaVersion>();
        foreach (var file in Directory.EnumerateFiles(paths.ManifestsDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            var item = await JsonFileHelper.ReadAsync<InstalledJavaVersion>(file, cancellationToken);
            if (item is not null)
            {
                result.Add(item);
            }
        }

        return result;
    }

    public async Task<InstalledJavaVersion?> FindByAliasAsync(string alias, CancellationToken cancellationToken)
    {
        var filePath = GetManifestPath(alias);
        return await JsonFileHelper.ReadAsync<InstalledJavaVersion>(filePath, cancellationToken);
    }

    public Task SaveAsync(InstalledJavaVersion installedVersion, CancellationToken cancellationToken)
    {
        paths.EnsureCreated();
        return JsonFileHelper.WriteAsync(GetManifestPath(installedVersion.Alias), installedVersion, cancellationToken);
    }

    public Task DeleteAsync(string alias, CancellationToken cancellationToken)
    {
        var path = GetManifestPath(alias);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string GetManifestPath(string alias) => Path.Combine(paths.ManifestsDirectory, $"{alias}.json");
}
