using System.Globalization;
using System.Xml.Linq;
using Jwmv.Core.Abstractions;
using Jwmv.Core.Models;
using Jwmv.Core.Utilities;

namespace Jwmv.Infrastructure.Catalog;

public sealed class MavenCatalogProvider(IHttpClientFactory httpClientFactory) : ISdkCatalogProvider, ISdkPackageDetailsProvider
{
    private const string MetadataPath = "maven2/org/apache/maven/apache-maven/maven-metadata.xml";

    // Strongest first. Older releases on archive.apache.org only publish md5/sha1.
    private static readonly string[] ArchiveChecksumTypes = ["sha512", "sha256", "sha1"];

    public SdkCandidate Candidate { get; } = new()
    {
        Name = "maven",
        DisplayName = "Apache Maven",
        Description = "Build and dependency management tool for Java projects.",
        WebsiteUri = new Uri("https://maven.apache.org/"),
        HomeEnvironmentVariable = "MAVEN_HOME",
        PrimaryExecutableName = OperatingSystem.IsWindows() ? "mvn.cmd" : "mvn"
    };

    public async Task<IReadOnlyList<SdkPackage>> GetPackagesAsync(CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient(ServiceCollectionExtensions.MavenCentralClientName);
        var xml = await client.GetStringAsync(MetadataPath, cancellationToken);
        var document = XDocument.Parse(xml);
        var versions = document.Descendants("version")
            .Select(element => element.Value.Trim())
            .Where(version => !string.IsNullOrWhiteSpace(version))
            .Where(SdkIdentifier.IsStableVersion)
            .ToList();

        return versions
            .Select(CreatePackage)
            .OrderBy(package => package.Version, Comparer<string>.Create(SdkIdentifier.CompareVersionsDescending))
            .ToList();
    }

    /// <summary>
    /// Prefers the checksum published on the Apache distribution archive, which is a different origin from the
    /// Maven Central download, so a tampered mirror cannot also serve a matching hash. Falls back to the
    /// same-origin checksum when the archive does not publish one.
    /// </summary>
    public async Task<SdkPackage> GetPackageDetailsAsync(SdkPackage package, CancellationToken cancellationToken)
    {
        if (!SdkIdentifier.IsSafeAlias(package.Version) || !SdkIdentifier.IsSafeFileName(package.FileName))
        {
            return package;
        }

        var major = GetMajorVersion(package.Version);
        if (major is null)
        {
            return package;
        }

        using var client = httpClientFactory.CreateClient(ServiceCollectionExtensions.ApacheArchiveClientName);
        foreach (var checksumType in ArchiveChecksumTypes)
        {
            var relativePath = $"dist/maven/maven-{major}/{package.Version}/binaries/{package.FileName}.{checksumType}";
            using var request = new HttpRequestMessage(HttpMethod.Head, relativePath);
            try
            {
                using var response = await client.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return package with
                    {
                        ChecksumUri = new Uri(client.BaseAddress!, relativePath),
                        ChecksumType = checksumType
                    };
                }
            }
            catch (HttpRequestException)
            {
                // The archive is unreachable; keep the Maven Central checksum instead of failing the install.
                break;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        return package;
    }

    private SdkPackage CreatePackage(string version)
    {
        var fileName = $"apache-maven-{version}-bin.zip";
        var path = $"maven2/org/apache/maven/apache-maven/{version}/{fileName}";
        var downloadUri = new Uri($"https://repo.maven.apache.org/{path}", UriKind.Absolute);
        return new SdkPackage
        {
            Id = $"maven-{version}",
            CandidateName = Candidate.Name,
            Version = version,
            Alias = version,
            DisplayName = version,
            ArchiveType = "zip",
            FileName = fileName,
            DownloadUri = downloadUri,
            ChecksumUri = new Uri($"{downloadUri}.sha512", UriKind.Absolute),
            ChecksumType = "sha512"
        };
    }

    private static int? GetMajorVersion(string version)
    {
        var digits = new string(version.TakeWhile(char.IsAsciiDigit).ToArray());
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var major) ? major : null;
    }
}
