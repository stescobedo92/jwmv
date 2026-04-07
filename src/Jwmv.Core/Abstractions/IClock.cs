namespace Jwmv.Core.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
