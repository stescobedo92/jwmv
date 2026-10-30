using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Jwmv.Core.Abstractions;
using Jwmv.Core.Models;
using Jwmv.Core.Utilities;

namespace Jwmv.Infrastructure.Catalog;

public sealed class KotlinCatalogProvider(IHttpClientFactory httpClientFactory) : ISdkCatalogProvider
{
    public SdkCandidate Candidate { get; } = new()
    {
        Name = "kotlin",
        DisplayName = "Kotlin",
        Description = "Kotlin command-line compiler from JetBrains releases.",
        WebsiteUri = new Uri("https://kotlinlang.org/"),
        HomeEnvironmentVariable = "KOTLIN_HOME",
        PrimaryExecutableName = OperatingSystem.IsWindows() ? "kotlinc.bat" : "kotlinc"
    };

    public async Task<IReadOnlyList<SdkPackage>> GetPackagesAsync(CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient(ServiceCollectionExtensions.GitHubClientName);
        var releases = await client.GetFromJsonAsync<List<GitHubRelease>>("repos/JetBrains/kotlin/releases?per_page=30", Storage.JsonFileHelper.SerializerOptions, cancellationToken)
            ?? [];

        return releases
            .Where(release => !release.Draft && !release.Prerelease)
            .Select(ToPackage)
            .Where(package => package is not null)
            .Select(package => package!)
            .OrderBy(package => package.Version, Comparer<string>.Create(SdkIdentifier.CompareVersionsDescending))
            .ToList();
    }

    private SdkPackage? ToPackage(GitHubRelease release)
    {
        var version = release.TagName.TrimStart('v', 'V');
        if (!SdkIdentifier.IsStableVersion(version))
        {
            return null;
        }

        var zip = release.Assets.FirstOrDefault(asset => string.Equals(asset.Name, $"kotlin-compiler-{version}.zip", StringComparison.OrdinalIgnoreCase));
        if (zip is null)
        {
            return null;
        }

        var checksum = release.Assets.FirstOrDefault(asset => string.Equals(asset.Name, $"{zip.Name}.sha256", StringComparison.OrdinalIgnoreCase));
        return new SdkPackage
        {
            Id = $"kotlin-{version}",
            CandidateName = Candidate.Name,
            Version = version,
            Alias = version,
            DisplayName = version,
            ArchiveType = "zip",
            FileName = zip.Name,
            DownloadUri = new Uri(zip.BrowserDownloadUrl, UriKind.Absolute),
            ChecksumUri = checksum is null ? null : new Uri(checksum.BrowserDownloadUrl, UriKind.Absolute),
            ChecksumType = checksum is null ? null : "sha256",
            Size = zip.Size
        };
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; init; } = string.Empty;

        [JsonPropertyName("draft")]
        public bool Draft { get; init; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; init; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; init; } = [];
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; init; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; init; }
    }
}
