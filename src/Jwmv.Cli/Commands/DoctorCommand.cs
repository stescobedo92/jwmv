using System.Diagnostics;
using Jwmv.Core;
using Jwmv.Core.Abstractions;
using Jwmv.Core.Models;
using Jwmv.Core.Utilities;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Jwmv.Cli.Commands;

public sealed class DoctorCommand(
    IJavaVersionManager manager,
    IAppContext appContext,
    IShellProfileIntegrationService integrationService,
    IAnsiConsole console) : AsyncCommand<DoctorCommand.Settings>
{
    public sealed class Settings : CommandSettings;

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var current = await manager.ResolveCurrentAsync(null, cancellationToken);
        var installed = await manager.ListInstalledAsync(cancellationToken);
        var profilePath = integrationService.GetProfilePath(ShellKind.PowerShell);
        var profileIntegrated = File.Exists(profilePath)
            && (await File.ReadAllTextAsync(profilePath, cancellationToken)).Contains("jwmv initialize", StringComparison.Ordinal);
        var processPathEntries = PathTools.SplitPath(appContext.GetEnvironmentVariable("Path"));
        var userPathValue = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User);
        var userPathEntries = PathTools.SplitPath(userPathValue);
        var processJavaHome = appContext.GetEnvironmentVariable("JAVA_HOME");
        var userJavaHome = Environment.GetEnvironmentVariable("JAVA_HOME", EnvironmentVariableTarget.User);
        var defaultAlias = Environment.GetEnvironmentVariable(JwmvConstants.DefaultAliasVariable, EnvironmentVariableTarget.User);
        var defaultHome = Environment.GetEnvironmentVariable(JwmvConstants.DefaultHomeVariable, EnvironmentVariableTarget.User);
        var defaultBin = Environment.GetEnvironmentVariable(JwmvConstants.DefaultBinVariable, EnvironmentVariableTarget.User);
        var whereJava = await RunWhereAsync("java", cancellationToken);

        console.Write(new Rule("[yellow]jwmv doctor[/]").RuleStyle("grey"));

        var statusGrid = new Grid();
        statusGrid.AddColumn();
        statusGrid.AddColumn();
        statusGrid.AddRow("Shell integration", IsEnabled(appContext.GetEnvironmentVariable(JwmvConstants.ShellIntegrationVariable)));
        statusGrid.AddRow("Profile", Markup.Escape(profilePath));
        statusGrid.AddRow("Profile contains jwmv block", profileIntegrated ? "[green]Yes[/]" : "[red]No[/]");
        statusGrid.AddRow("Installed versions", installed.Count.ToString());
        statusGrid.AddRow("Resolved alias", current.IsResolved ? Markup.Escape(current.Alias!) : "[yellow]None[/]");
        statusGrid.AddRow("Resolved source", current.Source.ToString());
        statusGrid.AddRow("Process JAVA_HOME", EscapeOrPlaceholder(processJavaHome));
        statusGrid.AddRow("User JAVA_HOME", EscapeOrPlaceholder(userJavaHome));
        statusGrid.AddRow("User default alias", EscapeOrPlaceholder(defaultAlias));
        statusGrid.AddRow("User default bin", EscapeOrPlaceholder(defaultBin));
        console.Write(statusGrid);
        console.WriteLine();

        var pathTable = new Table().Border(TableBorder.Rounded);
        pathTable.AddColumn("Scope");
        pathTable.AddColumn("First entries");
        pathTable.AddRow(
            "Process PATH",
            EscapeOrPlaceholder(string.Join(Environment.NewLine, processPathEntries.Take(5))));
        pathTable.AddRow(
            "User PATH",
            EscapeOrPlaceholder(string.Join(Environment.NewLine, userPathEntries.Take(5))));
        console.Write(pathTable);
        console.WriteLine();

        var javaTable = new Table().Border(TableBorder.Rounded);
        javaTable.AddColumn("java.exe order");
        if (whereJava.Count == 0)
        {
            javaTable.AddRow("[yellow]`where.exe java` found no entries.[/]");
        }
        else
        {
            foreach (var item in whereJava)
            {
                javaTable.AddRow(Markup.Escape(item));
            }
        }

        console.Write(javaTable);

        var issues = BuildIssues(current, processJavaHome, defaultHome, defaultBin, processPathEntries, whereJava, profileIntegrated);
        if (issues.Count == 0)
        {
            console.WriteLine();
            console.MarkupLine("[green]Doctor did not detect any obvious PATH or JAVA_HOME conflicts.[/]");
            return 0;
        }

        console.WriteLine();
        console.MarkupLine("[yellow]Findings[/]");
        foreach (var issue in issues)
        {
            console.MarkupLine($"[yellow]-[/] {Markup.Escape(issue)}");
        }

        return 0;
    }

    private static List<string> BuildIssues(
        ActiveJavaSelection current,
        string? processJavaHome,
        string? defaultHome,
        string? defaultBin,
        IReadOnlyList<string> processPathEntries,
        IReadOnlyList<string> whereJava,
        bool profileIntegrated)
    {
        var issues = new List<string>();

        if (!profileIntegrated)
        {
            issues.Add("The active PowerShell profile does not contain the jwmv initialization block. Run `jwmv integrate` and reload PowerShell.");
        }

        if (current.IsResolved && !string.IsNullOrWhiteSpace(defaultHome) && !string.Equals(current.JavaHome, defaultHome, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"The resolved Java home ({current.JavaHome}) does not match the persisted default home ({defaultHome}).");
        }

        if (!string.IsNullOrWhiteSpace(defaultBin) && (processPathEntries.Count == 0 || !string.Equals(processPathEntries[0], defaultBin, StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add($"The current process PATH does not start with the persisted jwmv bin directory ({defaultBin}). Another Java may win in this session.");
        }

        if (current.IsResolved && !string.IsNullOrWhiteSpace(processJavaHome) && !string.Equals(processJavaHome, current.JavaHome, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"The current process JAVA_HOME points to {processJavaHome}, but jwmv resolved {current.JavaHome}.");
        }

        if (current.IsResolved && whereJava.Count > 0)
        {
            var expectedJava = Path.Combine(current.BinDirectory!, "java.exe");
            if (!string.Equals(whereJava[0], expectedJava, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"`java.exe` resolves first to {whereJava[0]}, not to the active jwmv Java at {expectedJava}.");
            }
        }

        return issues;
    }

    private static string EscapeOrPlaceholder(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "[grey]<not set>[/]"
            : Markup.Escape(value);

    private static string IsEnabled(string? value) =>
        string.Equals(value, "1", StringComparison.Ordinal)
            ? "[green]Enabled[/]"
            : "[yellow]Disabled[/]";

    private static async Task<IReadOnlyList<string>> RunWhereAsync(string command, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "where.exe",
                Arguments = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return [];
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
            {
                return [];
            }

            return output
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }
        catch
        {
            return [];
        }
    }
}
