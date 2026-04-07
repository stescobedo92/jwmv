using Jwmv.Core.Abstractions;
using Jwmv.Core.Exceptions;
using Jwmv.Core.Models;
using Jwmv.Core.Utilities;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Jwmv.Cli.Commands;

public sealed class UpgradeCommand(IJavaVersionManager manager, IAnsiConsole console) : AsyncCommand<UpgradeCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[identifier]")]
        public string? Identifier { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var installedVersions = await manager.ListInstalledAsync(cancellationToken);
        var targets = string.IsNullOrWhiteSpace(settings.Identifier)
            ? installedVersions
            : installedVersions.Where(item => JavaIdentifier.Matches(item.Alias, settings.Identifier)).ToList();

        if (targets.Count == 0)
        {
            throw new JavaNotInstalledException(settings.Identifier ?? "installed");
        }

        var config = await manager.GetConfigAsync(cancellationToken);
        var processedTracks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var installed in targets.OrderByDescending(item => item.JavaVersion))
        {
            var track = $"{GetMajorVersion(installed.JavaVersion)}-{installed.DistributionAlias}";
            if (!processedTracks.Add(track))
            {
                continue;
            }

            var result = await manager.InstallAsync(new InstallJavaRequest
            {
                Identifier = track,
                SetAsDefault = string.Equals(config.DefaultJavaAlias, installed.Alias, StringComparison.OrdinalIgnoreCase)
            }, progress: null, cancellationToken);

            if (result.AlreadyInstalled)
            {
                console.MarkupLine($"[grey]{Markup.Escape(track)} already points to the latest installed package ({Markup.Escape(result.InstalledVersion.Alias)}).[/]");
            }
            else
            {
                console.MarkupLine($"[green]Upgraded[/] {Markup.Escape(track)} -> {Markup.Escape(result.InstalledVersion.Alias)}");
            }
        }

        return 0;
    }

    private static string GetMajorVersion(string javaVersion)
    {
        var digits = new string(javaVersion.TakeWhile(character => char.IsDigit(character)).ToArray());
        return string.IsNullOrWhiteSpace(digits) ? javaVersion : digits;
    }
}
