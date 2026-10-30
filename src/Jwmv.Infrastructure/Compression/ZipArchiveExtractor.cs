using System.IO.Compression;
using Jwmv.Core.Abstractions;
using Jwmv.Core.Exceptions;
using Jwmv.Core.Models;
using Jwmv.Core.Utilities;

namespace Jwmv.Infrastructure.Compression;

/// <summary>
/// Extracts ZIP archives while guaranteeing that every entry lands strictly below the destination directory
/// (Zip Slip, including sibling directories that share the destination's name as a prefix) and that the
/// archive cannot exhaust the disk through an excessive number of entries or uncompressed bytes.
/// </summary>
public sealed class ZipArchiveExtractor(int maxEntries = ZipArchiveExtractor.DefaultMaxEntries, long maxTotalBytes = ZipArchiveExtractor.DefaultMaxTotalBytes) : IArchiveExtractor
{
    public const int DefaultMaxEntries = 100_000;

    /// <summary>Large JDK distributions (GraalVM) exceed 1 GiB uncompressed; 8 GiB leaves generous headroom.</summary>
    public const long DefaultMaxTotalBytes = 8L * 1024 * 1024 * 1024;

    public async Task ExtractZipAsync(string archivePath, string destinationDirectory, IProgress<ArchiveExtractionProgress>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destinationDirectory);
        var destinationRoot = Path.GetFullPath(destinationDirectory);

        using var archive = ZipFile.OpenRead(archivePath);
        var totalEntries = archive.Entries.Count;
        if (totalEntries > maxEntries)
        {
            throw new JwmvException($"The archive contains {totalEntries} entries, more than the allowed {maxEntries}.");
        }

        // Validate every entry before writing anything so a malicious entry cannot leave partial output behind.
        var targets = new List<(ZipArchiveEntry Entry, string FullPath)>(totalEntries);
        long declaredBytes = 0;
        foreach (var entry in archive.Entries)
        {
            var fullPath = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
            if (!PathTools.IsStrictlyWithinDirectory(destinationRoot, fullPath))
            {
                throw new JwmvException($"Unsafe zip entry detected: {entry.FullName}");
            }

            declaredBytes += Math.Max(0, entry.Length);
            if (declaredBytes > maxTotalBytes)
            {
                throw new JwmvException($"The archive would expand to more than the allowed {maxTotalBytes} bytes.");
            }

            targets.Add((entry, fullPath));
        }

        var processedEntries = 0;
        long writtenBytes = 0;
        var buffer = new byte[81920];
        foreach (var (entry, fullPath) in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(fullPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await using (var source = entry.Open())
            await using (var target = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length, useAsync: true))
            {
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    // The declared sizes come from the archive itself, so the actual bytes are counted as well.
                    writtenBytes += read;
                    if (writtenBytes > maxTotalBytes)
                    {
                        throw new JwmvException($"The archive expanded to more than the allowed {maxTotalBytes} bytes.");
                    }

                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            TrySetLastWriteTime(fullPath, entry.LastWriteTime);
            processedEntries++;
            progress?.Report(new ArchiveExtractionProgress
            {
                EntriesProcessed = processedEntries,
                TotalEntries = totalEntries
            });
        }
    }

    private static void TrySetLastWriteTime(string path, DateTimeOffset lastWriteTime)
    {
        try
        {
            File.SetLastWriteTime(path, lastWriteTime.DateTime);
        }
        catch (ArgumentOutOfRangeException)
        {
            // Some archives carry timestamps outside the supported range; keeping "now" is harmless.
        }
        catch (IOException)
        {
            // Preserving timestamps is best-effort.
        }
        catch (UnauthorizedAccessException)
        {
            // Preserving timestamps is best-effort.
        }
    }
}
