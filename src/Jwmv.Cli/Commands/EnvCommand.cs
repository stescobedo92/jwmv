using Jwmv.Core.Abstractions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Jwmv.Cli.Commands;

public sealed class EnvCommand(IJavaVersionManager manager, IAnsiConsole console) : AsyncCommand<EnvCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--shell <SHELL>")]
        public string? Shell { get; init; }

        [CommandOption("--cwd <PATH>")]
        public string? WorkingDirectory { get; init; }

        [CommandOption("--init")]
        public bool Init { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (settings.Init)
        {
            var script = await manager.BuildShellInitScriptAsync(CommandHelpers.ParseShell(settings.Shell), cancellationToken);
            Console.WriteLine(script);
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(settings.Shell))
        {
            var script = await manager.BuildEnvShellScriptAsync(settings.WorkingDirectory, CommandHelpers.ParseShell(settings.Shell), cancellationToken);
            Console.WriteLine(script);
            return 0;
        }

        var projectConfig = await manager.FindProjectConfigurationAsync(settings.WorkingDirectory, cancellationToken);
        if (projectConfig is null)
        {
            console.MarkupLine("[grey]No .jwmvrc file found in the current directory tree.[/]");
            return 0;
        }

        console.MarkupLine($"Project Java: [blue]{Markup.Escape(projectConfig.JavaIdentifier)}[/]");
        console.MarkupLine($"Config file: [grey]{Markup.Escape(projectConfig.FilePath)}[/]");
        return 0;
    }
}
