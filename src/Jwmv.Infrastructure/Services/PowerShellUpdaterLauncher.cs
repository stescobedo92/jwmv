using System.Diagnostics;
using Jwmv.Core.Abstractions;
using Jwmv.Core.Exceptions;
using Jwmv.Infrastructure.Windows;

namespace Jwmv.Infrastructure.Services;

public sealed class PowerShellUpdaterLauncher : IUpdaterLauncher
{
    public void Launch(string scriptPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);

        var shells = ProcessLocator.FindPowerShellCandidates();
        if (shells.Count == 0)
        {
            throw new JwmvException("Unable to locate pwsh.exe or powershell.exe to finish the self-update.");
        }

        Exception? lastError = null;
        foreach (var shell in shells)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = shell,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetDirectoryName(scriptPath)
                };
                startInfo.ArgumentList.Add("-NoLogo");
                startInfo.ArgumentList.Add("-NoProfile");
                startInfo.ArgumentList.Add("-NonInteractive");
                startInfo.ArgumentList.Add("-ExecutionPolicy");
                startInfo.ArgumentList.Add("Bypass");
                startInfo.ArgumentList.Add("-File");
                startInfo.ArgumentList.Add(scriptPath);

                using var process = Process.Start(startInfo);
                if (process is not null)
                {
                    return;
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
            {
                lastError = exception;
            }
        }

        throw new JwmvException($"Unable to start PowerShell to finish the self-update: {lastError?.Message ?? "no PowerShell host could be started."}");
    }
}
