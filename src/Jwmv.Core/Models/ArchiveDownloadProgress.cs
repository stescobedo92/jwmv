namespace Jwmv.Core.Models;

public sealed record ArchiveDownloadProgress
{
    public long BytesTransferred { get; init; }
    public long? TotalBytes { get; init; }
}
