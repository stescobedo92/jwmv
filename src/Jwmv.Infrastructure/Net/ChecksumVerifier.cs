using System.Globalization;
using System.Security.Cryptography;
using Jwmv.Core.Abstractions;
using Jwmv.Core.Exceptions;
using Jwmv.Core.Models;

namespace Jwmv.Infrastructure.Net;

public sealed class ChecksumVerifier : IChecksumVerifier
{
    public async Task<ChecksumVerificationResult> VerifyAsync(string filePath, string expectedChecksum, string checksumType, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedChecksum);
        ArgumentException.ThrowIfNullOrWhiteSpace(checksumType);

        var normalizedType = checksumType.Trim().ToLowerInvariant().Replace("-", string.Empty, StringComparison.Ordinal);
        if (normalizedType is not ("sha256" or "sha512" or "sha1" or "md5"))
        {
            throw new IntegrityVerificationException($"Checksum type '{checksumType}' is not supported (supported: sha256, sha512, sha1, md5).");
        }

        await using var stream = File.OpenRead(filePath);
        var hash = await HashAsync(normalizedType, stream, cancellationToken);

        var actual = Convert.ToHexString(hash).ToLower(CultureInfo.InvariantCulture);
        var expected = ExtractChecksumValue(expectedChecksum).ToLower(CultureInfo.InvariantCulture);
        return new ChecksumVerificationResult
        {
            IsValid = string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
            Expected = expected,
            Actual = actual,
            Algorithm = normalizedType,
            IsWeakAlgorithm = normalizedType is "sha1" or "md5"
        };
    }

    private static async Task<byte[]> HashAsync(string algorithm, Stream stream, CancellationToken cancellationToken)
    {
        switch (algorithm)
        {
            case "sha256":
                return await SHA256.HashDataAsync(stream, cancellationToken);
            case "sha512":
                return await SHA512.HashDataAsync(stream, cancellationToken);
            case "sha1":
                // Legacy algorithm published by some upstream catalogs; reported as weak to the caller.
#pragma warning disable CA5350
                return await SHA1.HashDataAsync(stream, cancellationToken);
#pragma warning restore CA5350
            default:
                // Legacy algorithm published by some upstream catalogs; reported as weak to the caller.
#pragma warning disable CA5351
                return await MD5.HashDataAsync(stream, cancellationToken);
#pragma warning restore CA5351
        }
    }

    private static string ExtractChecksumValue(string value) =>
        value.Trim().Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)[0];
}
