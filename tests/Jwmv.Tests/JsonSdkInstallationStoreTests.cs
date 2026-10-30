using System.Runtime.InteropServices;
using System.Text.Json;
using Jwmv.Core.Abstractions;
using Jwmv.Core.Exceptions;
using Jwmv.Core.Models;
using Jwmv.Infrastructure;
using Jwmv.Infrastructure.Storage;

namespace Jwmv.Tests;

public sealed class JsonSdkInstallationStoreTests : IDisposable
{
    private readonly string _workspaceRoot = Path.Combine(Path.GetTempPath(), "jwmv-store-tests", Guid.NewGuid().ToString("n"));

    [Fact]
    public async Task FindAsync_ReturnsManifestSavedForAlias()
    {
        var (store, _) = CreateStore();
        await store.SaveAsync(CreateInstalled("java", "21.0.4-tem"), CancellationToken.None);

        var found = await store.FindAsync("java", "21.0.4-tem", CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal("21.0.4-tem", found!.Alias);
    }

    [Theory]
    [InlineData("../../evil")]
    [InlineData(@"..\..\evil")]
    [InlineData("../../../../evil")]
    public async Task FindAsync_DoesNotReadManifestsOutsideCandidateDirectory(string alias)
    {
        var (store, paths) = CreateStore();
        paths.EnsureCandidateCreated("java");
        var outside = Path.GetFullPath(Path.Combine(paths.GetCandidateManifestsDirectory("java"), alias + ".json"));
        Directory.CreateDirectory(Path.GetDirectoryName(outside)!);
        await File.WriteAllTextAsync(outside, JsonSerializer.Serialize(CreateInstalled("java", "evil") with { BinDirectory = @"C:\attacker\bin" }));

        var found = await store.FindAsync("java", alias, CancellationToken.None);

        Assert.Null(found);
    }

    [Fact]
    public async Task FindAsync_DoesNotReadAbsoluteOrUncPaths()
    {
        var (store, _) = CreateStore();
        var absolute = Path.Combine(_workspaceRoot, "absolute");
        Directory.CreateDirectory(_workspaceRoot);
        await File.WriteAllTextAsync(absolute + ".json", JsonSerializer.Serialize(CreateInstalled("java", "evil")));

        Assert.Null(await store.FindAsync("java", absolute, CancellationToken.None));
        Assert.Null(await store.FindAsync("java", @"\\attacker\share\evil", CancellationToken.None));
    }

    [Fact]
    public async Task FindAsync_WithUnsafeCandidateNameReturnsNull()
    {
        var (store, _) = CreateStore();
        await store.SaveAsync(CreateInstalled("java", "21.0.4-tem"), CancellationToken.None);

        Assert.Null(await store.FindAsync("../java", "21.0.4-tem", CancellationToken.None));
        Assert.Empty(await store.GetInstalledVersionsAsync("../java", CancellationToken.None));
    }

    [Fact]
    public async Task SaveAsync_RejectsUnsafeAlias()
    {
        var (store, _) = CreateStore();

        await Assert.ThrowsAnyAsync<JwmvException>(() => store.SaveAsync(CreateInstalled("java", "../../evil"), CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_RejectsUnsafeAlias()
    {
        var (store, _) = CreateStore();

        await Assert.ThrowsAnyAsync<JwmvException>(() => store.DeleteAsync("java", "../../evil", CancellationToken.None));
    }

    [Fact]
    public async Task LegacyStore_FindByAliasAsync_DoesNotReadManifestsOutsideDirectory()
    {
        var appContext = new FakeAppContext(_workspaceRoot);
        var paths = new JwmvPaths(appContext);
        var store = new JsonJavaInstallationStore(paths);
        paths.EnsureCreated();
        var outside = Path.GetFullPath(Path.Combine(paths.ManifestsDirectory, "..", "..", "evil.json"));
        await File.WriteAllTextAsync(outside, JsonSerializer.Serialize(new InstalledJavaVersion
        {
            Alias = "evil",
            Distribution = "temurin",
            DistributionAlias = "tem",
            JavaVersion = "21",
            DistributionVersion = "21",
            InstallDirectory = @"C:\attacker",
            JavaHome = @"C:\attacker",
            ArchiveType = "zip",
            PackageFileName = "evil.zip",
            SourcePackageId = "evil"
        }));

        Assert.Null(await store.FindByAliasAsync("../../evil", CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
        {
            Directory.Delete(_workspaceRoot, recursive: true);
        }
    }

    private (JsonSdkInstallationStore Store, JwmvPaths Paths) CreateStore()
    {
        var paths = new JwmvPaths(new FakeAppContext(_workspaceRoot));
        return (new JsonSdkInstallationStore(paths), paths);
    }

    private InstalledSdkVersion CreateInstalled(string candidate, string alias)
    {
        var home = Path.Combine(_workspaceRoot, ".jwmv", "candidates", candidate, alias);
        return new InstalledSdkVersion
        {
            CandidateName = candidate,
            Version = alias,
            Alias = alias,
            DisplayName = alias,
            InstallDirectory = home,
            HomeDirectory = home,
            BinDirectory = Path.Combine(home, "bin"),
            HomeEnvironmentVariable = "JAVA_HOME",
            PrimaryExecutableName = "java.exe",
            ArchiveType = "zip",
            PackageFileName = $"{candidate}-{alias}.zip",
            SourcePackageId = $"{candidate}-{alias}",
            InstalledAtUtc = DateTimeOffset.UtcNow
        };
    }

    private sealed class FakeAppContext(string userProfileDirectory) : IAppContext
    {
        public string WorkingDirectory { get; } = userProfileDirectory;
        public string UserProfileDirectory { get; } = userProfileDirectory;
        public Architecture ProcessArchitecture => Architecture.X64;
        public string ExecutablePath => Path.Combine(WorkingDirectory, "jwmv.exe");
        public string? GetEnvironmentVariable(string variableName) => null;
    }
}
