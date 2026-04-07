using Jwmv.Core.Abstractions;
using Jwmv.Core.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Jwmv.Cli.Commands;

public sealed class ListCommand(IJavaVersionManager manager, IAnsiConsole console) : AsyncCommand<ListCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[filter]")]
        public string? Filter { get; init; }

        [CommandOption("-r|--refresh")]
        public bool Refresh { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var available = await manager.ListAvailableAsync(new JavaCatalogQuery
        {
            IdentifierFilter = settings.Filter,
            ForceRefresh = settings.Refresh
        }, cancellationToken);

        var installed = await manager.ListInstalledAsync(cancellationToken);
        var current = await manager.ResolveCurrentAsync(null, cancellationToken);
        var installedAliases = installed.ToDictionary(item => item.Alias, StringComparer.OrdinalIgnoreCase);

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Alias");
        table.AddColumn("Java");
        table.AddColumn("Vendor");
        table.AddColumn("Support");
        table.AddColumn("Status");

        foreach (var package in available)
        {
            var statusParts = new List<string>();
            if (installedAliases.ContainsKey(package.Alias))
            {
                statusParts.Add("[green]installed[/]");
            }

            if (string.Equals(current.Alias, package.Alias, StringComparison.OrdinalIgnoreCase))
            {
                statusParts.Add($"[yellow]{current.Source}[/]");
            }

            table.AddRow(
                installedAliases.TryGetValue(package.Alias, out var installedVersion) ? installedVersion.DisplayAlias : package.Alias,
                package.JavaVersion,
                package.DistributionAlias,
                package.TermOfSupport,
                statusParts.Count == 0 ? "-" : string.Join(", ", statusParts));
        }

        console.Write(table);
        console.MarkupLine($"[grey]{available.Count} package(s) shown, {installed.Count} installed locally.[/]");
        return 0;
    }
}
