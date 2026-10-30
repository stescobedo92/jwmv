using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Jwmv.Core.Abstractions;
using Jwmv.Core.Models;
using Jwmv.Core.Utilities;

namespace Jwmv.Infrastructure.Catalog;

public sealed class GradleCatalogProvider(IHttpClientFactory httpClientFactory) : ISdkCatalogProvider
{
    public SdkCandidate Candidate { get; } = new()
    {
        Name = "gradle",
        DisplayName = "Gradle",
        Description = "Build automation tool for JVM and native projects.",
        WebsiteUri = new Uri("https://gradle.org/"),
        HomeEnvironmentVariable = "GRADLE_HOME",
        PrimaryExecutableName = OperatingSystem.IsWindows() ? "gradle.bat" : "gradle"
    };

    public async Task<IReadOnlyList<SdkPackage>> GetPackagesAsync(CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient(ServiceCollectionExtensions.GradleClientName);
        var versions = await client.GetFromJsonAsync<List<GradleVersion>>("versions/all", Storage.JsonFileHelper.SerializerOptions, cancellationToken)
            ?? [];

        return versions
            .Where(version => !version.Snapshot && !version.Nightly && !version.ReleaseNightly && !version.Broken)
            .Where(version => SdkIdentifier.IsStableVersion(version.Version))
            .Where(version => !string.IsNullOrWhiteSpace(version.DownloadUrl))
            .Select(version => new SdkPackage
            {
                Id = $"gradle-{version.Version}",
                CandidateName = Candidate.Name,
                Version = version.Version,
                Alias = version.Version,
                DisplayName = version.Version,
                ArchiveType = "zip",
                FileName = $"gradle-{version.Version}-bin.zip",
                DownloadUri = new Uri(version.DownloadUrl!, UriKind.Absolute),
                ChecksumUri = string.IsNullOrWhiteSpace(version.ChecksumUrl) ? null : new Uri(version.ChecksumUrl, UriKind.Absolute),
                Checksum = version.Checksum,
                ChecksumType = "sha256"
            })
            .OrderBy(package => package.Version, Comparer<string>.Create(SdkIdentifier.CompareVersionsDescending))
            .ToList();
    }

    private sealed class GradleVersion
    {
        [JsonPropertyName("version")]
        public string Version { get; init; } = string.Empty;

        [JsonPropertyName("snapshot")]
        public bool Snapshot { get; init; }

        [JsonPropertyName("nightly")]
        public bool Nightly { get; init; }

        [JsonPropertyName("releaseNightly")]
        public bool ReleaseNightly { get; init; }

        [JsonPropertyName("broken")]
        public bool Broken { get; init; }

        [JsonPropertyName("downloadUrl")]
        public string? DownloadUrl { get; init; }

        [JsonPropertyName("checksumUrl")]
        public string? ChecksumUrl { get; init; }

        [JsonPropertyName("checksum")]
        public string? Checksum { get; init; }
    }
}
