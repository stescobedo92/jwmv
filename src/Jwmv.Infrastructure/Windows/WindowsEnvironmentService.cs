using Jwmv.Core;
using Jwmv.Core.Abstractions;
using Jwmv.Core.Models;
using Jwmv.Core.Utilities;

namespace Jwmv.Infrastructure.Windows;

public sealed class WindowsEnvironmentService : IWindowsEnvironmentService
{
    public Task ApplyDefaultAsync(InstalledJavaVersion javaVersion, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var binDirectory = Path.Combine(javaVersion.JavaHome, "bin");
        var currentUserPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User);
        var previousDefaultBin = Environment.GetEnvironmentVariable(JwmvConstants.DefaultBinVariable, EnvironmentVariableTarget.User);
        if (!string.IsNullOrWhiteSpace(previousDefaultBin))
        {
            currentUserPath = PathTools.RemovePathEntry(currentUserPath, previousDefaultBin);
        }

        var updatedPath = PathTools.PrependPathEntry(currentUserPath, binDirectory);

        SetUserVariable("JAVA_HOME", javaVersion.JavaHome);
        SetUserVariable(JwmvConstants.DefaultAliasVariable, javaVersion.Alias);
        SetUserVariable(JwmvConstants.DefaultHomeVariable, javaVersion.JavaHome);
        SetUserVariable(JwmvConstants.DefaultBinVariable, binDirectory);
        SetUserVariable("Path", updatedPath);
        EnvironmentBroadcast.Notify();
        return Task.CompletedTask;
    }

    public Task ClearDefaultAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var currentUserPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User);
        var previousDefaultBin = Environment.GetEnvironmentVariable(JwmvConstants.DefaultBinVariable, EnvironmentVariableTarget.User);
        if (!string.IsNullOrWhiteSpace(previousDefaultBin))
        {
            currentUserPath = PathTools.RemovePathEntry(currentUserPath, previousDefaultBin);
        }

        SetUserVariable("JAVA_HOME", null);
        SetUserVariable(JwmvConstants.DefaultAliasVariable, null);
        SetUserVariable(JwmvConstants.DefaultHomeVariable, null);
        SetUserVariable(JwmvConstants.DefaultBinVariable, null);
        SetUserVariable("Path", currentUserPath);
        EnvironmentBroadcast.Notify();
        return Task.CompletedTask;
    }

    private static void SetUserVariable(string name, string? value)
    {
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process);
    }
}
