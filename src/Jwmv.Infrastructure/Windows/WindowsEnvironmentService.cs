using Jwmv.Core;
using Jwmv.Core.Abstractions;
using Jwmv.Core.Models;

namespace Jwmv.Infrastructure.Windows;

/// <summary>Legacy Java-only environment service; the multi-SDK equivalent is <see cref="SdkEnvironmentService"/>.</summary>
public sealed class WindowsEnvironmentService(IUserEnvironmentStore store) : IWindowsEnvironmentService
{
    public Task ApplyDefaultAsync(InstalledJavaVersion javaVersion, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var binDirectory = Path.Combine(javaVersion.JavaHome, "bin");
        var path = UserEnvironmentPath.Load(store);
        var previousDefaultBin = store.Get(JwmvConstants.DefaultBinVariable)?.Value;
        if (!string.IsNullOrWhiteSpace(previousDefaultBin))
        {
            path = path.Remove(previousDefaultBin);
        }

        path = path.Prepend(binDirectory);

        SetUserVariable("JAVA_HOME", javaVersion.JavaHome);
        SetUserVariable(JwmvConstants.DefaultAliasVariable, javaVersion.Alias);
        SetUserVariable(JwmvConstants.DefaultHomeVariable, javaVersion.JavaHome);
        SetUserVariable(JwmvConstants.DefaultBinVariable, binDirectory);
        path.Save(store);
        store.NotifyChanged();
        return Task.CompletedTask;
    }

    public Task ClearDefaultAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = UserEnvironmentPath.Load(store);
        var managedHome = store.Get(JwmvConstants.DefaultHomeVariable)?.Value;
        var managedBin = store.Get(JwmvConstants.DefaultBinVariable)?.Value;
        if (!string.IsNullOrWhiteSpace(managedBin))
        {
            path = path.Remove(managedBin);
        }

        var currentJavaHome = store.Get("JAVA_HOME")?.Value;
        if (currentJavaHome is not null && !string.IsNullOrWhiteSpace(managedHome) && string.Equals(currentJavaHome, managedHome, StringComparison.OrdinalIgnoreCase))
        {
            RemoveUserVariable("JAVA_HOME");
        }

        RemoveUserVariable(JwmvConstants.DefaultAliasVariable);
        RemoveUserVariable(JwmvConstants.DefaultHomeVariable);
        RemoveUserVariable(JwmvConstants.DefaultBinVariable);
        path.Save(store);
        store.NotifyChanged();
        return Task.CompletedTask;
    }

    private void SetUserVariable(string name, string value)
    {
        store.Set(name, value, isExpandable: false);
        store.SetProcessValue(name, value);
    }

    private void RemoveUserVariable(string name)
    {
        store.Remove(name);
        store.SetProcessValue(name, null);
    }
}
