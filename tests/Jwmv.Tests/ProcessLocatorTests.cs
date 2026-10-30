using Jwmv.Infrastructure.Windows;

namespace Jwmv.Tests;

public sealed class ProcessLocatorTests
{
    [Fact]
    public void FindOnPath_IgnoresExecutablesInCurrentDirectory()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\projects\evil\pwsh.exe",
            @"C:\tools\pwsh.exe"
        };
        var path = string.Join(Path.PathSeparator, [@"C:\projects\evil", ".", @"C:\tools"]);

        var resolved = ProcessLocator.FindOnPath("pwsh.exe", path, @"C:\projects\evil", existing.Contains);

        Assert.Equal(@"C:\tools\pwsh.exe", resolved);
    }

    [Fact]
    public void FindOnPath_ReturnsNullWhenOnlyCurrentDirectoryHasTheExecutable()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\projects\evil\pwsh.exe" };
        var path = string.Join(Path.PathSeparator, [@"C:\projects\evil", @"C:\tools"]);

        Assert.Null(ProcessLocator.FindOnPath("pwsh.exe", path, @"C:\projects\evil", existing.Contains));
    }

    [Fact]
    public void FindOnPath_SkipsRelativeEntries()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"bin\pwsh.exe", @"C:\tools\pwsh.exe" };
        var path = string.Join(Path.PathSeparator, ["bin", @"C:\tools"]);

        Assert.Equal(@"C:\tools\pwsh.exe", ProcessLocator.FindOnPath("pwsh.exe", path, @"C:\somewhere", existing.Contains));
    }

    [Fact]
    public void FindPowerShellCandidates_PrefersPowerShell7UnderProgramFilesAndFallsBackToWindowsPowerShell()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Program Files\PowerShell\7\pwsh.exe",
            @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
            @"C:\projects\evil\pwsh.exe",
            @"C:\projects\evil\powershell.exe"
        };
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ProgramFiles"] = @"C:\Program Files",
            ["SystemRoot"] = @"C:\Windows",
            ["PATH"] = string.Join(Path.PathSeparator, [@"C:\projects\evil", @"C:\Windows\System32"])
        };

        var candidates = ProcessLocator.FindPowerShellCandidates(name => environment.GetValueOrDefault(name), @"C:\projects\evil", existing.Contains);

        Assert.Equal([@"C:\Program Files\PowerShell\7\pwsh.exe", @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"], candidates);
    }

    [Fact]
    public void FindSystemExecutable_ResolvesUnderSystem32()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\Windows\System32\where.exe", @"C:\projects\evil\where.exe" };
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["SystemRoot"] = @"C:\Windows" };

        Assert.Equal(@"C:\Windows\System32\where.exe", ProcessLocator.FindSystemExecutable("where.exe", name => environment.GetValueOrDefault(name), existing.Contains));
        Assert.Null(ProcessLocator.FindSystemExecutable("missing.exe", name => environment.GetValueOrDefault(name), existing.Contains));
    }
}
