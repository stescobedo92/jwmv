using Jwmv.Core.Abstractions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Jwmv.Cli.Commands;

public sealed class IntegrateCommand(IShellProfileIntegrationService integrationService, IAnsiConsole console) : AsyncCommand<IntegrateCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--shell <SHELL>")]
        public string? Shell { get; init; }

        [CommandOption("--profile <PATH>")]
        public string? ProfilePath { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var shell = CommandHelpers.ParseShell(settings.Shell);
        var targetProfile = await integrationService.EnsureIntegratedAsync(shell, settings.ProfilePath, cancellationToken);

        console.MarkupLine("[green]PowerShell integration installed.[/]");
        console.MarkupLine($"Profile: [grey]{Markup.Escape(targetProfile)}[/]");
        console.MarkupLine("[grey]Next step:[/] open a new PowerShell window, or run `. $PROFILE` in the current one.");
        console.MarkupLine("[grey]Then use:[/] [blue]jwmv use <version>[/]");
        return 0;
    }
}
