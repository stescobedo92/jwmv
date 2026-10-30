namespace Jwmv.Core.Exceptions;

/// <summary>
/// Raised when a downloaded artifact cannot be proven authentic: a missing or mismatched checksum,
/// an unsupported hash algorithm, or a release signature that does not verify.
/// </summary>
public sealed class IntegrityVerificationException(string message) : JwmvException(message);
