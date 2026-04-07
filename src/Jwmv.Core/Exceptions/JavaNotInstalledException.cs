namespace Jwmv.Core.Exceptions;

public sealed class JavaNotInstalledException(string identifier)
    : JwmvException($"The Java version '{identifier}' is not installed.");
