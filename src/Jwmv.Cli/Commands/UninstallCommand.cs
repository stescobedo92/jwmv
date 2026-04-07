using Jwmv.Core.Abstractions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Jwmv.Cli.Commands;

public sealed class UninstallCommand(IJavaVersionManager manager, IAnsiConsole console) : AsyncCommand<UninstallCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[identifier]")]
        public string? Identifier { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var identifier = settings.Identifier;
        if (string.IsNullOrWhiteSpace(identifier))
        {
            var installed = await manager.ListInstalledAsync(cancellationToken);
            if (installed.Count == 0)
            {
                console.MarkupLine("[yellow]No Java versions are installed locally.[/]");
                return 0;
            }

            identifier = console.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select a Java version to uninstall")
                    .PageSize(10)
                    .AddChoices(installed.OrderBy(item => item.DisplayAlias).Select(item => item.DisplayAlias)));
        }

        await manager.UninstallAsync(identifier, cancellationToken);
        console.MarkupLine($"[green]Removed[/] {Markup.Escape(identifier)} and its local metadata/cache.");
        return 0;
    }
}
