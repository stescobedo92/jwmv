using Jwmv.Core.Abstractions;
using Spectre.Console.Cli;

namespace Jwmv.Cli.Commands;

public sealed class HomeCommand(IJavaVersionManager manager) : AsyncCommand<HomeCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[identifier]")]
        public string? Identifier { get; init; }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var home = await manager.GetJavaHomeAsync(settings.Identifier, cancellationToken);
        if (!string.IsNullOrWhiteSpace(home))
        {
            Console.WriteLine(home);
        }

        return 0;
    }
}
