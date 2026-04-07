using Jwmv.Core.Abstractions;
using Jwmv.Core.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Jwmv.Cli.Commands;

public sealed class InstallCommand(IJavaVersionManager manager, IAnsiConsole console) : AsyncCommand<InstallCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[identifier]")]
        public string? Identifier { get; init; }

        [CommandOption("-d|--default")]
        public bool SetDefault { get; init; }

        [CommandOption("-r|--refresh")]
        public bool ForceRefresh { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var identifier = settings.Identifier;
        var setDefault = settings.SetDefault;

        if (string.IsNullOrWhiteSpace(identifier))
        {
            var filter = console.Prompt(
                new TextPrompt<string>("Version or vendor filter?")
                    .DefaultValue("21"));
            var packages = await manager.ListAvailableAsync(new JavaCatalogQuery
            {
                IdentifierFilter = filter,
                ForceRefresh = settings.ForceRefresh
            }, cancellationToken);

            if (packages.Count == 0)
            {
                console.MarkupLine($"[red]No packages found for[/] [blue]{Markup.Escape(filter)}[/].");
                return -1;
            }

            identifier = console.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select a Java package to install")
                    .PageSize(12)
                    .UseConverter(alias => alias)
                    .AddChoices(packages.Select(package => package.Alias)));

            setDefault = console.Confirm("Set it as the default JAVA_HOME for new sessions?");
        }

        InstallJavaResult? result = null;
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
                var task = progressContext.AddTask($"[green]Installing {Markup.Escape(identifier!)}[/]", maxValue: 100);
                var progress = new Progress<InstallProgressUpdate>(update =>
                {
                    task.Description($"[green]{Markup.Escape(update.Status)}[/]");
                    task.Value(update.Percentage);
                });

                result = await manager.InstallAsync(new InstallJavaRequest
                {
                    Identifier = identifier!,
                    SetAsDefault = setDefault,
                    ForceCatalogRefresh = settings.ForceRefresh
                }, progress, cancellationToken);
            });

        if (result is null)
        {
            return -1;
        }

        if (result.AlreadyInstalled)
        {
            console.MarkupLine($"[yellow]{Markup.Escape(result.InstalledVersion.DisplayAlias)}[/] is already installed.");
        }
        else
        {
            console.MarkupLine($"[green]Installed[/] {Markup.Escape(result.InstalledVersion.DisplayAlias)} to [grey]{Markup.Escape(result.InstalledVersion.JavaHome)}[/].");
        }

        if (result.DefaultWasUpdated)
        {
            console.MarkupLine("[green]Default JAVA_HOME updated and will take precedence over existing system Java paths in new Windows sessions.[/]");
        }
        else
        {
            console.MarkupLine($"[grey]Tip:[/] run [blue]jwmv use {Markup.Escape(result.InstalledVersion.DisplayAlias)}[/] from the integrated PowerShell function, or [blue]jwmv default {Markup.Escape(result.InstalledVersion.DisplayAlias)}[/] to persist it.");
        }

        return 0;
    }
}
