namespace Jwmv.Core.Models;

public sealed record ProjectJavaConfiguration
{
    public required string JavaIdentifier { get; init; }
    public required string FilePath { get; init; }
}
