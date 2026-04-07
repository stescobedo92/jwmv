using System.IO.Compression;
using Jwmv.Core.Abstractions;
using Jwmv.Core.Models;

namespace Jwmv.Infrastructure.Compression;

public sealed class ZipArchiveExtractor : IArchiveExtractor
{
    public Task ExtractZipAsync(string archivePath, string destinationDirectory, IProgress<ArchiveExtractionProgress>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destinationDirectory);
        using var archive = ZipFile.OpenRead(archivePath);
        var totalEntries = archive.Entries.Count;
        var processedEntries = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fullPath = Path.GetFullPath(Path.Combine(destinationDirectory, entry.FullName));
            if (!fullPath.StartsWith(Path.GetFullPath(destinationDirectory), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Unsafe zip entry detected: {entry.FullName}");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(fullPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            entry.ExtractToFile(fullPath, overwrite: true);
            processedEntries++;
            progress?.Report(new ArchiveExtractionProgress
            {
                EntriesProcessed = processedEntries,
                TotalEntries = totalEntries
            });
        }

        return Task.CompletedTask;
    }
}
