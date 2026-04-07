using Jwmv.Core.Abstractions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Jwmv.Cli.Commands;

public sealed class DefaultCommand(IJavaVersionManager manager, IAnsiConsole console) : AsyncCommand<DefaultCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<identifier>")]
        public string Identifier { get; init; } = string.Empty;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var installed = await manager.SetDefaultAsync(settings.Identifier, cancellationToken);
        console.MarkupLine($"[green]Default Java set to[/] {Markup.Escape(installed.Alias)}");
        console.MarkupLine($"[grey]{Markup.Escape(installed.JavaHome)}[/]");
        return 0;
    }
}
