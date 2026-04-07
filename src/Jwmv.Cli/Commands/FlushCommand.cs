using Jwmv.Core.Abstractions;
using Jwmv.Core.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Jwmv.Cli.Commands;

public sealed class FlushCommand(IJavaVersionManager manager, IAnsiConsole console) : AsyncCommand<FlushCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--archives")]
        public bool IncludeArchives { get; init; }

        [CommandOption("--temp")]
        public bool IncludeTemp { get; init; }

        [CommandOption("--catalog")]
        public bool IncludeCatalog { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var request = new FlushRequest
        {
            IncludeArchives = settings.IncludeArchives,
            IncludeTemp = settings.IncludeTemp,
            IncludeCatalog = settings.IncludeCatalog
        };

        await manager.FlushAsync(request, cancellationToken);
        console.MarkupLine("[green]Requested cache directories were flushed.[/]");
        return 0;
    }
}
