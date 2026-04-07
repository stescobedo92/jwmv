using Jwmv.Core;
using Jwmv.Core.Abstractions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Jwmv.Cli.Commands;

public sealed class CurrentCommand(IJavaVersionManager manager, IAppContext appContext, IAnsiConsole console) : AsyncCommand<CurrentCommand.Settings>
{
    public sealed class Settings : CommandSettings;

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var current = await manager.ResolveCurrentAsync(null, cancellationToken);
        if (!current.IsResolved)
        {
            console.MarkupLine("[yellow]No active Java version is currently resolved.[/]");
            if (!string.Equals(appContext.GetEnvironmentVariable(JwmvConstants.ShellIntegrationVariable), "1", StringComparison.Ordinal))
            {
                console.MarkupLine("[grey]Hint:[/] `jwmv use` only affects the current PowerShell session when its output is evaluated.");
                console.WriteLine($"Run once: & '{appContext.ExecutablePath}' integrate");
                console.WriteLine("Then reload PowerShell or run `. $PROFILE`.");
            }
            else
            {
                console.MarkupLine("[grey]Shell integration is loaded.[/] Activate one with `jwmv use <version>` or persist one with `jwmv default <version>`.");
            }

            if (!string.IsNullOrWhiteSpace(current.ProjectFilePath))
            {
                console.MarkupLine($"Project file found at [grey]{Markup.Escape(current.ProjectFilePath)}[/], but its Java version is not installed.");
            }

            return 0;
        }

        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();
        var displayAlias = await manager.ListInstalledAsync(cancellationToken);
        var installed = displayAlias.FirstOrDefault(item => string.Equals(item.Alias, current.Alias, StringComparison.OrdinalIgnoreCase));
        grid.AddRow("Alias", Markup.Escape(installed?.DisplayAlias ?? current.Alias!));
        grid.AddRow("Source", current.Source.ToString());
        grid.AddRow("JAVA_HOME", Markup.Escape(current.JavaHome!));
        if (!string.IsNullOrWhiteSpace(current.ProjectFilePath))
        {
            grid.AddRow("Project file", Markup.Escape(current.ProjectFilePath));
        }

        console.Write(grid);
        return 0;
    }
}
