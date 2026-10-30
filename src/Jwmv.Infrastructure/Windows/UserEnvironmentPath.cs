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

    public UserEnvironmentPath Remove(string entry) => this with { Value = PathTools.RemovePathEntry(Value, entry) };

    public void Save(IUserEnvironmentStore store)
    {
        var value = Value ?? string.Empty;
        store.Set(PathVariable, value, IsExpandable);
        store.SetProcessValue(PathVariable, Environment.ExpandEnvironmentVariables(value));
    }
}
