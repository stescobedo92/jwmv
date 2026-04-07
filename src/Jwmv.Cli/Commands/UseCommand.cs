using Jwmv.Core.Abstractions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Jwmv.Cli.Commands;

public sealed class UseCommand(IJavaVersionManager manager, IAppContext appContext, IAnsiConsole console) : AsyncCommand<UseCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<identifier>")]
        public string Identifier { get; init; } = string.Empty;

        [CommandOption("--shell <SHELL>")]
        public string? Shell { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Shell))
        {
            var exe = appContext.ExecutablePath;
            console.MarkupLine("[yellow]`use` cannot mutate the current shell by itself when you run the executable directly.[/]");
            console.WriteLine("Use one of these PowerShell commands:");
            console.WriteLine($"Invoke-Expression ((& '{exe}' use '{settings.Identifier}' --shell powershell) -join [Environment]::NewLine)");
            console.WriteLine($"Or install shell integration once: & '{exe}' integrate");
            return 0;
        }

        var shell = CommandHelpers.ParseShell(settings.Shell);
        var script = await manager.BuildUseShellScriptAsync(settings.Identifier, shell, cancellationToken);
        Console.WriteLine(script);
        return 0;
    }
}
