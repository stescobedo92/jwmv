namespace Jwmv.Core.Models;

public sealed record InstallSdkResult
{
    public required InstalledSdkVersion InstalledVersion { get; init; }
    public bool AlreadyInstalled { get; init; }
    public bool DefaultWasUpdated { get; init; }

    /// <summary>
    /// Set when the archive was installed without a checksum (explicitly allowed by the caller) or was verified
    /// with a weak hash algorithm. <see langword="null"/> when the download was verified with SHA-256/SHA-512.
    /// </summary>
    public string? IntegrityWarning { get; init; }
}
