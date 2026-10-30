using Jwmv.Core.Abstractions;
using Jwmv.Core.Exceptions;
using Jwmv.Core.Models;

namespace Jwmv.Infrastructure.Net;

public sealed class HttpArchiveDownloader(IHttpClientFactory httpClientFactory, long maxBytes = HttpArchiveDownloader.DefaultMaxBytes) : IArchiveDownloader
{
    /// <summary>Largest archive jwmv expects to download (GraalVM distributions are a few hundred MB).</summary>
    public const long DefaultMaxBytes = 4L * 1024 * 1024 * 1024;

    public async Task DownloadAsync(Uri downloadUri, string destinationPath, IProgress<ArchiveDownloadProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(downloadUri);
        if (!string.Equals(downloadUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new JwmvException($"Refusing to download '{downloadUri}': only HTTPS downloads are allowed.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        // Download into a temporary file and move it into place only once it is complete, so an interrupted
        // download can never be mistaken for a cached archive.
        var partialPath = $"{destinationPath}.{Guid.NewGuid():n}.part";

        try
        {
            using var client = httpClientFactory.CreateClient(ServiceCollectionExtensions.FoojayClientName);
            using var response = await client.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            if (totalBytes is > 0 && totalBytes > maxBytes)
            {
                throw new JwmvException($"'{downloadUri}' is {totalBytes} bytes, larger than the allowed {maxBytes} bytes.");
            }

            await using (var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var fileStream = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long transferred = 0;
                while (true)
                {
                    var read = await httpStream.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    transferred += read;
                    if (transferred > maxBytes)
                    {
                        throw new JwmvException($"'{downloadUri}' exceeded the allowed {maxBytes} bytes.");
                    }

                    await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    progress?.Report(new ArchiveDownloadProgress
                    {
                        BytesTransferred = transferred,
                        TotalBytes = totalBytes
                    });
                }
            }

            File.Move(partialPath, destinationPath, overwrite: true);
        }
        finally
        {
            TryDelete(partialPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of the partial download.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup of the partial download.
        }
    }
}
