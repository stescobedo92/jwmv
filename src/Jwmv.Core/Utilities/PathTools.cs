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

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="path"/>, after normalization, is the root directory itself
    /// or a descendant of it. The comparison is done on full paths so <c>..</c> segments, absolute paths and UNC paths
    /// cannot escape the root, and a sibling directory that merely shares the root's name as a prefix is rejected.
    /// </summary>
    public static bool IsWithinDirectory(string rootDirectory, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        var candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (string.Equals(root, candidate, comparison))
        {
            return true;
        }

        return candidate.StartsWith(root + Path.DirectorySeparatorChar, comparison)
            || candidate.StartsWith(root + Path.AltDirectorySeparatorChar, comparison);
    }

    /// <summary>
    /// Like <see cref="IsWithinDirectory"/> but additionally rejects the root itself, which is what deletion
    /// code paths need: an installed version must live in its own sub-directory.
    /// </summary>
    public static bool IsStrictlyWithinDirectory(string rootDirectory, string path)
    {
        if (!IsWithinDirectory(rootDirectory, path))
        {
            return false;
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        var candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return !string.Equals(root, candidate, comparison);
    }
}
