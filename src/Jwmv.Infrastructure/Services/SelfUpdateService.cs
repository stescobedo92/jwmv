using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Jwmv.Core.Abstractions;
using Jwmv.Core.Exceptions;
using Jwmv.Core.Models;

namespace Jwmv.Infrastructure.Services;

public sealed class SelfUpdateService(
    IAppContext appContext,
    IConfigStore configStore,
    IHttpClientFactory httpClientFactory,
    IArchiveDownloader archiveDownloader,
    IArchiveExtractor archiveExtractor,
    JwmvPaths paths) : ISelfUpdateService
{
    private static readonly Regex SemVerPattern = new(@"(?<version>\d+\.\d+\.\d+)", RegexOptions.Compiled);

    public async Task<SelfUpdateCheckResult> CheckForUpdateAsync(SelfUpdateRequest request, CancellationToken cancellationToken)
    {
        var repository = await ResolveRepositoryAsync(request.Repository, cancellationToken);
        var currentVersion = GetCurrentVersion();
        var release = await GetReleaseAsync(repository, request.Tag, cancellationToken);
        var targetVersion = NormalizeVersionString(release.TagName) ?? release.TagName;
        var asset = SelectReleaseAsset(release.Assets);

        var currentSemanticVersion = ParseVersion(currentVersion);
        var targetSemanticVersion = ParseVersion(targetVersion);
        var isUpdateAvailable = request.Force
            || currentSemanticVersion is null
            || targetSemanticVersion is null
            || targetSemanticVersion > currentSemanticVersion;

        return new SelfUpdateCheckResult
        {
            Repository = repository,
            CurrentVersion = currentVersion,
            TargetVersion = targetVersion,
            ReleaseTag = release.TagName,
            ReleaseName = string.IsNullOrWhiteSpace(release.Name) ? release.TagName : release.Name,
            ReleasePageUri = new Uri(release.HtmlUrl),
            AssetName = asset.Name,
            AssetDownloadUri = new Uri(asset.BrowserDownloadUrl),
            IsUpdateAvailable = isUpdateAvailable,
            IsForced = request.Force
        };
    }

    public async Task<SelfUpdateResult> ApplyUpdateAsync(SelfUpdateCheckResult checkResult, bool restartAfterUpdate, IProgress<SelfUpdateProgressUpdate>? progress, CancellationToken cancellationToken)
    {
        paths.EnsureCreated();
        var tempRoot = Path.Combine(paths.TempDirectory, "selfupdate", Guid.NewGuid().ToString("n"));
        var extractRoot = Path.Combine(tempRoot, "extract");
        var packagePath = Path.Combine(tempRoot, checkResult.AssetName);
        Directory.CreateDirectory(tempRoot);
        Directory.CreateDirectory(extractRoot);

        progress?.Report(new SelfUpdateProgressUpdate
        {
            Phase = SelfUpdatePhase.Downloading,
            Percentage = 10,
            Status = $"Downloading {checkResult.AssetName}"
        });

        var downloadProgress = new Progress<ArchiveDownloadProgress>(update =>
        {
            var percentage = update.TotalBytes is > 0
                ? 10 + (update.BytesTransferred / (double)update.TotalBytes.Value * 65d)
                : 10d;
            progress?.Report(new SelfUpdateProgressUpdate
            {
                Phase = SelfUpdatePhase.Downloading,
                Percentage = Math.Min(75d, percentage),
                Status = $"Downloading {checkResult.AssetName}"
            });
        });

        await archiveDownloader.DownloadAsync(checkResult.AssetDownloadUri, packagePath, downloadProgress, cancellationToken);

        progress?.Report(new SelfUpdateProgressUpdate
        {
            Phase = SelfUpdatePhase.Extracting,
            Percentage = 78,
            Status = $"Extracting {checkResult.AssetName}"
        });

        var extractionProgress = new Progress<ArchiveExtractionProgress>(update =>
        {
            var fraction = update.TotalEntries > 0
                ? update.EntriesProcessed / (double)update.TotalEntries
                : 0d;
            progress?.Report(new SelfUpdateProgressUpdate
            {
                Phase = SelfUpdatePhase.Extracting,
                Percentage = 78 + (fraction * 15d),
                Status = $"Extracting {checkResult.AssetName}"
            });
        });

        await archiveExtractor.ExtractZipAsync(packagePath, extractRoot, extractionProgress, cancellationToken);

        progress?.Report(new SelfUpdateProgressUpdate
        {
            Phase = SelfUpdatePhase.Finalizing,
            Percentage = 94,
            Status = "Scheduling updater"
        });

        var currentExecutablePath = appContext.ExecutablePath;
        var replacementExecutablePath = FindUpdatedExecutable(extractRoot);
        var updaterScriptPath = Path.Combine(tempRoot, "apply-jwmv-update.ps1");
        var updaterScript = BuildUpdaterScript(
            currentProcessId: Environment.ProcessId,
            targetExecutablePath: currentExecutablePath,
            replacementExecutablePath,
            packagePath,
            extractRoot,
            restartAfterUpdate);
        await File.WriteAllTextAsync(updaterScriptPath, updaterScript, cancellationToken);
        StartDetachedUpdater(updaterScriptPath);

        progress?.Report(new SelfUpdateProgressUpdate
        {
            Phase = SelfUpdatePhase.Completed,
            Percentage = 100,
            Status = $"Scheduled update to {checkResult.TargetVersion}"
        });

        return new SelfUpdateResult
        {
            Repository = checkResult.Repository,
            PreviousVersion = checkResult.CurrentVersion,
            TargetVersion = checkResult.TargetVersion,
            InstalledAssetName = checkResult.AssetName,
            ExecutablePath = currentExecutablePath,
            UpdaterScriptPath = updaterScriptPath,
            RestartScheduled = restartAfterUpdate
        };
    }

    private async Task<string> ResolveRepositoryAsync(string? requestedRepository, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requestedRepository))
        {
            return requestedRepository.Trim();
        }

        var environmentRepository = appContext.GetEnvironmentVariable("JWVM_SELFUPDATE_REPOSITORY");
        if (!string.IsNullOrWhiteSpace(environmentRepository))
        {
            return environmentRepository.Trim();
        }

        var config = await configStore.LoadAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(config.SelfUpdateRepository))
        {
            return config.SelfUpdateRepository.Trim();
        }

        var assemblyRepository = Assembly.GetEntryAssembly()?
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, "JwmvReleaseRepository", StringComparison.Ordinal))?
            .Value;
        if (!string.IsNullOrWhiteSpace(assemblyRepository))
        {
            return assemblyRepository.Trim();
        }

        throw new JwmvException("No GitHub repository is configured for self-update. Run `jwmv selfupdate --repository <owner/repo>` or set `SelfUpdateRepository` in your config.");
    }

    private async Task<GitHubReleaseResponse> GetReleaseAsync(string repository, string? tag, CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient(ServiceCollectionExtensions.GitHubClientName);
        var url = string.IsNullOrWhiteSpace(tag)
            ? $"repos/{repository}/releases/latest"
            : $"repos/{repository}/releases/tags/{Uri.EscapeDataString(tag)}";
        using var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new JwmvException($"GitHub release lookup failed for '{repository}' with status {(int)response.StatusCode}.");
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GitHubReleaseResponse>(responseStream, cancellationToken: cancellationToken);

        return release ?? throw new JwmvException($"GitHub did not return a valid release document for '{repository}'.");
    }

    private GitHubReleaseAssetResponse SelectReleaseAsset(IReadOnlyList<GitHubReleaseAssetResponse> assets)
    {
        var assetName = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "jwmv-win-x64.zip",
            Architecture.Arm64 => "jwmv-win-arm64.zip",
            _ => throw new JwmvException($"Self-update does not support the current architecture '{RuntimeInformation.ProcessArchitecture}'.")
        };

        var asset = assets.FirstOrDefault(item => string.Equals(item.Name, assetName, StringComparison.OrdinalIgnoreCase));
        return asset ?? throw new JwmvException($"The GitHub release does not contain the expected asset '{assetName}'.");
    }

    private static string GetCurrentVersion() =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

    private static Version? ParseVersion(string? value)
    {
        var normalized = NormalizeVersionString(value);
        return Version.TryParse(normalized, out var version) ? version : null;
    }

    private static string? NormalizeVersionString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = SemVerPattern.Match(value);
        return match.Success ? match.Groups["version"].Value : null;
    }

    private static string FindUpdatedExecutable(string extractionRoot)
    {
        var directPath = Path.Combine(extractionRoot, "jwmv.exe");
        if (File.Exists(directPath))
        {
            return directPath;
        }

        var candidate = Directory.EnumerateFiles(extractionRoot, "jwmv.exe", SearchOption.AllDirectories).FirstOrDefault();
        return candidate ?? throw new JwmvException("The downloaded release asset was extracted, but no jwmv.exe was found inside it.");
    }

    private static string BuildUpdaterScript(
        int currentProcessId,
        string targetExecutablePath,
        string replacementExecutablePath,
        string packagePath,
        string extractRoot,
        bool restartAfterUpdate)
    {
        var target = EscapePowerShell(targetExecutablePath);
        var replacement = EscapePowerShell(replacementExecutablePath);
        var archive = EscapePowerShell(packagePath);
        var extract = EscapePowerShell(extractRoot);
        var restartLine = restartAfterUpdate
            ? "Start-Process -FilePath $target -WorkingDirectory (Split-Path -Parent $target) | Out-Null"
            : string.Empty;

        return $$"""
$ErrorActionPreference = 'Stop'
$target = '{{target}}'
$replacement = '{{replacement}}'
$archive = '{{archive}}'
$extractRoot = '{{extract}}'
$backup = "$target.bak"

for ($attempt = 0; $attempt -lt 80; $attempt++) {
    if (Get-Process -Id {{currentProcessId}} -ErrorAction SilentlyContinue) {
        Start-Sleep -Milliseconds 250
        continue
    }

    try {
        if (-not (Test-Path $backup) -and (Test-Path $target)) {
            Move-Item -LiteralPath $target -Destination $backup -Force
        }

        Copy-Item -LiteralPath $replacement -Destination $target -Force
        if (Test-Path $backup) {
            Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
        }

        Remove-Item -LiteralPath $archive -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $extractRoot -Recurse -Force -ErrorAction SilentlyContinue
        {{restartLine}}
        exit 0
    } catch {
        if (-not (Test-Path $target) -and (Test-Path $backup)) {
            Move-Item -LiteralPath $backup -Destination $target -Force -ErrorAction SilentlyContinue
        }
        Start-Sleep -Milliseconds 250
    }
}

exit 1
""";
    }

    private static void StartDetachedUpdater(string updaterScriptPath)
    {
        var arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{updaterScriptPath}\"";
        foreach (var shell in new[] { "pwsh", "powershell" })
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = shell,
                    Arguments = arguments,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                Process.Start(startInfo);
                return;
            }
            catch
            {
                // Try the next available shell.
            }
        }

        throw new JwmvException("Unable to start PowerShell to finish the self-update.");
    }

    private static string EscapePowerShell(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private sealed record GitHubReleaseResponse
    {
        [JsonPropertyName("tag_name")]
        public required string TagName { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("html_url")]
        public required string HtmlUrl { get; init; }

        [JsonPropertyName("assets")]
        public required List<GitHubReleaseAssetResponse> Assets { get; init; }
    }

    private sealed record GitHubReleaseAssetResponse
    {
        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("browser_download_url")]
        public required string BrowserDownloadUrl { get; init; }
    }

}
