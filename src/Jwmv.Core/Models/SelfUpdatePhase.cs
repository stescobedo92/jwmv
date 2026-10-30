namespace Jwmv.Core.Models;

public enum SelfUpdatePhase
{
    None = 0,
    Checking = 1,
    Downloading = 2,
    Verifying = 3,
    Extracting = 4,
    Finalizing = 5,
    Completed = 6
}
