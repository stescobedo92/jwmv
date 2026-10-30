using Jwmv.Core.Abstractions;
using Jwmv.Core.Utilities;

namespace Jwmv.Infrastructure.Windows;

/// <summary>
/// The user's persistent PATH value, manipulated in its unexpanded form so <c>%VAR%</c> entries survive
/// and the REG_EXPAND_SZ kind is preserved (new PATH values are always written expandable).
/// </summary>
internal sealed record UserEnvironmentPath(string? Value, bool IsExpandable)
{
    private const string PathVariable = "Path";

    public static UserEnvironmentPath Load(IUserEnvironmentStore store)
    {
        var current = store.Get(PathVariable);
        return current is null
            ? new UserEnvironmentPath(null, IsExpandable: true)
            : new UserEnvironmentPath(current.Value, current.IsExpandable);
    }

    public UserEnvironmentPath Prepend(string entry) => this with { Value = PathTools.PrependPathEntry(Value, entry) };

    public UserEnvironmentPath Remove(string entry) =>
        Value is null ? this : this with { Value = PathTools.RemovePathEntry(Value, entry) };

    public void Save(IUserEnvironmentStore store)
    {
        if (Value is null)
        {
            // The user never had a Path value and nothing was added: do not create an empty one.
            return;
        }

        store.Set(PathVariable, Value, IsExpandable);
        store.SetProcessValue(PathVariable, BuildProcessPath(Value));
    }

    /// <summary>
    /// Composes the process PATH the way Windows does for new processes (machine entries first, then user
    /// entries), so the running process keeps the machine directories after the user value changes.
    /// </summary>
    private static string BuildProcessPath(string userValue)
    {
        var machine = Environment.ExpandEnvironmentVariables(
            Environment.GetEnvironmentVariable(PathVariable, EnvironmentVariableTarget.Machine) ?? string.Empty);
        var user = Environment.ExpandEnvironmentVariables(userValue);
        if (string.IsNullOrEmpty(machine))
        {
            return user;
        }

        return string.IsNullOrEmpty(user) ? machine : machine + Path.PathSeparator + user;
    }
}
