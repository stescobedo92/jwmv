using Jwmv.Core.Abstractions;
using Jwmv.Core.Models;

namespace Jwmv.Infrastructure.Net;

public sealed class HttpArchiveDownloader(IHttpClientFactory httpClientFactory) : IArchiveDownloader
{
    public async Task DownloadAsync(Uri downloadUri, string destinationPath, IProgress<ArchiveDownloadProgress>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        using var client = httpClientFactory.CreateClient(ServiceCollectionExtensions.FoojayClientName);
        using var response = await client.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        await using var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = File.Create(destinationPath);
        var buffer = new byte[81920];
        long transferred = 0;
        while (true)
        {
            var read = await httpStream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            transferred += read;
            progress?.Report(new ArchiveDownloadProgress
            {
                BytesTransferred = transferred,
                TotalBytes = totalBytes
            });
        }
    }
}
