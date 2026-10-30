using System.Runtime.InteropServices;
using Jwmv.Core.Abstractions;
using Jwmv.Core.Models;
using Jwmv.Infrastructure.Shell;

namespace Jwmv.Tests;

public sealed class ShellProfileIntegrationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jwmv-profile-tests", Guid.NewGuid().ToString("n"));

    [Fact]
    public async Task EnsureIntegratedAsync_ReplacesTheManagedBlockAndLeavesNoTemporaryFiles()
    {
        var profile = Path.Combine(_root, "profile.ps1");
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(profile, "Set-Alias ll Get-ChildItem\n\n# >>> jwmv initialize >>>\nold block\n# <<< jwmv initialize <<<\n");
        var service = new ShellProfileIntegrationService(new FakeAppContext(_root), new FakeSdkVersionManager("$script:jwmvExe = 'new'"));

        var target = await service.EnsureIntegratedAsync(ShellKind.PowerShell, profile, CancellationToken.None);

        var content = await File.ReadAllTextAsync(target);
        Assert.Contains("Set-Alias ll Get-ChildItem", content, StringComparison.Ordinal);
        Assert.Contains("$script:jwmvExe = 'new'", content, StringComparison.Ordinal);
        Assert.DoesNotContain("old block", content, StringComparison.Ordinal);
        Assert.Equal(["profile.ps1"], Directory.GetFiles(_root).Select(Path.GetFileName));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FakeAppContext(string root) : IAppContext
    {
        public string WorkingDirectory => root;
        public string UserProfileDirectory => root;
        public Architecture ProcessArchitecture => Architecture.X64;
        public string ExecutablePath => Path.Combine(root, "jwmv.exe");
        public string? GetEnvironmentVariable(string variableName) => null;
    }

    private sealed class FakeSdkVersionManager(string initScript) : ISdkVersionManager
    {
        public Task<string> BuildShellInitScriptAsync(ShellKind shellKind, CancellationToken cancellationToken) => Task.FromResult(initScript);

        public Task<IReadOnlyList<SdkCandidate>> ListCandidatesAsync(string? filter, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<SdkPackage>> ListAvailableAsync(SdkCatalogQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<InstalledSdkVersion>> ListInstalledAsync(string? candidateName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InstallSdkResult> InstallAsync(InstallSdkRequest request, IProgress<InstallProgressUpdate>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UninstallAsync(string candidateName, string version, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<InstalledSdkVersion> SetDefaultAsync(string candidateName, string version, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ActiveSdkSelection> ResolveCurrentAsync(string candidateName, string? workingDirectory, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ActiveSdkSelection>> ResolveAllCurrentAsync(string? workingDirectory, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> GetHomeAsync(string candidateName, string? version, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> BuildUseShellScriptAsync(string candidateName, string version, ShellKind shellKind, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> BuildEnvShellScriptAsync(string? workingDirectory, ShellKind shellKind, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProjectSdkConfiguration?> FindProjectConfigurationAsync(string? workingDirectory, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<InstallSdkResult>> InstallProjectMissingAsync(string? workingDirectory, bool forceCatalogRefresh, IProgress<InstallProgressUpdate>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RefreshCatalogAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AppConfig> GetConfigAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
