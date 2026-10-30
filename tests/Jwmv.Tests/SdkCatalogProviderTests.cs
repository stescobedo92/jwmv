using System.Net;
using Jwmv.Core.Abstractions;
using Jwmv.Core.Models;
using Jwmv.Infrastructure;
using Jwmv.Infrastructure.Catalog;

namespace Jwmv.Tests;

public sealed class SdkCatalogProviderTests
{
    [Fact]
    public async Task GradleProvider_MapsStableVersionsAndChecksums()
    {
        var provider = new GradleCatalogProvider(new StaticHttpClientFactory(new Dictionary<string, HttpClient>
        {
            [ServiceCollectionExtensions.GradleClientName] = StaticHttpClientFactory.Create("https://services.gradle.org/", """
[
  {"version":"9.5.1","snapshot":false,"nightly":false,"releaseNightly":false,"broken":false,"downloadUrl":"https://services.gradle.org/distributions/gradle-9.5.1-bin.zip","checksumUrl":"https://services.gradle.org/distributions/gradle-9.5.1-bin.zip.sha256","checksum":"abc"},
  {"version":"9.6.0-milestone-2","snapshot":false,"nightly":false,"releaseNightly":false,"broken":false,"downloadUrl":"https://services.gradle.org/distributions/gradle-9.6.0-milestone-2-bin.zip","checksum":"bad"}
]
""")
        }));

        var packages = await provider.GetPackagesAsync(CancellationToken.None);

        var package = Assert.Single(packages);
        Assert.Equal("gradle", package.CandidateName);
        Assert.Equal("9.5.1", package.Alias);
        Assert.Equal("sha256", package.ChecksumType);
        Assert.Equal("abc", package.Checksum);
    }

    [Fact]
    public async Task MavenProvider_ExcludesPrereleases()
    {
        var provider = new MavenCatalogProvider(new StaticHttpClientFactory(new Dictionary<string, HttpClient>
        {
            [ServiceCollectionExtensions.MavenCentralClientName] = StaticHttpClientFactory.Create("https://repo.maven.apache.org/", """
<metadata>
  <versioning>
    <versions>
      <version>3.9.15</version>
      <version>4.0.0-rc-5</version>
    </versions>
  </versioning>
</metadata>
""")
        }));

        var packages = await provider.GetPackagesAsync(CancellationToken.None);

        var package = Assert.Single(packages);
        Assert.Equal("maven", package.CandidateName);
        Assert.Equal("3.9.15", package.Alias);
        Assert.Equal("sha512", package.ChecksumType);
    }

    [Fact]
    public async Task MavenProvider_PrefersChecksumPublishedOnApacheArchiveOverTheDownloadOrigin()
    {
        var routes = new RoutingHandler();
        routes.Add(HttpMethod.Head, "https://archive.apache.org/dist/maven/maven-3/3.9.15/binaries/apache-maven-3.9.15-bin.zip.sha512", HttpStatusCode.OK);
        var provider = new MavenCatalogProvider(new StaticHttpClientFactory(new Dictionary<string, HttpClient>
        {
            [ServiceCollectionExtensions.ApacheArchiveClientName] = new HttpClient(routes) { BaseAddress = new Uri("https://archive.apache.org/") }
        }));

        var package = await provider.GetPackageDetailsAsync(CreateMavenPackage("3.9.15"), CancellationToken.None);

        Assert.Equal(new Uri("https://archive.apache.org/dist/maven/maven-3/3.9.15/binaries/apache-maven-3.9.15-bin.zip.sha512"), package.ChecksumUri);
        Assert.Equal("sha512", package.ChecksumType);
    }

    [Fact]
    public async Task MavenProvider_FallsBackToWeakerArchiveChecksumBeforeSameOriginChecksum()
    {
        var routes = new RoutingHandler();
        routes.Add(HttpMethod.Head, "https://archive.apache.org/dist/maven/maven-3/3.0.4/binaries/apache-maven-3.0.4-bin.zip.sha1", HttpStatusCode.OK);
        var provider = new MavenCatalogProvider(new StaticHttpClientFactory(new Dictionary<string, HttpClient>
        {
            [ServiceCollectionExtensions.ApacheArchiveClientName] = new HttpClient(routes) { BaseAddress = new Uri("https://archive.apache.org/") }
        }));

        var package = await provider.GetPackageDetailsAsync(CreateMavenPackage("3.0.4"), CancellationToken.None);

        Assert.Equal(new Uri("https://archive.apache.org/dist/maven/maven-3/3.0.4/binaries/apache-maven-3.0.4-bin.zip.sha1"), package.ChecksumUri);
        Assert.Equal("sha1", package.ChecksumType);
    }

    [Fact]
    public async Task MavenProvider_KeepsSameOriginChecksumWhenArchiveHasNone()
    {
        var provider = new MavenCatalogProvider(new StaticHttpClientFactory(new Dictionary<string, HttpClient>
        {
            [ServiceCollectionExtensions.ApacheArchiveClientName] = new HttpClient(new RoutingHandler()) { BaseAddress = new Uri("https://archive.apache.org/") }
        }));
        var original = CreateMavenPackage("3.9.15");

        var package = await provider.GetPackageDetailsAsync(original, CancellationToken.None);

        Assert.Equal(original.ChecksumUri, package.ChecksumUri);
        Assert.Equal("sha512", package.ChecksumType);
    }

    [Fact]
    public async Task KotlinProvider_SelectsCompilerZipAsset()
    {
        var provider = new KotlinCatalogProvider(new StaticHttpClientFactory(new Dictionary<string, HttpClient>
        {
            [ServiceCollectionExtensions.GitHubClientName] = StaticHttpClientFactory.Create("https://api.github.com/", """
[
  {
    "tag_name":"v2.3.21",
    "draft":false,
    "prerelease":false,
    "assets":[
      {"name":"kotlin-compiler-2.3.21.zip","browser_download_url":"https://github.com/JetBrains/kotlin/releases/download/v2.3.21/kotlin-compiler-2.3.21.zip","size":123},
      {"name":"kotlin-compiler-2.3.21.zip.sha256","browser_download_url":"https://github.com/JetBrains/kotlin/releases/download/v2.3.21/kotlin-compiler-2.3.21.zip.sha256","size":64}
    ]
  }
]
""")
        }));

        var packages = await provider.GetPackagesAsync(CancellationToken.None);

        var package = Assert.Single(packages);
        Assert.Equal("kotlin", package.CandidateName);
        Assert.Equal("2.3.21", package.Alias);
        Assert.Equal("kotlin-compiler-2.3.21.zip", package.FileName);
        Assert.Equal("sha256", package.ChecksumType);
    }

    private static SdkPackage CreateMavenPackage(string version) =>
        new()
        {
            Id = $"maven-{version}",
            CandidateName = "maven",
            Version = version,
            Alias = version,
            DisplayName = version,
            ArchiveType = "zip",
            FileName = $"apache-maven-{version}-bin.zip",
            DownloadUri = new Uri($"https://repo.maven.apache.org/maven2/org/apache/maven/apache-maven/{version}/apache-maven-{version}-bin.zip"),
            ChecksumUri = new Uri($"https://repo.maven.apache.org/maven2/org/apache/maven/apache-maven/{version}/apache-maven-{version}-bin.zip.sha512"),
            ChecksumType = "sha512"
        };

    private sealed class StaticHttpClientFactory(IReadOnlyDictionary<string, HttpClient> clients) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => clients[name];

        public static HttpClient Create(string baseAddress, string content) =>
            new(new StaticHandler(content)) { BaseAddress = new Uri(baseAddress) };
    }

    private sealed class StaticHandler(string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            });
    }

    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, HttpStatusCode> _routes = new(StringComparer.OrdinalIgnoreCase);

        public void Add(HttpMethod method, string url, HttpStatusCode status) => _routes[$"{method} {url}"] = status;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_routes.TryGetValue($"{request.Method} {request.RequestUri!.AbsoluteUri}", out var status) ? status : HttpStatusCode.NotFound));
    }
}
