using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Jwmv.Core;
using Jwmv.Core.Abstractions;
using Jwmv.Core.Exceptions;
using Jwmv.Core.Models;

namespace Jwmv.Infrastructure.Services;

public sealed partial class SelfUpdateService(
    IAppContext appContext,
    IConfigStore configStore,
    IHttpClientFactory httpClientFactory,
    IArchiveDownloader archiveDownloader,
    IArchiveExtractor archiveExtractor,
    IReleaseSignatureVerifier signatureVerifier,
    IUpdaterLauncher updaterLauncher,
    JwmvPaths paths) : ISelfUpdateService
{
    private const int MaxCompanionAssetBytes = 64 * 1024;
    private const string ChecksumAssetSuffix = ".sha256";
    private const string SignatureAssetSuffix = ".sig";
    private const string CertificateAssetSuffix = ".pem";

    private static readonly (string Marker, string Manager, string Command)[] PackageManagerLocations =
    [
        (@"\Microsoft\WinGet\", "winget", "winget upgrade stescobedo92.jwmv"),
        (@"\node_modules\", "npm", "npm update -g @stescobedo9205/jwmv"),
        (@"\.dotnet\tools\", "dotnet tool", "dotnet tool update -g jwmv")
    ];

    public async Task<SelfUpdateCheckResult> CheckForUpdateAsync(SelfUpdateRequest request, CancellationToken cancellationToken)
    {
        var repository = await ResolveRepositoryAsync(request.Repository, cancellationToken);
        var currentVersion = GetCurrentVersion();
        var release = await GetReleaseAsync(repository, request.Tag, cancellationToken);
        var targetVersion = NormalizeVersionString(release.TagName) ?? release.TagName;
        var assetName = GetAssetName(appContext.ProcessArchitecture);
        var asset = FindAsset(release.Assets, assetName)
            ?? throw new JwmvException($"The GitHub release does not contain the expected asset '{assetName}'.");
        var checksumAsset = FindAsset(release.Assets, assetName + ChecksumAssetSuffix)
            ?? throw new IntegrityVerificationException($"The GitHub release does not publish '{assetName}{ChecksumAssetSuffix}', so the download cannot be verified.");
        var signatureAsset = FindAsset(release.Assets, assetName + SignatureAssetSuffix)
            ?? throw new IntegrityVerificationException($"The GitHub release does not publish '{assetName}{SignatureAssetSuffix}', so the download cannot be verified.");
        var certificateAsset = FindAsset(release.Assets, assetName + CertificateAssetSuffix)
            ?? throw new IntegrityVerificationException($"The GitHub release does not publish '{assetName}{CertificateAssetSuffix}', so the download cannot be verified.");

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
            ReleasePageUri = ParseReleasePageUri(release.HtmlUrl, repository),
            AssetName = asset.Name,
            AssetDownloadUri = ParseAssetUri(asset),
            ChecksumAssetUri = ParseAssetUri(checksumAsset),
            SignatureAssetUri = ParseAssetUri(signatureAsset),
            CertificateAssetUri = ParseAssetUri(certificateAsset),
            IsUpdateAvailable = isUpdateAvailable,
            IsForced = request.Force
        };
    }

    public async Task<SelfUpdateResult> ApplyUpdateAsync(SelfUpdateCheckResult checkResult, bool restartAfterUpdate, IProgress<SelfUpdateProgressUpdate>? progress, CancellationToken cancellationToken)
    {
        var currentExecutablePath = appContext.ExecutablePath;
        EnsureSelfUpdatableLocation(currentExecutablePath);

        paths.EnsureCreated();
        var tempRoot = Path.Combine(paths.TempDirectory, "selfupdate", Guid.NewGuid().ToString("n"));
        var extractRoot = Path.Combine(tempRoot, "extract");
        var packagePath = Path.Combine(tempRoot, checkResult.AssetName);
        Directory.CreateDirectory(tempRoot);
        Directory.CreateDirectory(extractRoot);

        try
        {
            progress?.Report(new SelfUpdateProgressUpdate
            {
                Phase = SelfUpdatePhase.Downloading,
                Percentage = 10,
                Status = $"Downloading {checkResult.AssetName}"
            });

            var downloadProgress = new Progress<ArchiveDownloadProgress>(update =>
            {
                var percentage = update.TotalBytes is > 0
                    ? 10 + (update.BytesTransferred / (double)update.TotalBytes.Value * 55d)
                    : 10d;
                progress?.Report(new SelfUpdateProgressUpdate
                {
                    Phase = SelfUpdatePhase.Downloading,
                    Percentage = Math.Min(65d, percentage),
                    Status = $"Downloading {checkResult.AssetName}"
                });
            });

            await archiveDownloader.DownloadAsync(checkResult.AssetDownloadUri, packagePath, downloadProgress, cancellationToken);

            progress?.Report(new SelfUpdateProgressUpdate
            {
                Phase = SelfUpdatePhase.Verifying,
                Percentage = 68,
                Status = $"Verifying {checkResult.AssetName}"
            });

            var checksumText = await ReadCompanionAssetAsync(checkResult.ChecksumAssetUri, cancellationToken);
            var signatureText = await ReadCompanionAssetAsync(checkResult.SignatureAssetUri, cancellationToken);
            var certificateText = await ReadCompanionAssetAsync(checkResult.CertificateAssetUri, cancellationToken);

            VerifyChecksum(packagePath, checksumText, checkResult.AssetName);
            var signatureResult = signatureVerifier.Verify(packagePath, signatureText, certificateText, checkResult.Repository);

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

            var replacementExecutablePath = FindUpdatedExecutable(extractRoot);
            var updaterScriptPath = Path.Combine(tempRoot, "apply-jwmv-update.ps1");
            var updaterLogPath = Path.Combine(tempRoot, "apply-jwmv-update.log");
            var updaterScript = BuildUpdaterScript(
                currentProcessId: Environment.ProcessId,
                targetExecutablePath: currentExecutablePath,
                replacementExecutablePath,
                packagePath,
                extractRoot,
                updaterLogPath,
                checkResult.TargetVersion,
                restartAfterUpdate);
            await File.WriteAllTextAsync(updaterScriptPath, updaterScript, cancellationToken);
            updaterLauncher.Launch(updaterScriptPath);

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
                UpdaterLogPath = updaterLogPath,
                SignerIdentity = signatureResult.SignerIdentity,
                RestartScheduled = restartAfterUpdate
            };
        }
        catch
        {
            TryDeleteDirectory(tempRoot);
            throw;
        }
    }

    private async Task<string> ResolveRepositoryAsync(string? requestedRepository, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requestedRepository))
        {
            return ValidateRepository(requestedRepository, "the --repository option");
        }

        var environmentRepository = appContext.GetEnvironmentVariable(JwmvConstants.SelfUpdateRepositoryVariable);
        if (!string.IsNullOrWhiteSpace(environmentRepository))
        {
            return ValidateRepository(environmentRepository, $"the {JwmvConstants.SelfUpdateRepositoryVariable} environment variable");
        }

        var config = await configStore.LoadAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(config.SelfUpdateRepository))
        {
            return ValidateRepository(config.SelfUpdateRepository, "config.json (SelfUpdateRepository)");
        }

        var assemblyRepository = Assembly.GetEntryAssembly()?
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, "JwmvReleaseRepository", StringComparison.Ordinal))?
            .Value;
        if (!string.IsNullOrWhiteSpace(assemblyRepository))
        {
            return ValidateRepository(assemblyRepository, "the build metadata");
        }

        throw new JwmvException("No GitHub repository is configured for self-update. Run `jwmv selfupdate --repository <owner/repo>` or set `SelfUpdateRepository` in your config.");
    }

    private static string ValidateRepository(string value, string source)
    {
        var repository = value.Trim();
        if (repository.Length > 200 || !RepositoryPattern().IsMatch(repository))
        {
            throw new JwmvException($"'{repository}' (from {source}) is not a valid GitHub repository; expected the form owner/repo.");
        }

        return repository;
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

    private static string GetAssetName(Architecture architecture) => architecture switch
    {
        Architecture.X64 => "jwmv-win-x64.zip",
        Architecture.Arm64 => "jwmv-win-arm64.zip",
        _ => throw new JwmvException($"Self-update does not support the current architecture '{architecture}'.")
    };

    private static GitHubReleaseAssetResponse? FindAsset(IReadOnlyList<GitHubReleaseAssetResponse> assets, string name) =>
        assets.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));

    private static Uri ParseAssetUri(GitHubReleaseAssetResponse asset)
    {
        if (!Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !IsGitHubHost(uri.Host))
        {
            throw new JwmvException($"Release asset '{asset.Name}' is not served from GitHub over HTTPS ({asset.BrowserDownloadUrl}).");
        }

        return uri;
    }

    private static Uri ParseReleasePageUri(string htmlUrl, string repository)
    {
        if (Uri.TryCreate(htmlUrl, UriKind.Absolute, out var uri)
            && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && IsGitHubHost(uri.Host))
        {
            return uri;
        }

        return new Uri($"https://github.com/{repository}/releases");
    }

    private static bool IsGitHubHost(string host) =>
        string.Equals(host, "github.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);

    private async Task<string> ReadCompanionAssetAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient(ServiceCollectionExtensions.GitHubClientName);
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new IntegrityVerificationException($"Unable to download '{uri}' (HTTP {(int)response.StatusCode}); the release cannot be verified.");
        }

        if (response.Content.Headers.ContentLength is > MaxCompanionAssetBytes)
        {
            throw new IntegrityVerificationException($"'{uri}' is larger than expected for a checksum or signature file.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[MaxCompanionAssetBytes + 1];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        if (total > MaxCompanionAssetBytes)
        {
            throw new IntegrityVerificationException($"'{uri}' is larger than expected for a checksum or signature file.");
        }

        return Encoding.UTF8.GetString(buffer, 0, total).Trim();
    }

    private static void VerifyChecksum(string packagePath, string checksumText, string assetName)
    {
        var expected = checksumText
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (expected is null || expected.Length != 64 || !expected.All(Uri.IsHexDigit))
        {
            throw new IntegrityVerificationException($"The published checksum for {assetName} is malformed.");
        }

        string actual;
        using (var stream = File.OpenRead(packagePath))
        {
            actual = Convert.ToHexString(SHA256.HashData(stream));
        }

        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new IntegrityVerificationException($"SHA-256 mismatch for {assetName}: expected {expected.ToLowerInvariant()}, got {actual.ToLowerInvariant()}.");
        }
    }

    private static void EnsureSelfUpdatableLocation(string executablePath)
    {
        var normalized = Path.GetFullPath(executablePath);
        foreach (var (marker, manager, command) in PackageManagerLocations)
        {
            if (normalized.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                throw new JwmvException($"jwmv at '{executablePath}' is managed by {manager}. Update it with `{command}` instead of `jwmv selfupdate` so the package manager stays in sync.");
            }
        }
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

        var match = SemVerPattern().Match(value);
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
        string logPath,
        string expectedVersion,
        bool restartAfterUpdate)
    {
        var target = EscapePowerShell(targetExecutablePath);
        var replacement = EscapePowerShell(replacementExecutablePath);
        var archive = EscapePowerShell(packagePath);
        var extract = EscapePowerShell(extractRoot);
        var log = EscapePowerShell(logPath);
        var version = EscapePowerShell(expectedVersion);
        var restartLine = restartAfterUpdate
            ? "Start-Process -FilePath $target -WorkingDirectory (Split-Path -Parent $target) | Out-Null"
            : string.Empty;
        var processId = currentProcessId.ToString(CultureInfo.InvariantCulture);

        return $$"""
$ErrorActionPreference = 'Stop'
$target = '{{target}}'
$replacement = '{{replacement}}'
$archive = '{{archive}}'
$extractRoot = '{{extract}}'
$logPath = '{{log}}'
$expectedVersion = '{{version}}'
$staged = "$target.new"
$backup = "$target.bak"

function Write-UpdateLog([string] $message) {
    Add-Content -LiteralPath $logPath -Value ("{0:o} {1}" -f (Get-Date), $message)
}

function Invoke-WithRetry([scriptblock] $action, [string] $description) {
    for ($attempt = 1; $attempt -le 40; $attempt++) {
        try {
            & $action
            return
        } catch {
            if ($attempt -eq 40) { throw }
            Start-Sleep -Milliseconds 250
        }
    }
}

try {
    Write-UpdateLog "Waiting for jwmv process {{processId}} to exit"
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Process -Id {{processId}} -ErrorAction SilentlyContinue) -and ((Get-Date) -lt $deadline)) {
        Start-Sleep -Milliseconds 250
    }
    if (Get-Process -Id {{processId}} -ErrorAction SilentlyContinue) {
        throw "jwmv (process {{processId}}) is still running; the update was not applied."
    }

    Write-UpdateLog "Staging $replacement as $staged"
    Copy-Item -LiteralPath $replacement -Destination $staged -Force
    if (Test-Path -LiteralPath $backup) {
        Remove-Item -LiteralPath $backup -Force
    }

    Write-UpdateLog "Swapping $target"
    Invoke-WithRetry { Move-Item -LiteralPath $target -Destination $backup -Force } 'backup current executable'
    try {
        Invoke-WithRetry { Move-Item -LiteralPath $staged -Destination $target -Force } 'move staged executable into place'
    } catch {
        Move-Item -LiteralPath $backup -Destination $target -Force
        throw
    }

    $reported = (& $target --version 2>&1 | Out-String).Trim()
    Write-UpdateLog "Installed executable reports: $reported"
    if ($reported -notlike "*$expectedVersion*") {
        Write-UpdateLog "Version check failed; restoring backup"
        Move-Item -LiteralPath $target -Destination "$target.failed" -Force
        Move-Item -LiteralPath $backup -Destination $target -Force
        Remove-Item -LiteralPath "$target.failed" -Force -ErrorAction SilentlyContinue
        throw "The updated executable reported '$reported' instead of version $expectedVersion; the previous version was restored."
    }

    Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $archive -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $extractRoot -Recurse -Force -ErrorAction SilentlyContinue
    Write-UpdateLog "Update to $expectedVersion completed"
    {{restartLine}}
    exit 0
} catch {
    Write-UpdateLog "Update failed: $($_.Exception.Message)"
    exit 1
}
""";
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of the temporary download directory.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup of the temporary download directory.
        }
    }

    private static string EscapePowerShell(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    [GeneratedRegex(@"(?<version>\d+\.\d+\.\d+)", RegexOptions.NonBacktracking, matchTimeoutMilliseconds: 100)]
    private static partial Regex SemVerPattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*/[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.NonBacktracking, matchTimeoutMilliseconds: 100)]
    private static partial Regex RepositoryPattern();

    private sealed record GitHubReleaseResponse
    {
        [JsonPropertyName("tag_name")]
        public required string TagName { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; init; } = string.Empty;

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
