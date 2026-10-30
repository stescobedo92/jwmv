using System.IO.Compression;
using Jwmv.Core.Exceptions;
using Jwmv.Core.Models;
using Jwmv.Infrastructure.Compression;

namespace Jwmv.Tests;

public sealed class ZipArchiveExtractorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jwmv-zip-tests", Guid.NewGuid().ToString("n"));

    public ZipArchiveExtractorTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task ExtractZipAsync_ExtractsFilesAndDirectoriesBelowDestination()
    {
        var archive = CreateArchive(("jdk/bin/java.exe", "binary"), ("jdk/release", "JAVA_VERSION=21"), ("jdk/lib/", string.Empty));
        var destination = Path.Combine(_root, "dest");
        var updates = new List<ArchiveExtractionProgress>();

        await new ZipArchiveExtractor().ExtractZipAsync(archive, destination, new Progress<ArchiveExtractionProgress>(updates.Add), CancellationToken.None);

        Assert.Equal("binary", await File.ReadAllTextAsync(Path.Combine(destination, "jdk", "bin", "java.exe")));
        Assert.Equal("JAVA_VERSION=21", await File.ReadAllTextAsync(Path.Combine(destination, "jdk", "release")));
        Assert.True(Directory.Exists(Path.Combine(destination, "jdk", "lib")));
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("../../evil.txt")]
    [InlineData("jdk/../../evil.txt")]
    [InlineData("C:/evil.txt")]
    [InlineData("/evil.txt")]
    public async Task ExtractZipAsync_RejectsEntriesThatEscapeTheDestination(string entryName)
    {
        var archive = CreateArchive((entryName, "evil"));
        var destination = Path.Combine(_root, "dest");

        await Assert.ThrowsAnyAsync<JwmvException>(() => new ZipArchiveExtractor().ExtractZipAsync(archive, destination, null, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(_root, "evil.txt")));
        Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "jwmv-zip-tests", "evil.txt")));
    }

    [Fact]
    public async Task ExtractZipAsync_RejectsSiblingDirectoryThatSharesThePrefix()
    {
        var archive = CreateArchive(("../dest-evil/x.txt", "evil"));
        var destination = Path.Combine(_root, "dest");

        await Assert.ThrowsAnyAsync<JwmvException>(() => new ZipArchiveExtractor().ExtractZipAsync(archive, destination, null, CancellationToken.None));
        Assert.False(Directory.Exists(Path.Combine(_root, "dest-evil")));
    }

    [Fact]
    public async Task ExtractZipAsync_EnforcesEntryCountLimit()
    {
        var archive = CreateArchive(("a.txt", "a"), ("b.txt", "b"), ("c.txt", "c"));
        var destination = Path.Combine(_root, "dest");

        await Assert.ThrowsAnyAsync<JwmvException>(() => new ZipArchiveExtractor(maxEntries: 2, maxTotalBytes: long.MaxValue).ExtractZipAsync(archive, destination, null, CancellationToken.None));
    }

    [Fact]
    public async Task ExtractZipAsync_EnforcesTotalUncompressedSizeLimit()
    {
        var archive = CreateArchive(("big.bin", new string('x', 2048)));
        var destination = Path.Combine(_root, "dest");

        await Assert.ThrowsAnyAsync<JwmvException>(() => new ZipArchiveExtractor(maxEntries: 100, maxTotalBytes: 1024).ExtractZipAsync(archive, destination, null, CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string CreateArchive(params (string Name, string Content)[] entries)
    {
        var archivePath = Path.Combine(_root, Guid.NewGuid().ToString("n") + ".zip");
        using var stream = File.Create(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name);
            if (name.EndsWith('/'))
            {
                continue;
            }

            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }

        return archivePath;
    }
}
