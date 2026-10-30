using System.Diagnostics;
using Jwmv.Core.Abstractions;
using Jwmv.Core.Models;
using Jwmv.Infrastructure.Windows;

namespace Jwmv.Infrastructure.Shell;

public sealed class ShellProfileIntegrationService(IAppContext appContext, ISdkVersionManager sdkVersionManager) : IShellProfileIntegrationService
{
    private const string StartMarker = "# >>> jwmv initialize >>>";
    private const string EndMarker = "# <<< jwmv initialize <<<";

    public string GetProfilePath(ShellKind shellKind) => shellKind switch
    {
        ShellKind.PowerShell => ResolvePowerShellProfilePath(),
        _ => throw new ArgumentOutOfRangeException(nameof(shellKind), shellKind, "Only PowerShell profile integration is implemented.")
    };

    public async Task<string> EnsureIntegratedAsync(ShellKind shellKind, string? profilePath, CancellationToken cancellationToken)
    {
        var targetPath = string.IsNullOrWhiteSpace(profilePath)
            ? GetProfilePath(shellKind)
            : Path.GetFullPath(profilePath);

        var bootstrap = await sdkVersionManager.BuildShellInitScriptAsync(shellKind, cancellationToken);
        var managedBlock = $"{StartMarker}{Environment.NewLine}{bootstrap.TrimEnd()}{Environment.NewLine}{EndMarker}";

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var currentContent = File.Exists(targetPath)
            ? await File.ReadAllTextAsync(targetPath, cancellationToken)
            : string.Empty;

        string updatedContent;
        var startIndex = currentContent.IndexOf(StartMarker, StringComparison.Ordinal);
        var endIndex = currentContent.IndexOf(EndMarker, StringComparison.Ordinal);
        if (startIndex >= 0 && endIndex > startIndex)
        {
            var endOfMarker = endIndex + EndMarker.Length;
            updatedContent = string.Concat(
                currentContent.AsSpan(0, startIndex),
                managedBlock,
                currentContent.AsSpan(endOfMarker));
        }
        else if (string.IsNullOrWhiteSpace(currentContent))
        {
            updatedContent = managedBlock + Environment.NewLine;
        }
        else
        {
            updatedContent = currentContent.TrimEnd() + Environment.NewLine + Environment.NewLine + managedBlock + Environment.NewLine;
        }

        await File.WriteAllTextAsync(targetPath, updatedContent, cancellationToken);
        return targetPath;
    }

    private string ResolvePowerShellProfilePath()
    {
        // Only fully-qualified PowerShell hosts are started, so a pwsh.exe planted in the current directory is never picked up.
        foreach (var shellPath in ProcessLocator.FindPowerShellCandidates())
        {
            var profile = TryReadProfileFromShell(shellPath);
            if (!string.IsNullOrWhiteSpace(profile))
            {
                return profile;
            }
        }

        return Path.Combine(appContext.UserProfileDirectory, "Documents", "PowerShell", "Microsoft.PowerShell_profile.ps1");
    }

    private static string? TryReadProfileFromShell(string shellPath)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = shellPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add("$PROFILE.CurrentUserCurrentHost");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(3000);
            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output) && Path.IsPathFullyQualified(output)
                ? output
                : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return null;
        }
    }
}
