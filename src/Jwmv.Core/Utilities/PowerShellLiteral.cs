using System.Text;

namespace Jwmv.Core.Utilities;

/// <summary>
/// Builds single-quoted PowerShell string literals from untrusted values.
/// </summary>
/// <remarks>
/// Inside a single-quoted literal PowerShell only recognises quote characters, but it treats the typographic
/// quotes U+2018, U+2019, U+201A and U+201B exactly like the ASCII apostrophe (see about_Quoting_Rules).
/// Escaping only <c>'</c> therefore lets a value such as <c>v1’; Start-Process evil</c> terminate the literal
/// and inject code. Every one of those characters is doubled, which PowerShell reads back as the single
/// character.
/// </remarks>
public static class PowerShellLiteral
{
    public static string Escape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        StringBuilder? builder = null;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (!IsSingleQuote(character))
            {
                builder?.Append(character);
                continue;
            }

            builder ??= new StringBuilder(value.Length + 8).Append(value, 0, index);
            builder.Append(character).Append(character);
        }

        return builder?.ToString() ?? value;
    }

    public static string SingleQuote(string value) => "'" + Escape(value) + "'";

    private static bool IsSingleQuote(char character) =>
        character is '\'' or '‘' or '’' or '‚' or '‛';
}
