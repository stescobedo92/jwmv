using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Jwmv.Core;
using Jwmv.Core.Abstractions;
using Jwmv.Core.Exceptions;
using Jwmv.Core.Models;
using Jwmv.Infrastructure;
using Jwmv.Infrastructure.Net;
using Jwmv.Infrastructure.Services;

namespace Jwmv.Tests;

public sealed class SdkVersionManagerTests : IDisposable
{
    private readonly string _workspaceRoot = Path.Combine(Path.GetTempPath(), "jwmv-sdk-tests", Guid.NewGuid().ToString("n"));

    [Fact]
    public async Task FindProjectConfigurationAsync_ParsesMultiCandidateFile()
    {
        var repo = Path.Combine(_workspaceRoot, "repo");
        Directory.CreateDirectory(repo);
        await File.WriteAllTextAsync(Path.Combine(repo, JwmvConstants.ProjectFileName), """
java=21-tem
gradle=9.5.1
maven=3.9.15
kotlin=2.3.21
""");
        var manager = CreateManager(repo);

        var config = await manager.FindProjectConfigurationAsync(repo, CancellationToken.None);

        Assert.NotNull(config);
        Assert.Equal("21-tem", config!.GetVersion("java"));
        Assert.Equal("9.5.1", config.GetVersion("gradle"));
        Assert.Equal("3.9.15", config.GetVersion("maven"));
        Assert.Equal("2.3.21", config.GetVersion("kotlin"));
    }

    [Fact]
    public async Task FindProjectConfigurationAsync_ParsesLegacyJavaOnlyFile()
    {
        var repo = Path.Combine(_workspaceRoot, "repo");
        Directory.CreateDirectory(repo);
        await File.WriteAllTextAsync(Path.Combine(repo, JwmvConstants.ProjectFileName), "21-tem");
        var manager = CreateManager(repo);

        var config = await manager.FindProjectConfigurationAsync(repo, CancellationToken.None);

        Assert.NotNull(config);
        Assert.Equal("21-tem", config!.GetVersion("java"));
    }

    [Theory]
    [InlineData("java=../../evil")]
    [InlineData(@"java=..\..\evil")]
    [InlineData(@"java=C:\attacker\evil")]
    [InlineData(@"java=\\attacker\share\evil")]
    [InlineData("../../evil")]
    [InlineData("java=21-tem'; Remove-Item -Recurse $HOME")]
    public async Task FindProjectConfigurationAsync_RejectsUnsafeVersionValues(string content)
    {
        var repo = Path.Combine(_workspaceRoot, "repo");
        Directory.CreateDirectory(repo);
        await File.WriteAllTextAsync(Path.Combine(repo, JwmvConstants.ProjectFileName), content);
        var manager = CreateManager(repo);

        await Assert.ThrowsAsync<ProjectConfigurationException>(() => manager.FindProjectConfigurationAsync(repo, CancellationToken.None));
    }

    [Theory]
    [InlineData("../java=21-tem")]
    [InlineData("java;evil=21-tem")]
    public async Task FindProjectConfigurationAsync_RejectsUnsafeCandidateKeys(string content)
    {
        var repo = Path.Combine(_workspaceRoot, "repo");
        Directory.CreateDirectory(repo);
        await File.WriteAllTextAsync(Path.Combine(repo, JwmvConstants.ProjectFileName), content);
        var manager = CreateManager(repo);

        await Assert.ThrowsAsync<ProjectConfigurationException>(() => manager.FindProjectConfigurationAsync(repo, CancellationToken.None));
    }

    [Theory]
    [InlineData("java=", 1)]
    [InlineData("=21-tem", 1)]
    [InlineData("gradle=9.5.1\njava=", 2)]
    public async Task FindProjectConfigurationAsync_RejectsEntriesWithABlankKeyOrValue(string content, int lineNumber)
    {
        var repo = Path.Combine(_workspaceRoot, "repo");
        Directory.CreateDirectory(repo);
        await File.WriteAllTextAsync(Path.Combine(repo, JwmvConstants.ProjectFileName), content);
        var manager = CreateManager(repo);

        var exception = await Assert.ThrowsAsync<ProjectConfigurationException>(() => manager.FindProjectConfigurationAsync(repo, CancellationToken.None));
        Assert.Contains($"line {lineNumber}", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildUseShellScriptAsync_DoesNotPersistDefault()
    {
        var installed = CreateInstalled("java", "21.0.4-tem", "java.exe");
        var configStore = new InMemoryConfigStore();
        var environment = new RecordingSdkEnvironmentService();
        var manager = CreateManager(Path.Combine(_workspaceRoot, "repo"), installed: [installed], configStore: configStore, environment: environment);

        var script = await manager.BuildUseShellScriptAsync("java", "21.0.4-tem", ShellKind.PowerShell, CancellationToken.None);

        Assert.Contains("JWMV_ACTIVE_JAVA_VERSION", script, StringComparison.Ordinal);
        Assert.Null(environment.LastApplied);
        Assert.Empty((await configStore.LoadAsync(CancellationToken.None)).DefaultVersions);
    }

    [Fact]
    public async Task BuildUseShellScriptAsync_UsesProviderHomeVariableInsteadOfManifestValue()
    {
        var installed = CreateInstalled("java", "21.0.4-tem", "java.exe") with
        {
            HomeEnvironmentVariable = "JAVA_HOME = 'x'; Remove-Item -Recurse $HOME #"
        };
        var manager = CreateManager(Path.Combine(_workspaceRoot, "repo"), installed: [installed]);

        var script = await manager.BuildUseShellScriptAsync("java", "21.0.4-tem", ShellKind.PowerShell, CancellationToken.None);

        Assert.Contains("$env:JAVA_HOME = '", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove-Item", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildEnvShellScriptAsync_UsesProviderHomeVariableInsteadOfManifestValue()
    {
        var repo = Path.Combine(_workspaceRoot, "repo");
        Directory.CreateDirectory(repo);
        await File.WriteAllTextAsync(Path.Combine(repo, JwmvConstants.ProjectFileName), "java=21.0.4-tem");
        var installed = CreateInstalled("java", "21.0.4-tem", "java.exe") with
        {
            HomeEnvironmentVariable = "EVIL; Remove-Item -Recurse $HOME #"
        };
        var manager = CreateManager(repo, installed: [installed]);

        var script = await manager.BuildEnvShellScriptAsync(repo, ShellKind.PowerShell, CancellationToken.None);

        Assert.Contains("$env:JAVA_HOME = '", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove-Item", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetDefaultAsync_PersistsOnlySelectedCandidate()
    {
        var installed = CreateInstalled("gradle", "9.5.1", "gradle.bat");
        var configStore = new InMemoryConfigStore();
        var environment = new RecordingSdkEnvironmentService();
        var manager = CreateManager(
            Path.Combine(_workspaceRoot, "repo"),
            installed: [installed],
            providers: [new FakeProvider("gradle", "gradle.bat", [CreatePackage("gradle", "9.5.1")])],
            configStore: configStore,
            environment: environment);

        await manager.SetDefaultAsync("gradle", "9.5.1", CancellationToken.None);

        var config = await configStore.LoadAsync(CancellationToken.None);
        Assert.Equal("9.5.1", config.DefaultVersions["gradle"]);
        Assert.Equal("gradle", environment.LastApplied?.CandidateName);
    }

    [Fact]
    public async Task InstallAsync_InvalidChecksumFailsBeforeExtraction()
    {
        var package = CreatePackage("gradle", "9.5.1") with
        {
            Checksum = new string('0', 64),
            ChecksumType = "sha256"
        };
        var extractor = new RecordingArchiveExtractor();
        var manager = CreateManager(
            Path.Combine(_workspaceRoot, "repo"),
            providers: [new FakeProvider("gradle", "gradle.bat", [package])],
            downloader: new WritingArchiveDownloader("not-the-expected-content"),
            extractor: extractor);

        await Assert.ThrowsAnyAsync<JwmvException>(() => manager.InstallAsync(new InstallSdkRequest
        {
            CandidateName = "gradle",
            Version = "9.5.1"
        }, progress: null, CancellationToken.None));
        Assert.False(extractor.WasCalled);
    }

    [Fact]
    public async Task InstallAsync_WithoutChecksumFailsClosed()
    {
        var extractor = new RecordingArchiveExtractor(createExecutables: true);
        var manager = CreateManager(
            Path.Combine(_workspaceRoot, "repo"),
            providers: [new FakeProvider("gradle", "gradle.bat", [CreatePackage("gradle", "9.5.1")])],
            extractor: extractor);

        await Assert.ThrowsAsync<IntegrityVerificationException>(() => manager.InstallAsync(new InstallSdkRequest
        {
            CandidateName = "gradle",
            Version = "9.5.1"
        }, progress: null, CancellationToken.None));
        Assert.False(extractor.WasCalled);
    }

    [Fact]
    public async Task InstallAsync_WithoutChecksumProceedsOnlyWhenExplicitlyAllowed()
    {
        var manager = CreateManager(
            Path.Combine(_workspaceRoot, "repo"),
            providers: [new FakeProvider("gradle", "gradle.bat", [CreatePackage("gradle", "9.5.1")])]);

        var result = await manager.InstallAsync(new InstallSdkRequest
        {
            CandidateName = "gradle",
            Version = "9.5.1",
            AllowUnverified = true
        }, progress: null, CancellationToken.None);

        Assert.Equal("9.5.1", result.InstalledVersion.Alias);
        Assert.NotNull(result.IntegrityWarning);
        Assert.Contains("unverified", result.IntegrityWarning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InstallAsync_UnsupportedChecksumTypeFailsClosed()
    {
        var package = CreatePackage("gradle", "9.5.1") with { Checksum = "abc", ChecksumType = "crc32" };
        var extractor = new RecordingArchiveExtractor(createExecutables: true);
        var manager = CreateManager(
            Path.Combine(_workspaceRoot, "repo"),
            providers: [new FakeProvider("gradle", "gradle.bat", [package])],
            extractor: extractor);

        await Assert.ThrowsAsync<IntegrityVerificationException>(() => manager.InstallAsync(new InstallSdkRequest
        {
            CandidateName = "gradle",
            Version = "9.5.1"
        }, progress: null, CancellationToken.None));
        Assert.False(extractor.WasCalled);
    }

    [Fact]
    public async Task InstallAsync_WeakChecksumAlgorithmProducesWarning()
    {
#pragma warning disable CA5351 // MD5 only mirrors what a legacy upstream catalog publishes.
        var package = CreatePackage("gradle", "9.5.1") with
        {
            Checksum = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes("archive"))),
            ChecksumType = "md5"
        };
#pragma warning restore CA5351
        var manager = CreateManager(
            Path.Combine(_workspaceRoot, "repo"),
            providers: [new FakeProvider("gradle", "gradle.bat", [package])]);

        var result = await manager.InstallAsync(new InstallSdkRequest
        {
            CandidateName = "gradle",
            Version = "9.5.1"
        }, progress: null, CancellationToken.None);

        Assert.NotNull(result.IntegrityWarning);
        Assert.Contains("md5", result.IntegrityWarning, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"..\..\evil.zip")]
    [InlineData("../../evil.zip")]
    [InlineData(@"C:\evil.zip")]
    public async Task InstallAsync_RejectsUnsafeFileNameFromCatalog(string fileName)
    {
        var package = CreatePackage("gradle", "9.5.1") with { FileName = fileName, Checksum = new string('0', 64), ChecksumType = "sha256" };
        var downloader = new WritingArchiveDownloader("archive");
        var manager = CreateManager(
            Path.Combine(_workspaceRoot, "repo"),
            providers: [new FakeProvider("gradle", "gradle.bat", [package])],
            downloader: downloader);

        await Assert.ThrowsAnyAsync<JwmvException>(() => manager.InstallAsync(new InstallSdkRequest
        {
            CandidateName = "gradle",
            Version = "9.5.1"
        }, progress: null, CancellationToken.None));
        Assert.Empty(downloader.Downloaded);
    }

    [Fact]
    public async Task InstallAsync_RejectsUnsafeAliasFromCatalog()
    {
        var package = CreatePackage("gradle", "9.5.1") with { Alias = @"..\evil", Checksum = new string('0', 64), ChecksumType = "sha256" };
        var downloader = new WritingArchiveDownloader("archive");
        var manager = CreateManager(
            Path.Combine(_workspaceRoot, "repo"),
            providers: [new FakeProvider("gradle", "gradle.bat", [package])],
            downloader: downloader);

        await Assert.ThrowsAnyAsync<JwmvException>(() => manager.InstallAsync(new InstallSdkRequest
        {
            CandidateName = "gradle",
            Version = @"..\evil"
        }, progress: null, CancellationToken.None));
        Assert.Empty(downloader.Downloaded);
    }

    [Fact]
    public async Task UninstallAsync_RefusesToDeleteDirectoriesOutsideCandidatesRoot()
    {
        var outside = Path.Combine(_workspaceRoot, "outside");
        Directory.CreateDirectory(outside);
        var installed = CreateInstalled("gradle", "9.5.1", "gradle.bat") with { InstallDirectory = outside, HomeDirectory = outside };
        var manager = CreateManager(
            Path.Combine(_workspaceRoot, "repo"),
            installed: [installed],
            providers: [new FakeProvider("gradle", "gradle.bat", [CreatePackage("gradle", "9.5.1")])]);

        await Assert.ThrowsAnyAsync<JwmvException>(() => manager.UninstallAsync("gradle", "9.5.1", CancellationToken.None));
        Assert.True(Directory.Exists(outside));
    }

    [Fact]
    public async Task InstallProjectMissingAsync_InstallsOnlyMissingVersions()
    {
        var repo = Path.Combine(_workspaceRoot, "repo");
        Directory.CreateDirectory(repo);
        await File.WriteAllTextAsync(Path.Combine(repo, JwmvConstants.ProjectFileName), """
gradle=9.5.1
maven=3.9.15
""");
        var mavenPackage = CreatePackage("maven", "3.9.15") with
        {
            Checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("archive"))),
            ChecksumType = "sha256"
        };
        var manager = CreateManager(
            repo,
            installed: [CreateInstalled("gradle", "9.5.1", "gradle.bat")],
            providers:
            [
                new FakeProvider("gradle", "gradle.bat", [CreatePackage("gradle", "9.5.1")]),
                new FakeProvider("maven", "mvn.cmd", [mavenPackage])
            ],
            downloader: new WritingArchiveDownloader("archive"),
            extractor: new RecordingArchiveExtractor(createExecutables: true));

        var results = await manager.InstallProjectMissingAsync(repo, forceCatalogRefresh: false, progress: null, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal("maven", result.InstalledVersion.CandidateName);
        Assert.Equal("3.9.15", result.InstalledVersion.Alias);
        Assert.Null(result.IntegrityWarning);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
        {
            Directory.Delete(_workspaceRoot, recursive: true);
        }
    }

    private SdkVersionManager CreateManager(
        string workingDirectory,
        IReadOnlyList<InstalledSdkVersion>? installed = null,
        IReadOnlyList<ISdkCatalogProvider>? providers = null,
        InMemoryConfigStore? configStore = null,
        RecordingSdkEnvironmentService? environment = null,
        IArchiveDownloader? downloader = null,
        RecordingArchiveExtractor? extractor = null)
    {
        var appContext = new FakeAppContext(_workspaceRoot, workingDirectory);
        var paths = new JwmvPaths(appContext);
        configStore ??= new InMemoryConfigStore();
        environment ??= new RecordingSdkEnvironmentService();
        return new SdkVersionManager(
            appContext,
            new FakeClock(),
            configStore,
            new InMemorySdkCatalogCacheStore(),
            providers ?? [new FakeProvider("java", "java.exe", [CreatePackage("java", "21.0.4-tem")])],
            new InMemorySdkInstallationStore(installed ?? []),
            downloader ?? new WritingArchiveDownloader("archive"),
            extractor ?? new RecordingArchiveExtractor(createExecutables: true),
            new ChecksumVerifier(),
            environment,
            new StaticHttpClientFactory(),
            paths);
    }

    private InstalledSdkVersion CreateInstalled(string candidate, string version, string executable)
    {
        var home = Path.Combine(_workspaceRoot, ".jwmv", "candidates", candidate, version);
        return new InstalledSdkVersion
        {
            CandidateName = candidate,
            Version = version,
            Alias = version,
            DisplayName = version,
            InstallDirectory = home,
            HomeDirectory = home,
            BinDirectory = Path.Combine(home, "bin"),
            HomeEnvironmentVariable = $"{candidate.ToUpperInvariant()}_HOME",
            PrimaryExecutableName = executable,
            ArchiveType = "zip",
            PackageFileName = $"{candidate}-{version}.zip",
            SourcePackageId = $"{candidate}-{version}",
            InstalledAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static SdkPackage CreatePackage(string candidate, string version) =>
        new()
        {
            Id = $"{candidate}-{version}",
            CandidateName = candidate,
            Version = version,
            Alias = version,
            DisplayName = version,
            ArchiveType = "zip",
            FileName = $"{candidate}-{version}.zip",
            DownloadUri = new Uri("https://example.invalid/archive.zip")
        };

    private sealed class FakeProvider(string candidateName, string executable, IReadOnlyList<SdkPackage> packages) : ISdkCatalogProvider
    {
        public SdkCandidate Candidate { get; } = new()
        {
            Name = candidateName,
            DisplayName = candidateName,
            Description = candidateName,
            WebsiteUri = new Uri("https://example.invalid/"),
            HomeEnvironmentVariable = $"{candidateName.ToUpperInvariant()}_HOME",
            PrimaryExecutableName = executable
        };

        public Task<IReadOnlyList<SdkPackage>> GetPackagesAsync(CancellationToken cancellationToken) => Task.FromResult(packages);
    }

    private sealed class FakeAppContext(string userProfileDirectory, string workingDirectory) : IAppContext
    {
        public string WorkingDirectory { get; } = workingDirectory;
        public string UserProfileDirectory { get; } = userProfileDirectory;
        public Architecture ProcessArchitecture => Architecture.X64;
        public string ExecutablePath => Path.Combine(WorkingDirectory, "jwmv.exe");
        public string? GetEnvironmentVariable(string variableName) => null;
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 5, 18, 12, 0, 0, TimeSpan.Zero);
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

    private sealed class InMemorySdkCatalogCacheStore : ISdkCatalogCacheStore
    {
        private SdkCatalogCache? _cache;
        public Task<SdkCatalogCache?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(_cache);
        public Task SaveAsync(SdkCatalogCache catalogCache, CancellationToken cancellationToken)
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

    private sealed class InMemorySdkInstallationStore(IReadOnlyList<InstalledSdkVersion> initial) : ISdkInstallationStore
    {
        private readonly Dictionary<string, InstalledSdkVersion> _items = initial.ToDictionary(item => $"{item.CandidateName}:{item.Alias}", StringComparer.OrdinalIgnoreCase);
        public Task<IReadOnlyList<InstalledSdkVersion>> GetInstalledVersionsAsync(string? candidateName, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InstalledSdkVersion>>(_items.Values.Where(item => string.IsNullOrWhiteSpace(candidateName) || string.Equals(item.CandidateName, candidateName, StringComparison.OrdinalIgnoreCase)).ToList());

        public Task<InstalledSdkVersion?> FindAsync(string candidateName, string alias, CancellationToken cancellationToken)
        {
            var item = _items.Values.FirstOrDefault(value => string.Equals(value.CandidateName, candidateName, StringComparison.OrdinalIgnoreCase) && value.Alias.StartsWith(alias, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(item);
        }

        public Task SaveAsync(InstalledSdkVersion installedVersion, CancellationToken cancellationToken)
        {
            _items[$"{installedVersion.CandidateName}:{installedVersion.Alias}"] = installedVersion;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string candidateName, string alias, CancellationToken cancellationToken)
        {
            _items.Remove($"{candidateName}:{alias}");
            return Task.CompletedTask;
        }
    }

    private sealed class WritingArchiveDownloader(string content) : IArchiveDownloader
    {
        public List<Uri> Downloaded { get; } = [];

        public Task DownloadAsync(Uri downloadUri, string destinationPath, IProgress<ArchiveDownloadProgress>? progress, CancellationToken cancellationToken)
        {
            Downloaded.Add(downloadUri);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.WriteAllText(destinationPath, content);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingArchiveExtractor(bool createExecutables = false) : IArchiveExtractor
    {
        public bool WasCalled { get; private set; }
        public Task ExtractZipAsync(string archivePath, string destinationDirectory, IProgress<ArchiveExtractionProgress>? progress, CancellationToken cancellationToken)
        {
            WasCalled = true;
            if (createExecutables)
            {
                var home = Path.Combine(destinationDirectory, "sdk");
                Directory.CreateDirectory(Path.Combine(home, "bin"));
                foreach (var executable in new[] { "java.exe", "gradle.bat", "mvn.cmd", "kotlinc.bat" })
                {
                    File.WriteAllText(Path.Combine(home, "bin", executable), string.Empty);
                }
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingSdkEnvironmentService : ISdkEnvironmentService
    {
        public InstalledSdkVersion? LastApplied { get; private set; }
        public Task ApplyDefaultAsync(InstalledSdkVersion installedVersion, CancellationToken cancellationToken)
        {
            LastApplied = installedVersion;
            return Task.CompletedTask;
        }

        public Task ClearDefaultAsync(string candidateName, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StaticHandler()) { BaseAddress = new Uri("https://example.invalid/") };
    }

    private sealed class StaticHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Empty)
            });
    }
}
