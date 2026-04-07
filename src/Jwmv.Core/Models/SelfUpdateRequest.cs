namespace Jwmv.Core.Models;

public sealed record SelfUpdateRequest
{
    public string? Repository { get; init; }
    public string? Tag { get; init; }
    public bool Force { get; init; }
}
