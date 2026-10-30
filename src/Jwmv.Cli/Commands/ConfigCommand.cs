using System.Globalization;
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

        var table = CommandHelpers.CreateTable();
        table.AddColumn(CommandHelpers.Header("Setting"));
        table.AddColumn(CommandHelpers.Header("Value"));
        table.AddRow("Root", CommandHelpers.Muted(paths.RootDirectory));
        table.AddRow("Config file", CommandHelpers.Muted(paths.ConfigFilePath));
        table.AddRow("SDK catalog cache", CommandHelpers.Muted(paths.SdkCatalogCacheFilePath));
        table.AddRow("Preferred distribution", CommandHelpers.Alias(config.PreferredDistributionAlias));
        table.AddRow("Catalog refresh (hours)", config.CatalogRefreshHours.ToString(CultureInfo.InvariantCulture));
        table.AddRow("Auto env", config.AutoEnvEnabled ? $"{CommandHelpers.CheckBox("green", "x")} [green]true[/]" : $"{CommandHelpers.CheckBox("grey", " ")} [grey]false[/]");
        table.AddRow("Default shell", CommandHelpers.Alias(config.DefaultShell));
        table.AddRow("Default Java alias", CommandHelpers.Alias(config.DefaultJavaAlias));
        table.AddRow("Default SDKs", config.DefaultVersions.Count == 0 ? "[grey]-[/]" : Markup.Escape(string.Join(", ", config.DefaultVersions.Select(item => $"{item.Key}={item.Value}"))));
        table.AddRow("Self-update repository", CommandHelpers.Alias(config.SelfUpdateRepository));

        console.Write(table);
        CommandHelpers.WriteSuccess(console, "Configuration loaded.");
        return 0;
    }
}
