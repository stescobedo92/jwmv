using System.Runtime.Versioning;
using Jwmv.Core.Abstractions;
using Jwmv.Core.Exceptions;
using Jwmv.Core.Models;
using Jwmv.Core.Utilities;
using Microsoft.Win32;

namespace Jwmv.Infrastructure.Windows;

/// <summary>
/// Reads and writes HKCU\Environment directly instead of going through
/// <see cref="Environment.SetEnvironmentVariable(string, string?, EnvironmentVariableTarget)"/>, which reads values
/// already expanded and writes them back as REG_SZ, silently destroying <c>%VAR%</c> references in the user PATH.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RegistryUserEnvironmentStore : IUserEnvironmentStore
{
    private const string EnvironmentKeyPath = "Environment";

    public UserEnvironmentValue? Get(string name)
    {
        EnsureValidName(name);
        using var key = Registry.CurrentUser.OpenSubKey(EnvironmentKeyPath, writable: false);
        if (key is null)
        {
            return null;
        }

        var kind = key.GetValueKind(name);
        var raw = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return raw switch
        {
            string value => new UserEnvironmentValue(value, kind == RegistryValueKind.ExpandString),
            _ => null
        };
    }

    public void Set(string name, string value, bool isExpandable)
    {
        EnsureValidName(name);
        ArgumentNullException.ThrowIfNull(value);
        using var key = Registry.CurrentUser.CreateSubKey(EnvironmentKeyPath, writable: true)
            ?? throw new JwmvException("Unable to open HKCU\\Environment for writing.");
        key.SetValue(name, value, isExpandable ? RegistryValueKind.ExpandString : RegistryValueKind.String);
    }

    public void Remove(string name)
    {
        EnsureValidName(name);
        using var key = Registry.CurrentUser.OpenSubKey(EnvironmentKeyPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    public void SetProcessValue(string name, string? value)
    {
        EnsureValidName(name);
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process);
    }

    public void NotifyChanged() => EnvironmentBroadcast.Notify();

    private static void EnsureValidName(string name)
    {
        if (!SdkIdentifier.IsSafeEnvironmentVariableName(name))
        {
            throw new JwmvException($"'{name}' is not a valid environment variable name.");
        }
    }
}
