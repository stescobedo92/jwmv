using System.Net;
using System.Text.Json.Serialization;
using Jwmv.Core.Abstractions;
using Jwmv.Core.Models;
using Jwmv.Core.Utilities;

namespace Jwmv.Infrastructure.Catalog;

public sealed class FoojayCatalogClient(IHttpClientFactory httpClientFactory, IAppContext appContext) : IJavaCatalogClient
{
    public async Task<IReadOnlyList<JavaDistributionPackage>> GetAvailablePackagesAsync(CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient(ServiceCollectionExtensions.FoojayClientName);
        var uri = BuildPackagesUri(appContext);
        using var response = await client.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await System.Text.Json.JsonSerializer.DeserializeAsync<FoojayPackagesResponse>(stream, Storage.JsonFileHelper.SerializerOptions, cancellationToken)
            ?? throw new InvalidOperationException("Unable to parse the Foojay package response.");

        return payload.Result
            .Where(package => package.Links?.PackageDownloadRedirect is not null)
            .Select(MapPackage)
            .OrderBy(package => package.Alias, Comparer<string>.Create(JavaIdentifier.CompareAliasesDescending))
            .ToList();
    }

    private static Uri BuildPackagesUri(IAppContext appContext)
    {
        var architecture = appContext.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.Arm64 => "aarch64",
            System.Runtime.InteropServices.Architecture.X86 => "x86",
            _ => "x64"
        };

        var query = new Dictionary<string, string?>
        {
            ["package_type"] = "jdk",
            ["operating_system"] = "windows",
            ["release_status"] = "ga",
            ["archive_type"] = "zip",
            ["directly_downloadable"] = "true",
            ["architecture"] = architecture
        };

        var queryString = string.Join("&", query.Select(pair => $"{WebUtility.UrlEncode(pair.Key)}={WebUtility.UrlEncode(pair.Value)}"));
        return new Uri($"https://api.foojay.io/disco/v3.0/packages?{queryString}");
    }

    private static JavaDistributionPackage MapPackage(FoojayPackage source)
    {
        var distribution = source.Distribution ?? throw new InvalidOperationException("Missing distribution in Foojay package.");
        var javaVersion = source.JavaVersion ?? throw new InvalidOperationException("Missing java_version in Foojay package.");
        return new JavaDistributionPackage
        {
            Id = source.Id ?? Guid.NewGuid().ToString("n"),
            Alias = JavaIdentifier.BuildAlias(javaVersion, distribution),
            Distribution = distribution,
            DistributionAlias = DistributionAlias.ToAlias(distribution),
            JavaVersion = javaVersion,
            DistributionVersion = source.DistributionVersion ?? string.Empty,
            ArchiveType = source.ArchiveType ?? "zip",
            Architecture = source.Architecture ?? string.Empty,
            OperatingSystem = source.OperatingSystem ?? "windows",
            PackageType = source.PackageType ?? "jdk",
            FileName = source.FileName ?? $"{javaVersion}-{distribution}.zip",
            DownloadUri = new Uri(source.Links!.PackageDownloadRedirect!, UriKind.Absolute),
            PackageInfoUri = source.Links.PackageInfoUri is null ? null : new Uri(source.Links.PackageInfoUri, UriKind.Absolute),
            DirectlyDownloadable = source.DirectlyDownloadable,
            IsLatestBuildAvailable = source.LatestBuildAvailable,
            ReleaseStatus = source.ReleaseStatus ?? "ga",
            TermOfSupport = source.TermOfSupport ?? string.Empty,
            Size = source.Size
        };
    }

    private sealed class FoojayPackagesResponse
    {
        [JsonPropertyName("result")]
        public List<FoojayPackage> Result { get; init; } = [];
    }

    private sealed class FoojayPackage
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("archive_type")]
        public string? ArchiveType { get; init; }

        [JsonPropertyName("distribution")]
        public string? Distribution { get; init; }

        [JsonPropertyName("java_version")]
        public string? JavaVersion { get; init; }

        [JsonPropertyName("distribution_version")]
        public string? DistributionVersion { get; init; }

        [JsonPropertyName("latest_build_available")]
        public bool LatestBuildAvailable { get; init; }

        [JsonPropertyName("release_status")]
        public string? ReleaseStatus { get; init; }

        [JsonPropertyName("term_of_support")]
        public string? TermOfSupport { get; init; }

        [JsonPropertyName("operating_system")]
        public string? OperatingSystem { get; init; }

        [JsonPropertyName("architecture")]
        public string? Architecture { get; init; }

        [JsonPropertyName("package_type")]
        public string? PackageType { get; init; }

        [JsonPropertyName("directly_downloadable")]
        public bool DirectlyDownloadable { get; init; }

        [JsonPropertyName("filename")]
        public string? FileName { get; init; }

        [JsonPropertyName("size")]
        public long Size { get; init; }

        [JsonPropertyName("links")]
        public FoojayLinks? Links { get; init; }
    }

    private sealed class FoojayLinks
    {
        [JsonPropertyName("pkg_info_uri")]
        public string? PackageInfoUri { get; init; }

        [JsonPropertyName("pkg_download_redirect")]
        public string? PackageDownloadRedirect { get; init; }
    }
}
