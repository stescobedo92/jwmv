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

        var table = CommandHelpers.CreateTable();
        table.AddColumn(CommandHelpers.Header("Field"));
        table.AddColumn(CommandHelpers.Header("Value"));
        table.AddRow("Repository", CommandHelpers.Text(checkResult.Repository));
        table.AddRow("Current version", CommandHelpers.Version(checkResult.CurrentVersion));
        table.AddRow("Target version", CommandHelpers.Version(checkResult.TargetVersion));
        table.AddRow("Release tag", CommandHelpers.Alias(checkResult.ReleaseTag));
        table.AddRow("Asset", CommandHelpers.Text(checkResult.AssetName));
        table.AddRow("Release page", CommandHelpers.Url(checkResult.ReleasePageUri));
        table.AddRow("Update available", checkResult.IsUpdateAvailable ? $"{CommandHelpers.CheckBox("green", "x")} [green]Yes[/]" : $"{CommandHelpers.CheckBox("yellow", "!")} [yellow]No[/]");
        console.Write(table);

        if (settings.CheckOnly)
        {
            return 0;
        }

        if (!checkResult.IsUpdateAvailable && !settings.Force)
        {
            CommandHelpers.WriteSuccess(console, "jwmv is already up to date.");
            return 0;
        }

        if (!settings.Yes)
        {
            var confirmed = console.Confirm($"Update jwmv from {checkResult.CurrentVersion} to {checkResult.TargetVersion}?");
            if (!confirmed)
            {
                CommandHelpers.WriteWarning(console, "Self-update cancelled.");
                return 0;
            }
        }

        var result = await CommandHelpers.RunSelfUpdateProgressAsync(
            console,
            "Updating jwmv",
            progress => selfUpdateService.ApplyUpdateAsync(checkResult, settings.RestartAfterUpdate, progress, cancellationToken));

        if (result is null)
        {
            return -1;
        }

        CommandHelpers.WriteSuccess(console, $"jwmv {result.TargetVersion} downloaded, verified and staged.");
        CommandHelpers.WriteInfo(console, $"Signed by: {result.SignerIdentity}");
        CommandHelpers.WriteInfo(console, $"Executable: {result.ExecutablePath}");
        CommandHelpers.WriteInfo(console, $"Updater log: {result.UpdaterLogPath}");
        CommandHelpers.WriteInfo(console, "The updater replaces the binary as soon as this process exits and verifies the new version; check the log if jwmv --version does not change.");
        if (result.RestartScheduled)
        {
            CommandHelpers.WriteInfo(console, "A new jwmv process will be started automatically after the replacement finishes.");
        }
        else
        {
            CommandHelpers.WriteInfo(console, "Next step: open a new shell after this command returns and run jwmv --version.");
        }

        return 0;
    }
}
