namespace Jwmv.Core.Models;

public enum SelfUpdatePhase
{
    None = 0,
    Checking = 1,
    Downloading = 2,
    Extracting = 3,
    Finalizing = 4,
    Completed = 5
}
