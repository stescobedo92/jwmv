using Jwmv.Core.Abstractions;
using Jwmv.Core.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Jwmv.Cli.Commands;

public sealed class SelfUpdateCommand(ISelfUpdateService selfUpdateService, IAnsiConsole console) : AsyncCommand<SelfUpdateCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-r|--repository <OWNER/REPO>")]
        public string? Repository { get; init; }

        [CommandOption("-t|--tag <TAG>")]
        public string? Tag { get; init; }

        [CommandOption("-c|--check")]
        public bool CheckOnly { get; init; }

        [CommandOption("-f|--force")]
        public bool Force { get; init; }

        [CommandOption("--restart")]
        public bool RestartAfterUpdate { get; init; }

        [CommandOption("-y|--yes")]
        public bool Yes { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var checkResult = await selfUpdateService.CheckForUpdateAsync(new SelfUpdateRequest
        {
            Repository = settings.Repository,
            Tag = settings.Tag,
            Force = settings.Force
        }, cancellationToken);

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Field");
        table.AddColumn("Value");
        table.AddRow("Repository", checkResult.Repository);
        table.AddRow("Current version", checkResult.CurrentVersion);
        table.AddRow("Target version", checkResult.TargetVersion);
        table.AddRow("Release tag", checkResult.ReleaseTag);
        table.AddRow("Asset", checkResult.AssetName);
        table.AddRow("Release page", Markup.Escape(checkResult.ReleasePageUri.ToString()));
        table.AddRow("Update available", checkResult.IsUpdateAvailable ? "[green]Yes[/]" : "[yellow]No[/]");
        console.Write(table);

        if (settings.CheckOnly)
        {
            return 0;
        }

        if (!checkResult.IsUpdateAvailable && !settings.Force)
        {
            console.MarkupLine("[green]jwmv is already up to date.[/]");
            return 0;
        }

        if (!settings.Yes)
        {
            var confirmed = console.Confirm($"Update jwmv from {checkResult.CurrentVersion} to {checkResult.TargetVersion}?");
            if (!confirmed)
            {
                console.MarkupLine("[yellow]Self-update cancelled.[/]");
                return 0;
            }
        }

        SelfUpdateResult? result = null;
        await console.Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(
            [
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn()
            ])
            .StartAsync(async progressContext =>
            {
                var task = progressContext.AddTask("[green]Updating jwmv[/]", maxValue: 100);
                var progress = new Progress<SelfUpdateProgressUpdate>(update =>
                {
                    task.Description($"[green]{Markup.Escape(update.Status)}[/]");
                    task.Value(update.Percentage);
                });

                result = await selfUpdateService.ApplyUpdateAsync(checkResult, settings.RestartAfterUpdate, progress, cancellationToken);
            });

        if (result is null)
        {
            return -1;
        }

        console.MarkupLine($"[green]jwmv {Markup.Escape(result.TargetVersion)} has been staged.[/]");
        console.MarkupLine($"[grey]Executable:[/] [blue]{Markup.Escape(result.ExecutablePath)}[/]");
        console.MarkupLine("[grey]The updater will replace the binary as soon as the current process fully exits.[/]");
        if (result.RestartScheduled)
        {
            console.MarkupLine("[grey]A new jwmv process will be started automatically after the replacement finishes.[/]");
        }
        else
        {
            console.MarkupLine("[grey]Next step:[/] open a new shell after this command returns and run [blue]jwmv --version[/].");
        }

        return 0;
    }
}
