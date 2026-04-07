namespace Jwmv.Core.Utilities;

public static class PathTools
{
    public static string PrependPathEntry(string? currentPath, string entry)
    {
        var entries = SplitPath(currentPath)
            .Where(path => !string.Equals(path, entry, StringComparison.OrdinalIgnoreCase))
            .ToList();
        entries.Insert(0, entry);
        return string.Join(Path.PathSeparator, entries);
    }

    public static string RemovePathEntry(string? currentPath, string entry)
    {
        return string.Join(
            Path.PathSeparator,
            SplitPath(currentPath).Where(path => !string.Equals(path, entry, StringComparison.OrdinalIgnoreCase)));
    }

    public static IReadOnlyList<string> SplitPath(string? value) =>
        (value ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToList();
}
