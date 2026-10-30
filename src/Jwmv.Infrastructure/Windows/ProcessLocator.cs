namespace Jwmv.Infrastructure.Windows;

/// <summary>
/// Resolves helper executables to absolute paths so that <see cref="System.Diagnostics.Process.Start(System.Diagnostics.ProcessStartInfo)"/>
/// never falls back to CreateProcess' search order, which looks in the current directory before PATH.
/// </summary>
public static class ProcessLocator
{
    public static string? FindPowerShell() =>
        FindPowerShell(Environment.GetEnvironmentVariable, Environment.CurrentDirectory, File.Exists);

    public static IReadOnlyList<string> FindPowerShellCandidates() =>
        FindPowerShellCandidates(Environment.GetEnvironmentVariable, Environment.CurrentDirectory, File.Exists);

    public static string? FindSystemExecutable(string fileName) =>
        FindSystemExecutable(fileName, Environment.GetEnvironmentVariable, File.Exists);

    public static string? FindPowerShell(Func<string, string?> getEnvironmentVariable, string currentDirectory, Func<string, bool> fileExists)
    {
        var candidates = FindPowerShellCandidates(getEnvironmentVariable, currentDirectory, fileExists);
        return candidates.Count > 0 ? candidates[0] : null;
    }

    /// <summary>
    /// Returns absolute paths of installed PowerShell hosts, PowerShell 7 (pwsh.exe) first and Windows PowerShell last.
    /// </summary>
    public static IReadOnlyList<string> FindPowerShellCandidates(Func<string, string?> getEnvironmentVariable, string currentDirectory, Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        ArgumentNullException.ThrowIfNull(fileExists);

        var candidates = new List<string>();

        foreach (var programFilesVariable in new[] { "ProgramFiles", "ProgramW6432", "ProgramFiles(x86)" })
        {
            var programFiles = getEnvironmentVariable(programFilesVariable);
            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                AddIfExists(candidates, Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe"), fileExists);
            }
        }

        var pwshOnPath = FindOnPath("pwsh.exe", getEnvironmentVariable("PATH"), currentDirectory, fileExists);
        if (pwshOnPath is not null)
        {
            AddIfExists(candidates, pwshOnPath, fileExists);
        }

        var windowsPowerShell = FindSystemExecutable(Path.Combine("WindowsPowerShell", "v1.0", "powershell.exe"), getEnvironmentVariable, fileExists);
        if (windowsPowerShell is not null)
        {
            AddIfExists(candidates, windowsPowerShell, fileExists);
        }

        return candidates;
    }

    /// <summary>
    /// Resolves a file under <c>%SystemRoot%\System32</c>, e.g. <c>where.exe</c>.
    /// </summary>
    public static string? FindSystemExecutable(string fileName, Func<string, string?> getEnvironmentVariable, Func<string, bool> fileExists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
        ArgumentNullException.ThrowIfNull(fileExists);

        var systemRoot = getEnvironmentVariable("SystemRoot");
        if (string.IsNullOrWhiteSpace(systemRoot))
        {
            systemRoot = getEnvironmentVariable("windir");
        }

        if (string.IsNullOrWhiteSpace(systemRoot))
        {
            return null;
        }

        var candidate = Path.GetFullPath(Path.Combine(systemRoot, "System32", fileName));
        return fileExists(candidate) ? candidate : null;
    }

    /// <summary>
    /// Searches PATH for <paramref name="fileName"/>, skipping the current directory and any relative entry.
    /// </summary>
    public static string? FindOnPath(string fileName, string? pathVariable, string currentDirectory, Func<string, bool> fileExists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(fileExists);

        var current = NormalizeDirectory(currentDirectory);
        foreach (var entry in (pathVariable ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var directory = entry.Trim('"');
            if (!Path.IsPathFullyQualified(directory))
            {
                continue;
            }

            if (current is not null && string.Equals(NormalizeDirectory(directory), current, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var candidate = Path.Combine(directory, fileName);
            if (fileExists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static void AddIfExists(List<string> candidates, string path, Func<string, bool> fileExists)
    {
        if (fileExists(path) && !candidates.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            candidates.Add(path);
        }
    }

    private static string? NormalizeDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
