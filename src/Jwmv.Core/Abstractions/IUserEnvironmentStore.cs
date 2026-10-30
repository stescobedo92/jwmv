using Jwmv.Core.Models;

namespace Jwmv.Core.Abstractions;

/// <summary>
/// Persistent per-user environment variables (HKCU\Environment on Windows). Values are read and written
/// unexpanded so entries such as <c>%USERPROFILE%\bin</c> keep their variable form.
/// </summary>
public interface IUserEnvironmentStore
{
    UserEnvironmentValue? Get(string name);

    void Set(string name, string value, bool isExpandable);

    void Remove(string name);

    /// <summary>Mirrors a change into the current process so the running command observes it immediately.</summary>
    void SetProcessValue(string name, string? value);

    /// <summary>Tells other applications (explorer, new shells) that the user environment changed.</summary>
    void NotifyChanged();
}
