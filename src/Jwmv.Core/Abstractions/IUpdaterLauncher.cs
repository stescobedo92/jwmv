namespace Jwmv.Core.Abstractions;

public interface IUpdaterLauncher
{
    /// <summary>Starts the detached updater script that replaces the executable once the current process exits.</summary>
    void Launch(string scriptPath);
}
