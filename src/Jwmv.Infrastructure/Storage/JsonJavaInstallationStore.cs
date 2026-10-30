using Jwmv.Core.Abstractions;
using Jwmv.Core.Exceptions;
using Jwmv.Core.Models;
using Jwmv.Core.Utilities;

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
        if (!SdkIdentifier.IsSafeAlias(alias))
        {
            return null;
        }

        var filePath = GetManifestPath(alias);
        return await JsonFileHelper.ReadAsync<InstalledJavaVersion>(filePath, cancellationToken);
    }

    public Task SaveAsync(InstalledJavaVersion installedVersion, CancellationToken cancellationToken)
    {
        var manifestPath = GetManifestPath(installedVersion.Alias);
        paths.EnsureCreated();
        return JsonFileHelper.WriteAsync(manifestPath, installedVersion, cancellationToken);
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

    private string GetManifestPath(string alias)
    {
        if (!SdkIdentifier.IsSafeAlias(alias))
        {
            throw new JwmvException($"'{alias}' is not a valid Java version alias.");
        }

        var manifestPath = Path.Combine(paths.ManifestsDirectory, $"{alias}.json");
        if (!PathTools.IsStrictlyWithinDirectory(paths.ManifestsDirectory, manifestPath))
        {
            throw new JwmvException($"The manifest path for '{alias}' escapes the jwmv manifests directory.");
        }

        return manifestPath;
    }
}
