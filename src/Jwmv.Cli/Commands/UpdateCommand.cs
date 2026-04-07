using Jwmv.Core.Abstractions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Jwmv.Cli.Commands;

public sealed class UpdateCommand(IJavaVersionManager manager, IAnsiConsole console) : AsyncCommand<UpdateCommand.Settings>
{
    public sealed class Settings : CommandSettings;

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        await manager.RefreshCatalogAsync(cancellationToken);
        console.MarkupLine("[green]Catalog cache refreshed from Foojay.[/]");
        return 0;
    }
}
