using Jwmv.Core.Abstractions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Jwmv.Cli.Commands;

public sealed class EnvCommand(ISdkVersionManager manager, IAnsiConsole console) : AsyncCommand<EnvCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[action]")]
        public string? Action { get; init; }

        [CommandOption("--shell <SHELL>")]
        public string? Shell { get; init; }

        [CommandOption("--cwd <PATH>")]
        public string? WorkingDirectory { get; init; }

        [CommandOption("--init")]
        public bool Init { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (settings.Init || string.Equals(settings.Action, "init", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(settings.Shell))
            {
                var script = await manager.BuildShellInitScriptAsync(CommandHelpers.ParseShell(settings.Shell), cancellationToken);
                Console.WriteLine(script);
                return 0;
            }

            var current = await manager.ResolveAllCurrentAsync(settings.WorkingDirectory, cancellationToken);
            var resolved = current.Where(selection => selection.IsResolved).ToList();
            if (resolved.Count == 0)
            {
                CommandHelpers.WriteWarning(console, "No active SDKs are resolved to write into .jwmvrc.");
                return 0;
            }

            await File.WriteAllLinesAsync(
                Path.Combine(settings.WorkingDirectory ?? Environment.CurrentDirectory, ".jwmvrc"),
                resolved.Select(selection => $"{selection.CandidateName}={selection.Alias}"),
                cancellationToken);
            CommandHelpers.WriteSuccess(console, "Created .jwmvrc from the active SDK selections.");
            return 0;
        }

        if (string.Equals(settings.Action, "install", StringComparison.OrdinalIgnoreCase))
        {
            CommandHelpers.WriteHeader(console, "env install");
            var results = await CommandHelpers.RunInstallProgressAsync(
                console,
                "Installing missing .jwmvrc SDKs",
                progress => manager.InstallProjectMissingAsync(settings.WorkingDirectory, forceCatalogRefresh: false, progress, cancellationToken));
            if (results is null)
            {
                return -1;
            }

            if (results.Count == 0)
            {
                CommandHelpers.WriteSuccess(console, "All SDKs from .jwmvrc are already installed.");
                return 0;
            }

            foreach (var result in results)
            {
                CommandHelpers.WriteSuccess(console, $"Installed {result.InstalledVersion.CandidateName} {result.InstalledVersion.Alias}");
                if (!string.IsNullOrWhiteSpace(result.IntegrityWarning))
                {
                    CommandHelpers.WriteWarning(console, result.IntegrityWarning);
                }
            }

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
            CommandHelpers.WriteInfo(console, "No .jwmvrc file found in the current directory tree.");
            return 0;
        }

        foreach (var item in projectConfig.Versions)
        {
            console.MarkupLine($"Project {Markup.Escape(item.Key)}: [blue]{Markup.Escape(item.Value)}[/]");
        }

        console.MarkupLine($"Config file: [grey]{Markup.Escape(projectConfig.FilePath)}[/]");
        return 0;
    }
}
