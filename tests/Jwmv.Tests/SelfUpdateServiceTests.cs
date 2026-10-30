using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Jwmv.Core.Abstractions;
using Jwmv.Core.Exceptions;
using Jwmv.Core.Models;
using Jwmv.Infrastructure;
using Jwmv.Infrastructure.Services;

namespace Jwmv.Tests;

public sealed class SelfUpdateServiceTests : IDisposable
{
    private const string Repository = "example-org/example-repo";
    private const string ReleaseBase = "https://github.com/example-org/example-repo/releases/download/v9.9.9/";

    private readonly string _workspaceRoot = Path.Combine(Path.GetTempPath(), "jwmv-selfupdate-tests", Guid.NewGuid().ToString("n"));
    private readonly byte[] _zipBytes = CreateZip("jwmv.exe", "fake jwmv binary");

    [Fact]
    public async Task CheckForUpdateAsync_RejectsMalformedRepositoryBeforeAnyRequest()
    {
        var http = new RoutingHttpClientFactory();
        var service = CreateService(http: http);

        await Assert.ThrowsAnyAsync<JwmvException>(() => service.CheckForUpdateAsync(new SelfUpdateRequest { Repository = "evil;rm -rf /" }, CancellationToken.None));
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task CheckForUpdateAsync_RequiresChecksumAndSignatureAssets()
    {
        var http = new RoutingHttpClientFactory();
        http.AddJson($"https://api.github.com/repos/{Repository}/releases/latest", ReleaseJson(includeSignatureAssets: false));
        var service = CreateService(http: http);

        await Assert.ThrowsAsync<IntegrityVerificationException>(() => service.CheckForUpdateAsync(new SelfUpdateRequest { Repository = Repository, Force = true }, CancellationToken.None));
    }

    [Fact]
    public async Task CheckForUpdateAsync_RejectsAssetsNotServedFromGitHubOverHttps()
    {
        var http = new RoutingHttpClientFactory();
        http.AddJson($"https://api.github.com/repos/{Repository}/releases/latest", ReleaseJson(baseUrl: "http://evil.example/download/"));
        var service = CreateService(http: http);

        await Assert.ThrowsAnyAsync<JwmvException>(() => service.CheckForUpdateAsync(new SelfUpdateRequest { Repository = Repository, Force = true }, CancellationToken.None));
    }

    [Fact]
    public async Task CheckForUpdateAsync_ExposesCompanionAssets()
    {
        var http = new RoutingHttpClientFactory();
        http.AddJson($"https://api.github.com/repos/{Repository}/releases/latest", ReleaseJson());
        var service = CreateService(http: http);

        var result = await service.CheckForUpdateAsync(new SelfUpdateRequest { Repository = Repository, Force = true }, CancellationToken.None);

        Assert.Equal("jwmv-win-x64.zip", result.AssetName);
        Assert.Equal(new Uri(ReleaseBase + "jwmv-win-x64.zip.sha256"), result.ChecksumAssetUri);
        Assert.Equal(new Uri(ReleaseBase + "jwmv-win-x64.zip.sig"), result.SignatureAssetUri);
        Assert.Equal(new Uri(ReleaseBase + "jwmv-win-x64.zip.pem"), result.CertificateAssetUri);
    }

    [Theory]
    [InlineData("latest-build")]
    [InlineData("9.9.9-beta.1")]
    [InlineData("v9.9.9’; Start-Process calc.exe")]
    public async Task CheckForUpdateAsync_RejectsReleaseTagsThatAreNotPlainSemanticVersions(string tag)
    {
        var http = new RoutingHttpClientFactory();
        http.AddJson($"https://api.github.com/repos/{Repository}/releases/latest", ReleaseJson(tag: tag));
        var service = CreateService(http: http);

        var exception = await Assert.ThrowsAnyAsync<JwmvException>(() => service.CheckForUpdateAsync(new SelfUpdateRequest { Repository = Repository, Force = true }, CancellationToken.None));
        Assert.Contains(tag, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("v9.9.9")]
    [InlineData("9.9.9")]
    public async Task CheckForUpdateAsync_AcceptsPlainSemanticVersionTags(string tag)
    {
        var http = new RoutingHttpClientFactory();
        http.AddJson($"https://api.github.com/repos/{Repository}/releases/latest", ReleaseJson(tag: tag));
        var service = CreateService(http: http);

        var result = await service.CheckForUpdateAsync(new SelfUpdateRequest { Repository = Repository, Force = true }, CancellationToken.None);

        Assert.Equal("9.9.9", result.TargetVersion);
        Assert.Equal(tag, result.ReleaseTag);
    }

    [Fact]
    public async Task ApplyUpdateAsync_FailsWhenChecksumDoesNotMatch()
    {
        var http = CreateReleaseHttp(sha256: new string('0', 64));
        var launcher = new RecordingUpdaterLauncher();
        var verifier = new FakeSignatureVerifier();
        var service = CreateService(http: http, launcher: launcher, verifier: verifier);
        var check = await service.CheckForUpdateAsync(new SelfUpdateRequest { Repository = Repository, Force = true }, CancellationToken.None);

        await Assert.ThrowsAsync<IntegrityVerificationException>(() => service.ApplyUpdateAsync(check, restartAfterUpdate: false, progress: null, CancellationToken.None));
        Assert.Null(launcher.LaunchedScriptPath);
        Assert.Equal(0, verifier.Calls);
    }

    [Fact]
    public async Task ApplyUpdateAsync_FailsWhenSignatureVerificationFails()
    {
        var http = CreateReleaseHttp();
        var launcher = new RecordingUpdaterLauncher();
        var service = CreateService(http: http, launcher: launcher, verifier: new FakeSignatureVerifier(shouldFail: true));
        var check = await service.CheckForUpdateAsync(new SelfUpdateRequest { Repository = Repository, Force = true }, CancellationToken.None);

        await Assert.ThrowsAsync<IntegrityVerificationException>(() => service.ApplyUpdateAsync(check, restartAfterUpdate: false, progress: null, CancellationToken.None));
        Assert.Null(launcher.LaunchedScriptPath);
    }

    [Fact]
    public async Task ApplyUpdateAsync_StagesUpdaterAfterVerificationSucceeds()
    {
        var http = CreateReleaseHttp();
        var launcher = new RecordingUpdaterLauncher();
        var verifier = new FakeSignatureVerifier();
        var service = CreateService(http: http, launcher: launcher, verifier: verifier);
        var check = await service.CheckForUpdateAsync(new SelfUpdateRequest { Repository = Repository, Force = true }, CancellationToken.None);

        var result = await service.ApplyUpdateAsync(check, restartAfterUpdate: false, progress: null, CancellationToken.None);

        Assert.Equal(1, verifier.Calls);
        Assert.Equal(Repository, verifier.LastRepository);
        Assert.Equal("signature-text", verifier.LastSignature);
        Assert.Equal("certificate-text", verifier.LastCertificate);
        Assert.Equal(result.UpdaterScriptPath, launcher.LaunchedScriptPath);
        Assert.True(File.Exists(result.UpdaterScriptPath));
        Assert.Equal("https://github.com/example-org/example-repo/.github/workflows/release.yml@refs/tags/v9.9.9", result.SignerIdentity);
        var script = await File.ReadAllTextAsync(result.UpdaterScriptPath);
        Assert.Contains(result.UpdaterLogPath, script, StringComparison.Ordinal);
        Assert.Contains("--version", script, StringComparison.Ordinal);
        Assert.Contains("9.9.9", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"C:\Users\me\AppData\Local\Microsoft\WinGet\Packages\stescobedo92.jwmv_Microsoft.Winget.Source_8wekyb3d8bbwe\jwmv.exe")]
    [InlineData(@"C:\Users\me\AppData\Roaming\npm\node_modules\@stescobedo9205\jwmv\bin\jwmv.exe")]
    [InlineData(@"C:\Users\me\.dotnet\tools\jwmv.exe")]
    public async Task ApplyUpdateAsync_RefusesExecutablesOwnedByPackageManagers(string executablePath)
    {
        var http = CreateReleaseHttp();
        var launcher = new RecordingUpdaterLauncher();
        var service = CreateService(http: http, launcher: launcher, executablePath: executablePath);
        var check = await service.CheckForUpdateAsync(new SelfUpdateRequest { Repository = Repository, Force = true }, CancellationToken.None);

        await Assert.ThrowsAnyAsync<JwmvException>(() => service.ApplyUpdateAsync(check, restartAfterUpdate: false, progress: null, CancellationToken.None));
        Assert.Null(launcher.LaunchedScriptPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspaceRoot))
        {
            Directory.Delete(_workspaceRoot, recursive: true);
        }
    }

    private RoutingHttpClientFactory CreateReleaseHttp(string? sha256 = null)
    {
        var http = new RoutingHttpClientFactory();
        http.AddJson($"https://api.github.com/repos/{Repository}/releases/latest", ReleaseJson());
        http.AddBytes(ReleaseBase + "jwmv-win-x64.zip", _zipBytes);
        http.AddText(ReleaseBase + "jwmv-win-x64.zip.sha256", $"{sha256 ?? Convert.ToHexString(SHA256.HashData(_zipBytes)).ToLowerInvariant()} *jwmv-win-x64.zip\n");
        http.AddText(ReleaseBase + "jwmv-win-x64.zip.sig", "signature-text");
        http.AddText(ReleaseBase + "jwmv-win-x64.zip.pem", "certificate-text");
        return http;
    }

    private SelfUpdateService CreateService(
        RoutingHttpClientFactory? http = null,
        RecordingUpdaterLauncher? launcher = null,
        FakeSignatureVerifier? verifier = null,
        string? executablePath = null)
    {
        http ??= new RoutingHttpClientFactory();
        var appContext = new FakeAppContext(_workspaceRoot, executablePath ?? Path.Combine(_workspaceRoot, "bin", "jwmv.exe"));
        var paths = new JwmvPaths(appContext);
        return new SelfUpdateService(
            appContext,
            new InMemoryConfigStore(),
            http,
            new RoutingArchiveDownloader(http),
            new ExtractingArchiveExtractor(),
            verifier ?? new FakeSignatureVerifier(),
            launcher ?? new RecordingUpdaterLauncher(),
            paths);
    }

    private static string ReleaseJson(bool includeSignatureAssets = true, string baseUrl = ReleaseBase, string tag = "v9.9.9")
    {
        var assets = new List<string>
        {
            Asset("jwmv-win-x64.zip", baseUrl),
            Asset("jwmv-win-arm64.zip", baseUrl),
            Asset("jwmv-win-x64.zip.sha256", baseUrl),
            Asset("jwmv-win-arm64.zip.sha256", baseUrl)
        };
        if (includeSignatureAssets)
        {
            assets.Add(Asset("jwmv-win-x64.zip.sig", baseUrl));
            assets.Add(Asset("jwmv-win-x64.zip.pem", baseUrl));
            assets.Add(Asset("jwmv-win-arm64.zip.sig", baseUrl));
            assets.Add(Asset("jwmv-win-arm64.zip.pem", baseUrl));
        }

        return $$"""
{"tag_name":"{{tag}}","name":"jwmv 9.9.9","html_url":"https://github.com/example-org/example-repo/releases/tag/v9.9.9","assets":[{{string.Join(",", assets)}}]}
""";
    }

    private static string Asset(string name, string baseUrl) =>
        $$"""{"name":"{{name}}","browser_download_url":"{{baseUrl}}{{name}}"}""";

    private static byte[] CreateZip(string entryName, string content)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }

        return stream.ToArray();
    }

    private sealed class FakeAppContext(string userProfileDirectory, string executablePath) : IAppContext
    {
        public string WorkingDirectory { get; } = userProfileDirectory;
        public string UserProfileDirectory { get; } = userProfileDirectory;
        public Architecture ProcessArchitecture => Architecture.X64;
        public string ExecutablePath { get; } = executablePath;
        public string? GetEnvironmentVariable(string variableName) => null;
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

    private sealed class RoutingHttpClientFactory : IHttpClientFactory
    {
        private readonly Dictionary<string, Func<HttpContent>> _routes = new(StringComparer.OrdinalIgnoreCase);

        public List<Uri> Requests { get; } = [];

        public void AddJson(string url, string json) => _routes[url] = () => new StringContent(json, Encoding.UTF8, "application/json");
        public void AddText(string url, string text) => _routes[url] = () => new StringContent(text, Encoding.UTF8, "text/plain");
        public void AddBytes(string url, byte[] bytes) => _routes[url] = () => new ByteArrayContent(bytes);

        public bool TryGetBytes(Uri uri, out byte[] bytes)
        {
            if (_routes.TryGetValue(uri.AbsoluteUri, out var factory))
            {
                bytes = factory().ReadAsByteArrayAsync().GetAwaiter().GetResult();
                return true;
            }

            bytes = [];
            return false;
        }

        public HttpClient CreateClient(string name) => new(new RoutingHandler(this)) { BaseAddress = new Uri("https://api.github.com/") };

        private sealed class RoutingHandler(RoutingHttpClientFactory owner) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                owner.Requests.Add(request.RequestUri!);
                return Task.FromResult(owner._routes.TryGetValue(request.RequestUri!.AbsoluteUri, out var factory)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = factory() }
                    : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("not found") });
            }
        }
    }

    private sealed class RoutingArchiveDownloader(RoutingHttpClientFactory http) : IArchiveDownloader
    {
        public Task DownloadAsync(Uri downloadUri, string destinationPath, IProgress<ArchiveDownloadProgress>? progress, CancellationToken cancellationToken)
        {
            if (!http.TryGetBytes(downloadUri, out var bytes))
            {
                throw new HttpRequestException($"No route for {downloadUri}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.WriteAllBytes(destinationPath, bytes);
            return Task.CompletedTask;
        }
    }

    private sealed class ExtractingArchiveExtractor : IArchiveExtractor
    {
        public Task ExtractZipAsync(string archivePath, string destinationDirectory, IProgress<ArchiveExtractionProgress>? progress, CancellationToken cancellationToken)
        {
            ZipFile.ExtractToDirectory(archivePath, destinationDirectory, overwriteFiles: true);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSignatureVerifier(bool shouldFail = false) : IReleaseSignatureVerifier
    {
        public int Calls { get; private set; }
        public string? LastRepository { get; private set; }
        public string? LastSignature { get; private set; }
        public string? LastCertificate { get; private set; }

        public ReleaseSignatureVerificationResult Verify(string filePath, string signature, string certificate, string expectedRepository)
        {
            Calls++;
            LastRepository = expectedRepository;
            LastSignature = signature;
            LastCertificate = certificate;
            if (shouldFail)
            {
                throw new IntegrityVerificationException("signature rejected by test");
            }

            return new ReleaseSignatureVerificationResult
            {
                SignerIdentity = $"https://github.com/{expectedRepository}/.github/workflows/release.yml@refs/tags/v9.9.9",
                Issuer = "https://token.actions.githubusercontent.com"
            };
        }
    }

    private sealed class RecordingUpdaterLauncher : IUpdaterLauncher
    {
        public string? LaunchedScriptPath { get; private set; }

        public void Launch(string scriptPath) => LaunchedScriptPath = scriptPath;
    }
}
