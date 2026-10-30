using Jwmv.Core.Abstractions;
using Jwmv.Core.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Jwmv.Cli.Commands;

public sealed class InstallCommand(ISdkVersionManager manager, IAnsiConsole console) : AsyncCommand<InstallCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[candidate-or-version]")]
        public string? CandidateOrVersion { get; init; }

        [CommandArgument(1, "[version]")]
        public string? Version { get; init; }

        [CommandOption("-d|--default")]
        public bool SetDefault { get; init; }

        [CommandOption("-r|--refresh")]
        public bool ForceRefresh { get; init; }

        [CommandOption("--allow-unverified")]
        [System.ComponentModel.Description("Install even if the catalog does not publish a checksum for the package (not recommended).")]
        public bool AllowUnverified { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var (candidateName, version) = CommandHelpers.ResolveCandidateAndVersion(settings.CandidateOrVersion, settings.Version);
        var setDefault = settings.SetDefault;
        CommandHelpers.WriteHeader(console, $"install {candidateName}");

        if (string.IsNullOrWhiteSpace(version))
        {
            var filter = settings.CandidateOrVersion is null || CommandHelpers.IsKnownCandidate(settings.CandidateOrVersion)
                ? console.Prompt(new TextPrompt<string>($"Version filter for {candidateName}?").DefaultValue(string.Equals(candidateName, "java", StringComparison.OrdinalIgnoreCase) ? "21" : string.Empty).AllowEmpty())
                : settings.CandidateOrVersion;
            var sdkPackages = await manager.ListAvailableAsync(new SdkCatalogQuery
            {
                CandidateName = candidateName,
                VersionFilter = filter,
                ForceRefresh = settings.ForceRefresh
            }, cancellationToken);

            if (sdkPackages.Count == 0)
            {
                CommandHelpers.WriteFailure(console, $"No packages found for {filter}.");
                return -1;
            }

            version = console.Prompt(
                new SelectionPrompt<string>()
                    .Title($"Select a {candidateName} package to install")
                    .PageSize(12)
                    .UseConverter(alias => alias)
                    .AddChoices(sdkPackages.Select(package => package.Alias)));

            setDefault = console.Confirm($"Set it as the default {candidateName} for new sessions?");
        }

        var result = await CommandHelpers.RunInstallProgressAsync(
            console,
            $"Installing {candidateName} {version}",
            progress => manager.InstallAsync(new InstallSdkRequest
            {
                CandidateName = candidateName,
                Version = version!,
                SetAsDefault = setDefault,
                ForceCatalogRefresh = settings.ForceRefresh,
                AllowUnverified = settings.AllowUnverified
            }, progress, cancellationToken));

        if (result is null)
        {
            return -1;
        }

        if (result.AlreadyInstalled)
        {
            CommandHelpers.WriteWarning(console, $"{result.InstalledVersion.CandidateName} {result.InstalledVersion.Alias} is already installed.");
        }
        else
        {
            CommandHelpers.WriteSuccess(console, $"Installed {result.InstalledVersion.CandidateName} {result.InstalledVersion.Alias}");
            CommandHelpers.WriteInfo(console, $"Home: {result.InstalledVersion.HomeDirectory}");
        }

        if (!string.IsNullOrWhiteSpace(result.IntegrityWarning))
        {
            CommandHelpers.WriteWarning(console, result.IntegrityWarning);
        }

        if (result.DefaultWasUpdated)
        {
            CommandHelpers.WriteSuccess(console, $"Default {result.InstalledVersion.CandidateName} updated for new Windows sessions.");
        }
        else
        {
            CommandHelpers.WriteInfo(console, $"Run jwmv use {result.InstalledVersion.CandidateName} {result.InstalledVersion.Alias} for this session, or jwmv default {result.InstalledVersion.CandidateName} {result.InstalledVersion.Alias} to persist it.");
        }

        return 0;
    }
}
