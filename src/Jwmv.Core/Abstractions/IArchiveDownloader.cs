using Jwmv.Core.Models;

namespace Jwmv.Core.Abstractions;

public interface IArchiveDownloader
{
    Task DownloadAsync(Uri downloadUri, string destinationPath, IProgress<ArchiveDownloadProgress>? progress, CancellationToken cancellationToken);
}
