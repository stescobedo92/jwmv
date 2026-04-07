using Jwmv.Core;
using Jwmv.Core.Abstractions;
using Jwmv.Core.Exceptions;
using Jwmv.Core.Models;
using Jwmv.Core.Utilities;

namespace Jwmv.Infrastructure.Services;

public sealed class JavaVersionManager(
    IAppContext appContext,
    IClock clock,
    IConfigStore configStore,
    ICatalogCacheStore catalogCacheStore,
    IJavaCatalogClient javaCatalogClient,
    IJavaInstallationStore installationStore,
    IArchiveDownloader archiveDownloader,
    IArchiveExtractor archiveExtractor,
    IWindowsEnvironmentService windowsEnvironmentService,
    JwmvPaths paths) : IJavaVersionManager
{
    public async Task<IReadOnlyList<JavaDistributionPackage>> ListAvailableAsync(JavaCatalogQuery query, CancellationToken cancellationToken)
    {
        var packages = await GetCatalogAsync(query.ForceRefresh, cancellationToken);
        return FilterAndSortPackages(packages, query.IdentifierFilter);
    }

    public Task<IReadOnlyList<InstalledJavaVersion>> ListInstalledAsync(CancellationToken cancellationToken) =>
        installationStore.GetInstalledVersionsAsync(cancellationToken);

    public async Task<InstallJavaResult> InstallAsync(InstallJavaRequest request, IProgress<InstallProgressUpdate>? progress, CancellationToken cancellationToken)
    {
        paths.EnsureCreated();
        progress?.Report(new InstallProgressUpdate
        {
            Phase = InstallPhase.Resolving,
            Percentage = 2,
            Status = $"Resolving {request.Identifier}"
        });

        var exactInstalled = await installationStore.FindByAliasAsync(request.Identifier, cancellationToken);
        if (exactInstalled is not null)
        {
            var result = new InstallJavaResult
            {
                InstalledVersion = exactInstalled,
                AlreadyInstalled = true,
                DefaultWasUpdated = false
            };

            if (request.SetAsDefault)
            {
                await SetDefaultInternalAsync(exactInstalled, cancellationToken);
                return result with { DefaultWasUpdated = true };
            }

            return result;
        }

        var packages = await GetCatalogAsync(request.ForceCatalogRefresh, cancellationToken);
        var package = ResolveBestPackage(packages, request.Identifier);
        progress?.Report(new InstallProgressUpdate
        {
            Phase = InstallPhase.Resolving,
            Percentage = 8,
            Status = $"Selected {package.Alias}"
        });

        var installed = await installationStore.FindByAliasAsync(package.Alias, cancellationToken);
        if (installed is not null)
        {
            var alreadyInstalledResult = new InstallJavaResult
            {
                InstalledVersion = installed,
                AlreadyInstalled = true,
                DefaultWasUpdated = false
            };

            if (request.SetAsDefault)
            {
                await SetDefaultInternalAsync(installed, cancellationToken);
                return alreadyInstalledResult with { DefaultWasUpdated = true };
            }

            return alreadyInstalledResult;
        }

        var archivePath = Path.Combine(paths.ArchivesDirectory, package.FileName);
        if (!File.Exists(archivePath))
        {
            progress?.Report(new InstallProgressUpdate
            {
                Phase = InstallPhase.Downloading,
                Percentage = 10,
                Status = $"Downloading {package.FileName}"
            });

            var downloadProgress = new Progress<ArchiveDownloadProgress>(update =>
            {
                var totalBytes = update.TotalBytes;
                var percentage = totalBytes is > 0
                    ? 10 + (update.BytesTransferred / (double)totalBytes.Value * 70d)
                    : 10;
                progress?.Report(new InstallProgressUpdate
                {
                    Phase = InstallPhase.Downloading,
                    Percentage = Math.Min(80, percentage),
                    Status = $"Downloading {package.FileName}"
                });
            });

            await archiveDownloader.DownloadAsync(package.DownloadUri, archivePath, downloadProgress, cancellationToken);
        }
        else
        {
            progress?.Report(new InstallProgressUpdate
            {
                Phase = InstallPhase.Downloading,
                Percentage = 80,
                Status = $"Using cached archive {package.FileName}"
            });
        }

        var extractionRoot = Path.Combine(paths.TempDirectory, Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(extractionRoot);

        try
        {
            progress?.Report(new InstallProgressUpdate
            {
                Phase = InstallPhase.Extracting,
                Percentage = 82,
                Status = $"Extracting {package.FileName}"
            });

            var extractionProgress = new Progress<ArchiveExtractionProgress>(update =>
            {
                var fraction = update.TotalEntries > 0
                    ? update.EntriesProcessed / (double)update.TotalEntries
                    : 0d;
                progress?.Report(new InstallProgressUpdate
                {
                    Phase = InstallPhase.Extracting,
                    Percentage = 82 + (fraction * 13d),
                    Status = $"Extracting {package.FileName}"
                });
            });

            await archiveExtractor.ExtractZipAsync(archivePath, extractionRoot, extractionProgress, cancellationToken);

            var javaHome = FindJavaHome(extractionRoot);
            var finalDirectory = Path.Combine(paths.JavaCandidatesDirectory, package.Alias);
            progress?.Report(new InstallProgressUpdate
            {
                Phase = InstallPhase.Finalizing,
                Percentage = 96,
                Status = $"Finalizing {package.Alias}"
            });

            if (Directory.Exists(finalDirectory))
            {
                Directory.Delete(finalDirectory, recursive: true);
            }

            Directory.Move(javaHome, finalDirectory);

            var installedVersion = new InstalledJavaVersion
            {
                Alias = package.Alias,
                Distribution = package.Distribution,
                DistributionAlias = package.DistributionAlias,
                JavaVersion = package.JavaVersion,
                DistributionVersion = package.DistributionVersion,
                InstallDirectory = finalDirectory,
                JavaHome = finalDirectory,
                ArchiveType = package.ArchiveType,
                PackageFileName = package.FileName,
                SourcePackageId = package.Id,
                InstalledAtUtc = clock.UtcNow
            };

            await installationStore.SaveAsync(installedVersion, cancellationToken);
            var defaultWasUpdated = false;
            if (request.SetAsDefault)
            {
                await SetDefaultInternalAsync(installedVersion, cancellationToken);
                defaultWasUpdated = true;
            }

            progress?.Report(new InstallProgressUpdate
            {
                Phase = InstallPhase.Completed,
                Percentage = 100,
                Status = $"Installed {installedVersion.Alias}"
            });

            return new InstallJavaResult
            {
                InstalledVersion = installedVersion,
                AlreadyInstalled = false,
                DefaultWasUpdated = defaultWasUpdated
            };
        }
        finally
        {
            if (Directory.Exists(extractionRoot))
            {
                Directory.Delete(extractionRoot, recursive: true);
            }
        }
    }

    public async Task UninstallAsync(string identifier, CancellationToken cancellationToken)
    {
        var installed = await ResolveInstalledAsync(identifier, cancellationToken)
            ?? throw new JavaNotInstalledException(identifier);

        var config = await configStore.LoadAsync(cancellationToken);
        if (string.Equals(config.DefaultJavaAlias, installed.Alias, StringComparison.OrdinalIgnoreCase))
        {
            await configStore.SaveAsync(config with { DefaultJavaAlias = null }, cancellationToken);
            await windowsEnvironmentService.ClearDefaultAsync(cancellationToken);
        }

        if (Directory.Exists(installed.InstallDirectory))
        {
            Directory.Delete(installed.InstallDirectory, recursive: true);
        }

        var archivePath = Path.Combine(paths.ArchivesDirectory, installed.PackageFileName);
        if (!string.IsNullOrWhiteSpace(installed.PackageFileName) && File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        await installationStore.DeleteAsync(installed.Alias, cancellationToken);
    }

    public async Task<InstalledJavaVersion> SetDefaultAsync(string identifier, CancellationToken cancellationToken)
    {
        var installed = await ResolveInstalledAsync(identifier, cancellationToken)
            ?? throw new JavaNotInstalledException(identifier);

        await SetDefaultInternalAsync(installed, cancellationToken);
        return installed;
    }

    public async Task<ActiveJavaSelection> ResolveCurrentAsync(string? workingDirectory, CancellationToken cancellationToken)
    {
        var sessionAlias = appContext.GetEnvironmentVariable(JwmvConstants.SessionAliasVariable);
        if (!string.IsNullOrWhiteSpace(sessionAlias))
        {
            var installed = await installationStore.FindByAliasAsync(sessionAlias, cancellationToken);
            if (installed is not null)
            {
                var source = Enum.TryParse<JavaActivationSource>(appContext.GetEnvironmentVariable(JwmvConstants.SessionSourceVariable), true, out var parsed)
                    ? parsed
                    : JavaActivationSource.Session;

                return ToActiveSelection(installed, source);
            }
        }

        var projectConfig = await FindProjectConfigurationAsync(workingDirectory, cancellationToken);
        if (projectConfig is not null)
        {
            var installedProjectJava = await ResolveInstalledAsync(projectConfig.JavaIdentifier, cancellationToken);
            if (installedProjectJava is not null)
            {
                return ToActiveSelection(installedProjectJava, JavaActivationSource.Project) with
                {
                    ProjectFilePath = projectConfig.FilePath
                };
            }

            return new ActiveJavaSelection
            {
                Alias = projectConfig.JavaIdentifier,
                Source = JavaActivationSource.Project,
                ProjectFilePath = projectConfig.FilePath
            };
        }

        var config = await configStore.LoadAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(config.DefaultJavaAlias))
        {
            var defaultJava = await installationStore.FindByAliasAsync(config.DefaultJavaAlias, cancellationToken);
            if (defaultJava is not null)
            {
                return ToActiveSelection(defaultJava, JavaActivationSource.Default);
            }
        }

        return new ActiveJavaSelection { Source = JavaActivationSource.None };
    }

    public async Task<string?> GetJavaHomeAsync(string? identifier, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return (await ResolveCurrentAsync(appContext.WorkingDirectory, cancellationToken)).JavaHome;
        }

        var installed = await ResolveInstalledAsync(identifier, cancellationToken)
            ?? throw new JavaNotInstalledException(identifier);
        return installed.JavaHome;
    }

    public async Task<string> BuildUseShellScriptAsync(string identifier, ShellKind shellKind, CancellationToken cancellationToken)
    {
        if (shellKind != ShellKind.PowerShell)
        {
            throw new JwmvException("Only PowerShell shell integration is implemented.");
        }

        var installed = await ResolveInstalledAsync(identifier, cancellationToken)
            ?? throw new JavaNotInstalledException(identifier);
        await SetDefaultInternalAsync(installed, cancellationToken);
        return BuildActivationScript(
            installed,
            JavaActivationSource.Session,
            emitConfirmationMessage: true,
            persistAsDefault: true);
    }

    public async Task<string> BuildEnvShellScriptAsync(string? workingDirectory, ShellKind shellKind, CancellationToken cancellationToken)
    {
        if (shellKind != ShellKind.PowerShell)
        {
            throw new JwmvException("Only PowerShell shell integration is implemented.");
        }

        var projectConfig = await FindProjectConfigurationAsync(workingDirectory, cancellationToken);
        if (projectConfig is not null)
        {
            var installedProjectJava = await ResolveInstalledAsync(projectConfig.JavaIdentifier, cancellationToken);
            if (installedProjectJava is null)
            {
                throw new JavaNotInstalledException(projectConfig.JavaIdentifier);
            }

            return BuildActivationScript(installedProjectJava, JavaActivationSource.Project);
        }

        var currentSource = appContext.GetEnvironmentVariable(JwmvConstants.SessionSourceVariable);
        if (string.Equals(currentSource, nameof(JavaActivationSource.Project), StringComparison.OrdinalIgnoreCase))
        {
            var config = await configStore.LoadAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(config.DefaultJavaAlias))
            {
                var defaultJava = await installationStore.FindByAliasAsync(config.DefaultJavaAlias, cancellationToken);
                if (defaultJava is not null)
                {
                    return BuildActivationScript(defaultJava, JavaActivationSource.Default);
                }
            }

            return BuildClearScript();
        }

        return string.Empty;
    }

    public Task<string> BuildShellInitScriptAsync(ShellKind shellKind, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (shellKind != ShellKind.PowerShell)
        {
            throw new JwmvException("Only PowerShell shell integration is implemented.");
        }

        var executable = EscapePowerShell(appContext.ExecutablePath);
        var script = $$"""
$script:jwmvExe = '{{executable}}'
$env:{{JwmvConstants.ShellIntegrationVariable}} = '1'

function global:__jwmv_apply_default {
    $defaultBin = $env:{{JwmvConstants.DefaultBinVariable}}
    if (-not $defaultBin) {
        return
    }

    $env:Path = $defaultBin + ';' + (@($env:Path -split ';' | Where-Object { $_ -and $_ -ne $defaultBin }) -join ';')
    if ($env:{{JwmvConstants.DefaultHomeVariable}}) {
        $env:JAVA_HOME = $env:{{JwmvConstants.DefaultHomeVariable}}
    }
}

function global:jwmv {
    param([Parameter(ValueFromRemainingArguments = $true)] [string[]]$JwmvArgs)

    if ($JwmvArgs.Length -gt 0 -and @('use', 'env') -contains $JwmvArgs[0].ToLowerInvariant()) {
        $result = & $script:jwmvExe @JwmvArgs --shell powershell
        if ($LASTEXITCODE -eq 0 -and $result) {
            Invoke-Expression ($result -join [Environment]::NewLine)
        }
        return
    }

    & $script:jwmvExe @JwmvArgs
}

if (-not (Test-Path function:\global:__jwmv_original_prompt)) {
    $function:global:__jwmv_original_prompt = $function:prompt
}

__jwmv_apply_default

function global:prompt {
    $result = & $script:jwmvExe env --shell powershell --cwd (Get-Location).Path
    if ($LASTEXITCODE -eq 0 -and $result) {
        Invoke-Expression ($result -join [Environment]::NewLine)
    }

    & $function:global:__jwmv_original_prompt
}
""";

        return Task.FromResult(script);
    }

    public async Task<ProjectJavaConfiguration?> FindProjectConfigurationAsync(string? workingDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? appContext.WorkingDirectory : workingDirectory;
        var current = new DirectoryInfo(startDirectory);

        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, JwmvConstants.ProjectFileName);
            if (File.Exists(candidate))
            {
                var javaIdentifier = await ParseProjectJavaIdentifierAsync(candidate, cancellationToken);
                if (string.IsNullOrWhiteSpace(javaIdentifier))
                {
                    throw new ProjectConfigurationException($"The project file '{candidate}' does not contain a valid 'java=' entry.");
                }

                return new ProjectJavaConfiguration
                {
                    JavaIdentifier = javaIdentifier,
                    FilePath = candidate
                };
            }

            current = current.Parent;
        }

        return null;
    }

    public async Task RefreshCatalogAsync(CancellationToken cancellationToken)
    {
        var packages = await javaCatalogClient.GetAvailablePackagesAsync(cancellationToken);
        await catalogCacheStore.SaveAsync(new CatalogCache
        {
            RefreshedAtUtc = clock.UtcNow,
            Packages = packages
        }, cancellationToken);
    }

    public Task<AppConfig> GetConfigAsync(CancellationToken cancellationToken) =>
        configStore.LoadAsync(cancellationToken);

    public async Task FlushAsync(FlushRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.IncludeCatalog)
        {
            await catalogCacheStore.ClearAsync(cancellationToken);
        }

        if (request.IncludeArchives && Directory.Exists(paths.ArchivesDirectory))
        {
            Directory.Delete(paths.ArchivesDirectory, recursive: true);
            Directory.CreateDirectory(paths.ArchivesDirectory);
        }

        if (request.IncludeTemp && Directory.Exists(paths.TempDirectory))
        {
            Directory.Delete(paths.TempDirectory, recursive: true);
            Directory.CreateDirectory(paths.TempDirectory);
        }
    }

    private async Task SetDefaultInternalAsync(InstalledJavaVersion installedJavaVersion, CancellationToken cancellationToken)
    {
        var config = await configStore.LoadAsync(cancellationToken);
        await configStore.SaveAsync(config with { DefaultJavaAlias = installedJavaVersion.Alias }, cancellationToken);
        await windowsEnvironmentService.ApplyDefaultAsync(installedJavaVersion, cancellationToken);
    }

    private async Task<IReadOnlyList<JavaDistributionPackage>> GetCatalogAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        paths.EnsureCreated();
        var config = await configStore.LoadAsync(cancellationToken);
        var cache = await catalogCacheStore.LoadAsync(cancellationToken);
        var isFresh = cache is not null && (clock.UtcNow - cache.RefreshedAtUtc) < TimeSpan.FromHours(config.CatalogRefreshHours);
        if (!forceRefresh && isFresh)
        {
            return cache!.Packages;
        }

        var refreshed = await javaCatalogClient.GetAvailablePackagesAsync(cancellationToken);
        var newCache = new CatalogCache
        {
            RefreshedAtUtc = clock.UtcNow,
            Packages = refreshed
        };

        await catalogCacheStore.SaveAsync(newCache, cancellationToken);
        return refreshed;
    }

    private static IReadOnlyList<JavaDistributionPackage> FilterAndSortPackages(IReadOnlyList<JavaDistributionPackage> packages, string? identifier)
    {
        var filtered = string.IsNullOrWhiteSpace(identifier)
            ? packages
            : packages.Where(package => JavaIdentifier.Matches(package.Alias, identifier)).ToList();

        return filtered
            .OrderBy(package => package.Alias, Comparer<string>.Create(JavaIdentifier.CompareAliasesDescending))
            .ToList();
    }

    private async Task<InstalledJavaVersion?> ResolveInstalledAsync(string identifier, CancellationToken cancellationToken)
    {
        var installed = await installationStore.GetInstalledVersionsAsync(cancellationToken);
        return installed
            .Where(item => JavaIdentifier.Matches(item.Alias, identifier))
            .OrderBy(item => item.Alias, Comparer<string>.Create(JavaIdentifier.CompareAliasesDescending))
            .FirstOrDefault();
    }

    private static JavaDistributionPackage ResolveBestPackage(IReadOnlyList<JavaDistributionPackage> packages, string identifier)
    {
        var package = packages
            .Where(item => JavaIdentifier.Matches(item.Alias, identifier))
            .OrderBy(item => item.Alias, Comparer<string>.Create(JavaIdentifier.CompareAliasesDescending))
            .FirstOrDefault();

        return package ?? throw new JavaPackageNotFoundException(identifier);
    }

    private static string FindJavaHome(string extractionRoot)
    {
        if (HasJavaBinary(extractionRoot))
        {
            return extractionRoot;
        }

        var candidates = Directory.EnumerateDirectories(extractionRoot, "*", SearchOption.AllDirectories)
            .Where(HasJavaBinary)
            .OrderBy(path => path.Length)
            .ToList();

        return candidates.FirstOrDefault()
            ?? throw new InvalidOperationException("The downloaded archive was extracted successfully, but no Java home could be discovered.");
    }

    private static bool HasJavaBinary(string directoryPath) =>
        File.Exists(Path.Combine(directoryPath, "bin", "java.exe"));

    private static ActiveJavaSelection ToActiveSelection(InstalledJavaVersion installedVersion, JavaActivationSource source) =>
        new()
        {
            Alias = installedVersion.Alias,
            JavaHome = installedVersion.JavaHome,
            BinDirectory = Path.Combine(installedVersion.JavaHome, "bin"),
            Source = source
        };

    private static string BuildActivationScript(
        InstalledJavaVersion installedVersion,
        JavaActivationSource source,
        bool emitConfirmationMessage = false,
        bool persistAsDefault = false)
    {
        var javaHome = EscapePowerShell(installedVersion.JavaHome);
        var binDirectory = EscapePowerShell(Path.Combine(installedVersion.JavaHome, "bin"));
        var alias = EscapePowerShell(installedVersion.Alias);
        var sourceText = EscapePowerShell(source.ToString());
        var displayAlias = EscapePowerShell(installedVersion.DisplayAlias);
        var confirmationLine = emitConfirmationMessage
            ? persistAsDefault
                ? $"Write-Host 'Activated {displayAlias} for this session and saved it as the global default.' -ForegroundColor Green"
                : $"Write-Host 'Activated {displayAlias} for this session.' -ForegroundColor Green"
            : string.Empty;

        return $$"""
$__jwmvPreviousBin = $env:{{JwmvConstants.SessionBinVariable}}
if ($__jwmvPreviousBin) {
    $env:Path = @($env:Path -split ';' | Where-Object { $_ -and $_ -ne $__jwmvPreviousBin }) -join ';'
}

$env:JAVA_HOME = '{{javaHome}}'
$env:{{JwmvConstants.SessionAliasVariable}} = '{{alias}}'
$env:{{JwmvConstants.SessionHomeVariable}} = '{{javaHome}}'
$env:{{JwmvConstants.SessionBinVariable}} = '{{binDirectory}}'
$env:{{JwmvConstants.SessionSourceVariable}} = '{{sourceText}}'
$env:Path = '{{binDirectory}};' + (@($env:Path -split ';' | Where-Object { $_ -and $_ -ne '{{binDirectory}}' }) -join ';')
{{confirmationLine}}
""";
    }

    private static string BuildClearScript() =>
        $$"""
$__jwmvPreviousBin = $env:{{JwmvConstants.SessionBinVariable}}
if ($__jwmvPreviousBin) {
    $env:Path = @($env:Path -split ';' | Where-Object { $_ -and $_ -ne $__jwmvPreviousBin }) -join ';'
}

Remove-Item Env:\{{JwmvConstants.SessionAliasVariable}} -ErrorAction SilentlyContinue
Remove-Item Env:\{{JwmvConstants.SessionHomeVariable}} -ErrorAction SilentlyContinue
Remove-Item Env:\{{JwmvConstants.SessionBinVariable}} -ErrorAction SilentlyContinue
Remove-Item Env:\{{JwmvConstants.SessionSourceVariable}} -ErrorAction SilentlyContinue
""";

    private static string EscapePowerShell(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static async Task<string?> ParseProjectJavaIdentifierAsync(string filePath, CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && string.Equals(parts[0], JwmvConstants.CandidateName, StringComparison.OrdinalIgnoreCase))
            {
                return parts[1];
            }
        }

        return null;
    }
}
