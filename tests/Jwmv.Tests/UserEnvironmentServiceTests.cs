using Jwmv.Core.Abstractions;
using Jwmv.Core.Exceptions;
using Jwmv.Core.Models;
using Jwmv.Infrastructure.Windows;

namespace Jwmv.Tests;

public sealed class UserEnvironmentServiceTests
{
    [Fact]
    public async Task ApplyDefaultAsync_PrependsBinToUserPathAndKeepsItExpandable()
    {
        var store = new InMemoryUserEnvironmentStore();
        store.Set("Path", @"%USERPROFILE%\bin;C:\Tools", isExpandable: true);
        var service = new SdkEnvironmentService(store);
        var installed = CreateInstalled("gradle", @"C:\jwmv\candidates\gradle\9.5.1");

        await service.ApplyDefaultAsync(installed, CancellationToken.None);

        var path = store.Get("Path");
        Assert.NotNull(path);
        Assert.Equal(@"C:\jwmv\candidates\gradle\9.5.1\bin;%USERPROFILE%\bin;C:\Tools", path!.Value);
        Assert.True(path.IsExpandable);
        Assert.Equal(@"C:\jwmv\candidates\gradle\9.5.1", store.Get("GRADLE_HOME")?.Value);
        Assert.Equal("9.5.1", store.Get("JWMV_DEFAULT_GRADLE_VERSION")?.Value);
        Assert.Equal(@"C:\jwmv\candidates\gradle\9.5.1\bin", store.Get("JWMV_DEFAULT_GRADLE_BIN")?.Value);
        Assert.Equal(@"C:\jwmv\candidates\gradle\9.5.1\bin", store.ProcessValues["JWMV_DEFAULT_GRADLE_BIN"]);
    }

    [Fact]
    public async Task ApplyDefaultAsync_ReplacesThePreviouslyManagedBinEntry()
    {
        var store = new InMemoryUserEnvironmentStore();
        store.Set("Path", @"C:\jwmv\candidates\gradle\8.0\bin;C:\Tools", isExpandable: true);
        store.Set("JWMV_DEFAULT_GRADLE_BIN", @"C:\jwmv\candidates\gradle\8.0\bin", isExpandable: false);
        var service = new SdkEnvironmentService(store);

        await service.ApplyDefaultAsync(CreateInstalled("gradle", @"C:\jwmv\candidates\gradle\9.5.1"), CancellationToken.None);

        Assert.Equal(@"C:\jwmv\candidates\gradle\9.5.1\bin;C:\Tools", store.Get("Path")?.Value);
    }

    [Fact]
    public async Task ApplyDefaultAsync_RejectsUnsafeHomeVariableNameFromManifest()
    {
        var store = new InMemoryUserEnvironmentStore();
        var service = new SdkEnvironmentService(store);
        var installed = CreateInstalled("gradle", @"C:\jwmv\candidates\gradle\9.5.1") with { HomeEnvironmentVariable = "GRADLE_HOME=evil" };

        await Assert.ThrowsAnyAsync<JwmvException>(() => service.ApplyDefaultAsync(installed, CancellationToken.None));
        Assert.Null(store.Get("Path"));
    }

    [Fact]
    public async Task ClearDefaultAsync_LeavesHomeVariableThatJwmvDidNotSet()
    {
        var store = new InMemoryUserEnvironmentStore();
        store.Set("JAVA_HOME", @"C:\Program Files\Java\jdk-17", isExpandable: false);
        store.Set("Path", @"C:\Tools", isExpandable: true);
        var service = new SdkEnvironmentService(store);

        await service.ClearDefaultAsync("java", CancellationToken.None);

        Assert.Equal(@"C:\Program Files\Java\jdk-17", store.Get("JAVA_HOME")?.Value);
        Assert.Equal(@"C:\Tools", store.Get("Path")?.Value);
    }

    [Fact]
    public async Task ClearDefaultAsync_RemovesHomeVariableAndPathEntryThatJwmvSet()
    {
        var store = new InMemoryUserEnvironmentStore();
        var service = new SdkEnvironmentService(store);
        await service.ApplyDefaultAsync(CreateInstalled("java", @"C:\jwmv\candidates\java\21-tem"), CancellationToken.None);
        store.Set("Path", @"C:\jwmv\candidates\java\21-tem\bin;%USERPROFILE%\bin", isExpandable: true);

        await service.ClearDefaultAsync("java", CancellationToken.None);

        Assert.Null(store.Get("JAVA_HOME"));
        Assert.Null(store.Get("JWMV_DEFAULT_JAVA_HOME"));
        Assert.Null(store.Get("JWMV_DEFAULT_JAVA_BIN"));
        Assert.Equal(@"%USERPROFILE%\bin", store.Get("Path")?.Value);
        Assert.True(store.Get("Path")!.IsExpandable);
    }

    [Fact]
    public async Task LegacyService_ClearDefaultAsync_LeavesForeignJavaHome()
    {
        var store = new InMemoryUserEnvironmentStore();
        store.Set("JAVA_HOME", @"C:\Program Files\Java\jdk-17", isExpandable: false);
        var service = new WindowsEnvironmentService(store);

        await service.ClearDefaultAsync(CancellationToken.None);

        Assert.Equal(@"C:\Program Files\Java\jdk-17", store.Get("JAVA_HOME")?.Value);
    }

    [Fact]
    public async Task LegacyService_ApplyDefaultAsync_KeepsPathExpandable()
    {
        var store = new InMemoryUserEnvironmentStore();
        store.Set("Path", @"%USERPROFILE%\bin", isExpandable: true);
        var service = new WindowsEnvironmentService(store);

        await service.ApplyDefaultAsync(new InstalledJavaVersion
        {
            Alias = "21.0.4-tem",
            Distribution = "temurin",
            DistributionAlias = "tem",
            JavaVersion = "21.0.4",
            DistributionVersion = "21.0.4+7",
            InstallDirectory = @"C:\jwmv\candidates\java\21.0.4-tem",
            JavaHome = @"C:\jwmv\candidates\java\21.0.4-tem",
            ArchiveType = "zip",
            PackageFileName = "temurin.zip",
            SourcePackageId = "pkg"
        }, CancellationToken.None);

        var path = store.Get("Path");
        Assert.Equal(@"C:\jwmv\candidates\java\21.0.4-tem\bin;%USERPROFILE%\bin", path?.Value);
        Assert.True(path!.IsExpandable);
        Assert.Equal(@"C:\jwmv\candidates\java\21.0.4-tem", store.Get("JAVA_HOME")?.Value);
    }

    private static InstalledSdkVersion CreateInstalled(string candidate, string home) =>
        new()
        {
            CandidateName = candidate,
            Version = Path.GetFileName(home),
            Alias = Path.GetFileName(home),
            DisplayName = Path.GetFileName(home),
            InstallDirectory = home,
            HomeDirectory = home,
            BinDirectory = Path.Combine(home, "bin"),
            HomeEnvironmentVariable = $"{candidate.ToUpperInvariant()}_HOME",
            PrimaryExecutableName = "x.exe",
            ArchiveType = "zip",
            PackageFileName = "x.zip",
            SourcePackageId = "x"
        };

    private sealed class InMemoryUserEnvironmentStore : IUserEnvironmentStore
    {
        private readonly Dictionary<string, UserEnvironmentValue> _values = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string?> ProcessValues { get; } = new(StringComparer.OrdinalIgnoreCase);

        public UserEnvironmentValue? Get(string name) => _values.TryGetValue(name, out var value) ? value : null;

        public void Set(string name, string value, bool isExpandable) => _values[name] = new UserEnvironmentValue(value, isExpandable);

        public void Remove(string name) => _values.Remove(name);

        public void SetProcessValue(string name, string? value) => ProcessValues[name] = value;

        public void NotifyChanged()
        {
        }
    }
}
