namespace Jwmv.Core.Exceptions;

public sealed class JavaPackageNotFoundException(string identifier)
    : JwmvException($"Unable to find a downloadable Java package that matches '{identifier}'.");
