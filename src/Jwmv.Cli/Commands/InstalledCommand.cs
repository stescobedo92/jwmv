using System.Globalization;
using Jwmv.Core.Abstractions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Jwmv.Cli.Commands;

public sealed class InstalledCommand(ISdkVersionManager manager, IAnsiConsole console) : AsyncCommand<InstalledCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[candidate]")]
        public string? Candidate { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var installed = await manager.ListInstalledAsync(settings.Candidate, cancellationToken);
        var currents = await manager.ResolveAllCurrentAsync(null, cancellationToken);

        if (installed.Count == 0)
        {
            CommandHelpers.WriteWarning(console, "No SDK versions are installed locally.");
            CommandHelpers.WriteInfo(console, "Tip: use jwmv install to install one interactively.");
            return 0;
        }

        var medium = CommandHelpers.IsMediumOrWider(console);
        var table = CommandHelpers.CreateTable();
        table.AddColumn(CommandHelpers.Header("Candidate"));
        table.AddColumn(CommandHelpers.Header("Version"));
        table.AddColumn(CommandHelpers.Header("Alias"));
        if (medium)
        {
            table.AddColumn(CommandHelpers.Header("Installed"));
        }

        table.AddColumn(CommandHelpers.Header("Status"));

        foreach (var item in installed.OrderBy(item => item.CandidateName, StringComparer.OrdinalIgnoreCase).ThenByDescending(item => item.Version, StringComparer.OrdinalIgnoreCase))
        {
            var current = currents.FirstOrDefault(selection => string.Equals(selection.CandidateName, item.CandidateName, StringComparison.OrdinalIgnoreCase));
            var status = string.Equals(current?.Alias, item.Alias, StringComparison.OrdinalIgnoreCase)
                ? $"[green]{current?.Source.ToString()}[/]"
                : "[grey]-[/]";

            var row = new List<string>
            {
                CommandHelpers.Candidate(item.CandidateName),
                CommandHelpers.Version(item.Version),
                CommandHelpers.Alias(item.Alias)
            };

            if (medium)
            {
                row.Add(CommandHelpers.Muted(item.InstalledAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)));
            }

            row.Add(status);
            table.AddRow(row.ToArray());
        }

        console.Write(table);
        CommandHelpers.WriteSuccess(console, $"{installed.Count} installed SDK version(s).");
        return 0;
    }
}
