using Jwmv.Core.Models;

namespace Jwmv.Cli.Commands;

internal static class CommandHelpers
{
    public static ShellKind ParseShell(string? shell) =>
        string.IsNullOrWhiteSpace(shell) || string.Equals(shell, "powershell", StringComparison.OrdinalIgnoreCase) || string.Equals(shell, "pwsh", StringComparison.OrdinalIgnoreCase)
            ? ShellKind.PowerShell
            : throw new ArgumentOutOfRangeException(nameof(shell), shell, "Only PowerShell is supported.");
}
