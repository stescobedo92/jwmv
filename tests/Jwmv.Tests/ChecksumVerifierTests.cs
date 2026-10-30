using System.Security.Cryptography;
using System.Text;
using Jwmv.Core.Exceptions;
using Jwmv.Infrastructure.Net;

namespace Jwmv.Tests;

public sealed class ChecksumVerifierTests : IDisposable
{
    private readonly string _filePath = Path.Combine(Path.GetTempPath(), "jwmv-checksum-tests", Guid.NewGuid().ToString("n") + ".bin");
    private readonly byte[] _content = Encoding.UTF8.GetBytes("jwmv checksum fixture");

    public ChecksumVerifierTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllBytes(_filePath, _content);
    }

    [Fact]
    public async Task VerifyAsync_AcceptsSha256InChecksumFileFormat()
    {
        var expected = Convert.ToHexString(SHA256.HashData(_content)).ToLowerInvariant();

        var result = await new ChecksumVerifier().VerifyAsync(_filePath, $"{expected} *archive.zip", "sha256", CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.False(result.IsWeakAlgorithm);
    }

    [Fact]
    public async Task VerifyAsync_DetectsMismatch()
    {
        var result = await new ChecksumVerifier().VerifyAsync(_filePath, new string('0', 64), "SHA256", CancellationToken.None);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("sha1")]
    [InlineData("md5")]
    public async Task VerifyAsync_SupportsLegacyAlgorithmsButFlagsThemAsWeak(string algorithm)
    {
        // The legacy algorithms are only used here to compute the hash an upstream catalog would publish.
#pragma warning disable CA5350, CA5351
        var expected = algorithm == "sha1"
            ? Convert.ToHexString(SHA1.HashData(_content))
            : Convert.ToHexString(MD5.HashData(_content));
#pragma warning restore CA5350, CA5351

        var result = await new ChecksumVerifier().VerifyAsync(_filePath, expected, algorithm, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.True(result.IsWeakAlgorithm);
    }

    [Fact]
    public async Task VerifyAsync_RejectsUnsupportedAlgorithmWithIntegrityException()
    {
        await Assert.ThrowsAsync<IntegrityVerificationException>(() => new ChecksumVerifier().VerifyAsync(_filePath, "abc", "crc32", CancellationToken.None));
    }

    public void Dispose()
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
    }
}
