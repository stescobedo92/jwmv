using Jwmv.Core.Models;

namespace Jwmv.Core.Abstractions;

public interface IArchiveExtractor
{
    Task ExtractZipAsync(string archivePath, string destinationDirectory, IProgress<ArchiveExtractionProgress>? progress, CancellationToken cancellationToken);
}
