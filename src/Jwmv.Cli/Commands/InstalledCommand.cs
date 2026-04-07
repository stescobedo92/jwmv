using Jwmv.Core.Abstractions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Jwmv.Cli.Commands;

public sealed class InstalledCommand(IJavaVersionManager manager, IAnsiConsole console) : AsyncCommand<InstalledCommand.Settings>
{
    public sealed class Settings : CommandSettings;

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var installed = await manager.ListInstalledAsync(cancellationToken);
        var current = await manager.ResolveCurrentAsync(null, cancellationToken);

        if (installed.Count == 0)
        {
            console.MarkupLine("[yellow]No Java versions are installed locally.[/]");
            console.MarkupLine("[grey]Tip:[/] use [blue]jwmv install[/] to install one interactively.");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Alias");
        table.AddColumn("Java");
        table.AddColumn("Vendor");
        table.AddColumn("Installed");
        table.AddColumn("Status");

        foreach (var item in installed.OrderByDescending(item => item.JavaVersion).ThenBy(item => item.DistributionAlias))
        {
            var status = string.Equals(current.Alias, item.Alias, StringComparison.OrdinalIgnoreCase)
                ? $"[green]{current.Source}[/]"
                : "-";

            table.AddRow(
                item.DisplayAlias,
                item.JavaVersion,
                item.DistributionAlias,
                item.InstalledAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                status);
        }

        console.Write(table);
        console.MarkupLine($"[grey]{installed.Count} installed Java version(s).[/]");
        return 0;
    }
}
