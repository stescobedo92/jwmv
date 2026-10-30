using Jwmv.Core;
using Jwmv.Core.Abstractions;
using Jwmv.Core.Exceptions;
using Jwmv.Core.Models;
using Jwmv.Core.Utilities;

namespace Jwmv.Infrastructure.Windows;

public sealed class SdkEnvironmentService(IUserEnvironmentStore store) : ISdkEnvironmentService
{
    public Task ApplyDefaultAsync(InstalledSdkVersion installedVersion, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var candidate = SdkIdentifier.NormalizeCandidateName(installedVersion.CandidateName);
        var homeVariable = installedVersion.HomeEnvironmentVariable;
        if (!SdkIdentifier.IsSafeCandidateName(candidate) || !SdkIdentifier.IsSafeEnvironmentVariableName(homeVariable))
        {
            throw new JwmvException($"The installation manifest for {installedVersion.Alias} contains an invalid candidate or environment variable name.");
        }

        var path = UserEnvironmentPath.Load(store);
        var previousDefaultBin = store.Get(GetDefaultVariable(candidate, "BIN"))?.Value;
        if (!string.IsNullOrWhiteSpace(previousDefaultBin))
        {
            path = path.Remove(previousDefaultBin);
        }

        path = path.Prepend(installedVersion.BinDirectory);

        SetUserVariable(homeVariable, installedVersion.HomeDirectory);
        SetUserVariable(GetDefaultVariable(candidate, "VERSION"), installedVersion.Alias);
        SetUserVariable(GetDefaultVariable(candidate, "HOME"), installedVersion.HomeDirectory);
        SetUserVariable(GetDefaultVariable(candidate, "BIN"), installedVersion.BinDirectory);

        if (string.Equals(candidate, JwmvConstants.CandidateName, StringComparison.OrdinalIgnoreCase))
        {
            SetUserVariable(JwmvConstants.DefaultAliasVariable, installedVersion.Alias);
            SetUserVariable(JwmvConstants.DefaultHomeVariable, installedVersion.HomeDirectory);
            SetUserVariable(JwmvConstants.DefaultBinVariable, installedVersion.BinDirectory);
        }

        path.Save(store);
        store.NotifyChanged();
        return Task.CompletedTask;
    }

    public Task ClearDefaultAsync(string candidateName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var candidate = SdkIdentifier.NormalizeCandidateName(candidateName);
        if (!SdkIdentifier.IsSafeCandidateName(candidate))
        {
            throw new JwmvException($"'{candidateName}' is not a valid SDK candidate name.");
        }

        var path = UserEnvironmentPath.Load(store);
        var managedHome = store.Get(GetDefaultVariable(candidate, "HOME"))?.Value;
        var managedBin = store.Get(GetDefaultVariable(candidate, "BIN"))?.Value;
        if (!string.IsNullOrWhiteSpace(managedBin))
        {
            path = path.Remove(managedBin);
        }

        // Only remove the public home variable (JAVA_HOME, GRADLE_HOME, ...) when jwmv is the one that set it;
        // a value the user pointed at another installation is left untouched.
        RemoveUserVariableIfManaged(GetHomeVariable(candidate), managedHome);
        RemoveUserVariable(GetDefaultVariable(candidate, "VERSION"));
        RemoveUserVariable(GetDefaultVariable(candidate, "HOME"));
        RemoveUserVariable(GetDefaultVariable(candidate, "BIN"));

        if (string.Equals(candidate, JwmvConstants.CandidateName, StringComparison.OrdinalIgnoreCase))
        {
            var legacyManagedHome = store.Get(JwmvConstants.DefaultHomeVariable)?.Value;
            var legacyManagedBin = store.Get(JwmvConstants.DefaultBinVariable)?.Value;
            if (!string.IsNullOrWhiteSpace(legacyManagedBin))
            {
                path = path.Remove(legacyManagedBin);
            }

            RemoveUserVariableIfManaged("JAVA_HOME", legacyManagedHome);
            RemoveUserVariable(JwmvConstants.DefaultAliasVariable);
            RemoveUserVariable(JwmvConstants.DefaultHomeVariable);
            RemoveUserVariable(JwmvConstants.DefaultBinVariable);
        }

        path.Save(store);
        store.NotifyChanged();
        return Task.CompletedTask;
    }

    public static string GetActiveVariable(string candidateName, string suffix) =>
        $"{JwmvConstants.GenericActiveVariablePrefix}_{SdkIdentifier.NormalizeCandidateName(candidateName).ToUpperInvariant()}_{suffix}";

    public static string GetDefaultVariable(string candidateName, string suffix) =>
        $"{JwmvConstants.GenericDefaultVariablePrefix}_{SdkIdentifier.NormalizeCandidateName(candidateName).ToUpperInvariant()}_{suffix}";

    public static string GetHomeVariable(string candidateName) =>
        $"{SdkIdentifier.NormalizeCandidateName(candidateName).ToUpperInvariant()}_HOME";

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

    private void RemoveUserVariableIfManaged(string name, string? managedValue)
    {
        var current = store.Get(name)?.Value;
        if (current is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(managedValue) && string.Equals(current, managedValue, StringComparison.OrdinalIgnoreCase))
        {
            RemoveUserVariable(name);
        }
    }
}
