using Jwmv.Core.Abstractions;
using Jwmv.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Jwmv.Cli.Commands;

public sealed class ConfigCommand(IJavaVersionManager manager, JwmvPaths paths, IAnsiConsole console) : AsyncCommand<ConfigCommand.Settings>
{
    public sealed class Settings : CommandSettings;

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var config = await manager.GetConfigAsync(cancellationToken);

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Setting");
        table.AddColumn("Value");
        table.AddRow("Root", paths.RootDirectory);
        table.AddRow("Config file", paths.ConfigFilePath);
        table.AddRow("Preferred distribution", config.PreferredDistributionAlias);
        table.AddRow("Catalog refresh (hours)", config.CatalogRefreshHours.ToString());
        table.AddRow("Auto env", config.AutoEnvEnabled ? "true" : "false");
        table.AddRow("Default shell", config.DefaultShell);
        table.AddRow("Default Java alias", config.DefaultJavaAlias ?? "-");
        table.AddRow("Self-update repository", config.SelfUpdateRepository ?? "-");

        console.Write(table);
        return 0;
    }
}
