namespace Jwmv.Core.Models;

public sealed record ActiveJavaSelection
{
    public string? Alias { get; init; }
    public string? JavaHome { get; init; }
    public string? BinDirectory { get; init; }
    public JavaActivationSource Source { get; init; }
    public string? ProjectFilePath { get; init; }

    public bool IsResolved => !string.IsNullOrWhiteSpace(Alias) && !string.IsNullOrWhiteSpace(JavaHome);
}
