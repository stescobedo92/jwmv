using System.Runtime.InteropServices;
using Jwmv.Core;
using Jwmv.Core.Abstractions;
using Jwmv.Core.Models;
using Jwmv.Infrastructure;
using Jwmv.Infrastructure.Services;

namespace Jwmv.Tests;

public sealed class JavaVersionManagerTests : IDisposable
{
    private readonly string _workspaceRoot = Path.Combine(Path.GetTempPath(), "jwmv-tests", Guid.NewGuid().ToString("n"));

    [Fact]
    public async Task FindProjectConfigurationAsync_FindsNearestJwmvrc()
    {
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, "repo", "nested", "deeper"));
        await File.WriteAllTextAsync(Path.Combine(_workspaceRoot, "repo", JwmvConstants.ProjectFileName), "java=21-tem");

        var manager = CreateManager(Path.Combine(_workspaceRoot, "repo", "nested", "deeper"));

        var projectConfig = await manager.FindProjectConfigurationAsync(null, CancellationToken.None);

        Assert.NotNull(projectConfig);
        Assert.Equal("21-tem", projectConfig!.JavaIdentifier);
        Assert.Equal(Path.Combine(_workspaceRoot, "repo", JwmvConstants.ProjectFileName), projectConfig.FilePath);
    }

    [Fact]
    public async Task BuildEnvShellScriptAsync_UsesProjectVersionWhenInstalled()
    {
        var repoPath = Path.Combine(_workspaceRoot, "repo");
        Directory.CreateDirectory(repoPath);
        await File.WriteAllTextAsync(Path.Combine(repoPath, JwmvConstants.ProjectFileName), "java=21-tem");

        var installed = new InstalledJavaVersion
        {
            Alias = "21.0.4.7-tem",
            Distribution = "temurin",
            DistributionAlias = "tem",
            JavaVersion = "21.0.4+7",
            DistributionVersion = "21.0.4+7",
            InstallDirectory = @"C:\Users\tester\.jwmv\candidates\java\21.0.4.7-tem",
            JavaHome = @"C:\Users\tester\.jwmv\candidates\java\21.0.4.7-tem",
            ArchiveType = "zip",
            PackageFileName = "temurin.zip",
            SourcePackageId = "pkg-1",
            InstalledAtUtc = DateTimeOffset.UtcNow
        };

        var manager = CreateManager(repoPath, [installed]);

        var script = await manager.BuildEnvShellScriptAsync(repoPath, ShellKind.PowerShell, CancellationToken.None);

        Assert.Contains("JAVA_HOME", script, StringComparison.Ordinal);
        Assert.Contains("21.0.4.7-tem", script, StringComparison.Ordinal);
        Assert.Contains("Project", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallAsync_WithFuzzyTrack_InstallsLatestRemoteAliasEvenIfOlderTrackExistsLocally()
    {
        var oldInstalled = new InstalledJavaVersion
        {
            Alias = "21.0.2.13-tem",
            Distribution = "temurin",
            DistributionAlias = "tem",
            JavaVersion = "21.0.2+13",
            DistributionVersion = "21.0.2+13",
            InstallDirectory = Path.Combine(_workspaceRoot, ".jwmv", "candidates", "java", "21.0.2.13-tem"),
            JavaHome = Path.Combine(_workspaceRoot, ".jwmv", "candidates", "java", "21.0.2.13-tem"),
            ArchiveType = "zip",
            PackageFileName = "old.zip",
            SourcePackageId = "old-pkg",
            InstalledAtUtc = DateTimeOffset.UtcNow
        };

        var newPackage = new JavaDistributionPackage
        {
            Id = "new-pkg",
            Alias = "21.0.4.7-tem",
            Distribution = "temurin",
            DistributionAlias = "tem",
            JavaVersion = "21.0.4+7",
            DistributionVersion = "21.0.4+7",
            ArchiveType = "zip",
            Architecture = "x64",
            OperatingSystem = "windows",
            PackageType = "jdk",
            FileName = "new.zip",
            DownloadUri = new Uri("https://example.invalid/new.zip"),
            DirectlyDownloadable = true,
            IsLatestBuildAvailable = true,
            ReleaseStatus = "ga",
            TermOfSupport = "lts",
            Size = 42
        };

        var manager = CreateManager(
            Path.Combine(_workspaceRoot, "repo"),
            [oldInstalled],
            [newPackage],
            createJavaBinaryOnExtract: true);

        var result = await manager.InstallAsync(new InstallJavaRequest
        {
            Identifier = "21-tem"
        }, progress: null, CancellationToken.None);

        Assert.False(result.AlreadyInstalled);
        Assert.Equal("21.0.4.7-tem", result.InstalledVersion.Alias);
    }

    [Fact]
    public async Task BuildUseShellScriptAsync_PersistsSelectedVersionAsGlobalDefault()
    {
        var installed = new InstalledJavaVersion
        {
            Alias = "21.0.4.7-tem",
            Distribution = "temurin",
            DistributionAlias = "tem",
            JavaVersion = "21.0.4+7",
            DistributionVersion = "21.0.4+7",
            InstallDirectory = Path.Combine(_workspaceRoot, ".jwmv", "candidates", "java", "21.0.4.7-tem"),
            JavaHome = Path.Combine(_workspaceRoot, ".jwmv", "candidates", "java", "21.0.4.7-tem"),
            ArchiveType = "zip",
            PackageFileName = "temurin.zip",
            SourcePackageId = "pkg-1",
            InstalledAtUtc = DateTimeOffset.UtcNow
        };

        var configStore = new InMemoryConfigStore();
        var windowsEnvironment = new RecordingWindowsEnvironmentService();
        var manager = CreateManager(
            Path.Combine(_workspaceRoot, "repo"),
            [installed],
            configStore: configStore,
            windowsEnvironmentService: windowsEnvironment);

        var script = await manager.BuildUseShellScriptAsync("21-tem", ShellKind.PowerShell, CancellationToken.None);

        Assert.Contains("JAVA_HOME", script, StringComparison.Ordinal);
        Assert.Contains("Write-Host 'Activated 21.0.4.7-tem for this session and saved it as the global default.'", script, StringComparison.Ordinal);
        Assert.Equal("21.0.4.7-tem", (await configStore.LoadAsync(CancellationToken.None)).DefaultJavaAlias);
        Assert.Equal("21.0.4.7-tem", windowsEnvironment.LastAppliedAlias);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
        {
            Directory.Delete(_workspaceRoot, recursive: true);
        }
    }

    private JavaVersionManager CreateManager(
        string workingDirectory,
        IReadOnlyList<InstalledJavaVersion>? installedVersions = null,
        IReadOnlyList<JavaDistributionPackage>? packages = null,
        bool createJavaBinaryOnExtract = false,
        InMemoryConfigStore? configStore = null,
        RecordingWindowsEnvironmentService? windowsEnvironmentService = null)
    {
        var appContext = new FakeAppContext(_workspaceRoot, workingDirectory);
        var paths = new JwmvPaths(appContext);
        configStore ??= new InMemoryConfigStore();
        var catalogCacheStore = new InMemoryCatalogCacheStore();
        var installationStore = new InMemoryInstallationStore(installedVersions ?? []);
        windowsEnvironmentService ??= new RecordingWindowsEnvironmentService();

        return new JavaVersionManager(
            appContext,
            new FakeClock(),
            configStore,
            catalogCacheStore,
            new FakeCatalogClient(packages ?? []),
            installationStore,
            new NoopArchiveDownloader(),
            new NoopArchiveExtractor(createJavaBinaryOnExtract),
            windowsEnvironmentService,
            paths);
    }

    private sealed class FakeAppContext : IAppContext
    {
        public FakeAppContext(string userProfileDirectory, string workingDirectory)
        {
            UserProfileDirectory = userProfileDirectory;
            WorkingDirectory = workingDirectory;
            ExecutablePath = Path.Combine(workingDirectory, "jwmv.exe");
        }

        public string WorkingDirectory { get; }
        public string UserProfileDirectory { get; }
        public Architecture ProcessArchitecture => Architecture.X64;
        public string ExecutablePath { get; }
        public string? GetEnvironmentVariable(string variableName) => null;
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 4, 7, 15, 0, 0, TimeSpan.Zero);
    }

    private sealed class InMemoryConfigStore : IConfigStore
    {
        private AppConfig _config = new();

        public Task<AppConfig> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(_config);

        public Task SaveAsync(AppConfig config, CancellationToken cancellationToken)
        {
            _config = config;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryCatalogCacheStore : ICatalogCacheStore
    {
        private CatalogCache? _cache;

        public Task<CatalogCache?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(_cache);

        public Task SaveAsync(CatalogCache catalogCache, CancellationToken cancellationToken)
        {
            _cache = catalogCache;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken)
        {
            _cache = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCatalogClient(IReadOnlyList<JavaDistributionPackage> packages) : IJavaCatalogClient
    {
        public Task<IReadOnlyList<JavaDistributionPackage>> GetAvailablePackagesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(packages);
    }

    private sealed class InMemoryInstallationStore(IReadOnlyList<InstalledJavaVersion> initial) : IJavaInstallationStore
    {
        private readonly Dictionary<string, InstalledJavaVersion> _items = initial.ToDictionary(item => item.Alias, StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlyList<InstalledJavaVersion>> GetInstalledVersionsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InstalledJavaVersion>>(_items.Values.ToList());

        public Task<InstalledJavaVersion?> FindByAliasAsync(string alias, CancellationToken cancellationToken)
        {
            _items.TryGetValue(alias, out var item);
            return Task.FromResult(item);
        }

        public Task SaveAsync(InstalledJavaVersion installedVersion, CancellationToken cancellationToken)
        {
            _items[installedVersion.Alias] = installedVersion;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string alias, CancellationToken cancellationToken)
        {
            _items.Remove(alias);
            return Task.CompletedTask;
        }
    }

    private sealed class NoopArchiveDownloader : IArchiveDownloader
    {
        public Task DownloadAsync(Uri downloadUri, string destinationPath, IProgress<ArchiveDownloadProgress>? progress, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoopArchiveExtractor(bool createJavaBinaryOnExtract = false) : IArchiveExtractor
    {
        public Task ExtractZipAsync(string archivePath, string destinationDirectory, IProgress<ArchiveExtractionProgress>? progress, CancellationToken cancellationToken)
        {
            if (createJavaBinaryOnExtract)
            {
                var javaHome = Path.Combine(destinationDirectory, "jdk");
                Directory.CreateDirectory(Path.Combine(javaHome, "bin"));
                File.WriteAllText(Path.Combine(javaHome, "bin", "java.exe"), string.Empty);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingWindowsEnvironmentService : IWindowsEnvironmentService
    {
        public string? LastAppliedAlias { get; private set; }

        public Task ApplyDefaultAsync(InstalledJavaVersion javaVersion, CancellationToken cancellationToken)
        {
            LastAppliedAlias = javaVersion.Alias;
            return Task.CompletedTask;
        }

        public Task ClearDefaultAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
