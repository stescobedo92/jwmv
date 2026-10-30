using Jwmv.Core.Utilities;

namespace Jwmv.Infrastructure.Shell;

/// <summary>
/// Shell integration records the absolute path of the executable in the user's PowerShell profile and runs it on
/// every prompt. Running <c>integrate</c> from a transient or remote location therefore plants a persistent
/// execution point that is easy to tamper with; this advisor produces the warning shown in that case.
/// </summary>
public static class InstallLocationAdvisor
{
    private static readonly string[] TransientProfileFolders = ["Downloads", "Desktop"];

    public static string? GetWarning(string executablePath, string userProfileDirectory, string tempDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        if (executablePath.StartsWith(@"\\", StringComparison.Ordinal) || executablePath.StartsWith("//", StringComparison.Ordinal))
        {
            return $"jwmv is running from a network location ({executablePath}). The profile hook will execute it on every prompt; copy jwmv.exe to a local folder such as ~\\.jwmv\\bin and run integrate again.";
        }

        if (!string.IsNullOrWhiteSpace(tempDirectory) && PathTools.IsWithinDirectory(tempDirectory, executablePath))
        {
            return $"jwmv is running from a temporary folder ({executablePath}). The profile hook will execute it on every prompt; move jwmv.exe to a permanent folder such as ~\\.jwmv\\bin and run integrate again.";
        }

        if (!string.IsNullOrWhiteSpace(userProfileDirectory))
        {
            foreach (var folder in TransientProfileFolders)
            {
                if (PathTools.IsWithinDirectory(Path.Combine(userProfileDirectory, folder), executablePath))
                {
                    return $"jwmv is running from {folder} ({executablePath}). The profile hook will execute it on every prompt; move jwmv.exe to a permanent folder such as ~\\.jwmv\\bin and run integrate again.";
                }
            }
        }

        return null;
    }
}
