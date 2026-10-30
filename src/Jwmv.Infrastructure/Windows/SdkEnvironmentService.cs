using Jwmv.Core;
using Jwmv.Core.Abstractions;
using Jwmv.Core.Models;
using Jwmv.Core.Utilities;

namespace Jwmv.Infrastructure.Windows;

public sealed class SdkEnvironmentService : ISdkEnvironmentService
{
    public Task ApplyDefaultAsync(InstalledSdkVersion installedVersion, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var currentUserPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User);
        var previousDefaultBin = Environment.GetEnvironmentVariable(GetDefaultVariable(installedVersion.CandidateName, "BIN"), EnvironmentVariableTarget.User);
        if (!string.IsNullOrWhiteSpace(previousDefaultBin))
        {
            currentUserPath = PathTools.RemovePathEntry(currentUserPath, previousDefaultBin);
        }

        var updatedPath = PathTools.PrependPathEntry(currentUserPath, installedVersion.BinDirectory);
        SetUserVariable(installedVersion.HomeEnvironmentVariable, installedVersion.HomeDirectory);
        SetUserVariable(GetDefaultVariable(installedVersion.CandidateName, "VERSION"), installedVersion.Alias);
        SetUserVariable(GetDefaultVariable(installedVersion.CandidateName, "HOME"), installedVersion.HomeDirectory);
        SetUserVariable(GetDefaultVariable(installedVersion.CandidateName, "BIN"), installedVersion.BinDirectory);

        if (string.Equals(installedVersion.CandidateName, JwmvConstants.CandidateName, StringComparison.OrdinalIgnoreCase))
        {
            SetUserVariable(JwmvConstants.DefaultAliasVariable, installedVersion.Alias);
            SetUserVariable(JwmvConstants.DefaultHomeVariable, installedVersion.HomeDirectory);
            SetUserVariable(JwmvConstants.DefaultBinVariable, installedVersion.BinDirectory);
        }

        SetUserVariable("Path", updatedPath);
        EnvironmentBroadcast.Notify();
        return Task.CompletedTask;
    }

    public Task ClearDefaultAsync(string candidateName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedCandidate = SdkIdentifier.NormalizeCandidateName(candidateName);
        var currentUserPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User);
        var previousDefaultBin = Environment.GetEnvironmentVariable(GetDefaultVariable(normalizedCandidate, "BIN"), EnvironmentVariableTarget.User);
        if (!string.IsNullOrWhiteSpace(previousDefaultBin))
        {
            currentUserPath = PathTools.RemovePathEntry(currentUserPath, previousDefaultBin);
        }

        SetUserVariable(GetHomeVariable(normalizedCandidate), null);
        SetUserVariable(GetDefaultVariable(normalizedCandidate, "VERSION"), null);
        SetUserVariable(GetDefaultVariable(normalizedCandidate, "HOME"), null);
        SetUserVariable(GetDefaultVariable(normalizedCandidate, "BIN"), null);
        if (string.Equals(normalizedCandidate, JwmvConstants.CandidateName, StringComparison.OrdinalIgnoreCase))
        {
            SetUserVariable("JAVA_HOME", null);
            SetUserVariable(JwmvConstants.DefaultAliasVariable, null);
            SetUserVariable(JwmvConstants.DefaultHomeVariable, null);
            SetUserVariable(JwmvConstants.DefaultBinVariable, null);
        }

        SetUserVariable("Path", currentUserPath);
        EnvironmentBroadcast.Notify();
        return Task.CompletedTask;
    }

    public static string GetActiveVariable(string candidateName, string suffix) =>
        $"{JwmvConstants.GenericActiveVariablePrefix}_{SdkIdentifier.NormalizeCandidateName(candidateName).ToUpperInvariant()}_{suffix}";

    public static string GetDefaultVariable(string candidateName, string suffix) =>
        $"{JwmvConstants.GenericDefaultVariablePrefix}_{SdkIdentifier.NormalizeCandidateName(candidateName).ToUpperInvariant()}_{suffix}";

    public static string GetHomeVariable(string candidateName) =>
        $"{SdkIdentifier.NormalizeCandidateName(candidateName).ToUpperInvariant()}_HOME";

    private static void SetUserVariable(string name, string? value)
    {
        if (!SdkIdentifier.IsSafeEnvironmentVariableName(name))
        {
            throw new Jwmv.Core.Exceptions.JwmvException($"'{name}' is not a valid environment variable name.");
        }

        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process);
    }
}
