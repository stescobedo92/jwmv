using System.Globalization;
using System.Text.RegularExpressions;

namespace Jwmv.Core.Utilities;

public static partial class SdkIdentifier
{
    public static string NormalizeCandidateName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim().ToLowerInvariant();
    }

    public static bool Matches(string alias, string identifier)
    {
        if (string.Equals(alias, identifier, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalizedAlias = alias.Trim().ToLowerInvariant();
        var normalizedIdentifier = identifier.Trim().ToLowerInvariant();
        return normalizedAlias.StartsWith(normalizedIdentifier, StringComparison.Ordinal);
    }

    public static bool IsStableVersion(string version) =>
        !PreReleasePattern().IsMatch(version);

    /// <summary>
    /// A candidate name is safe when it can be used verbatim as a directory name and as part of an
    /// environment variable name: letters, digits and dashes only, never a path separator or dot segment.
    /// </summary>
    public static bool IsSafeCandidateName(string? value) =>
        IsSafeToken(value, maxLength: 32, char.IsAsciiLetterOrDigit, static c => char.IsAsciiLetterOrDigit(c) || c == '-');

    /// <summary>
    /// A version alias is safe when it can be used verbatim as a single file or directory name:
    /// it starts with a letter or digit and only contains letters, digits, dots, dashes, underscores and plus signs.
    /// This rejects <c>..</c>, absolute paths, UNC paths, quotes and whitespace.
    /// </summary>
    public static bool IsSafeAlias(string? value) =>
        IsSafeToken(value, maxLength: 128, char.IsAsciiLetterOrDigit, IsAliasCharacter);

    /// <summary>
    /// A file name is safe when it is a single path segment without separators, drive letters or trailing dots.
    /// </summary>
    public static bool IsSafeFileName(string? value) =>
        IsSafeToken(value, maxLength: 255, char.IsAsciiLetterOrDigit, IsAliasCharacter) && !value!.EndsWith('.');

    /// <summary>
    /// Environment variable names are interpolated into generated PowerShell as identifiers, so they must be plain identifiers.
    /// </summary>
    public static bool IsSafeEnvironmentVariableName(string? value) =>
        IsSafeToken(value, maxLength: 255, static c => char.IsAsciiLetter(c) || c == '_', static c => char.IsAsciiLetterOrDigit(c) || c == '_');

    private static bool IsAliasCharacter(char c) =>
        char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or '+';

    private static bool IsSafeToken(string? value, int maxLength, Func<char, bool> isValidFirstCharacter, Func<char, bool> isValidCharacter)
    {
        if (string.IsNullOrEmpty(value) || value.Length > maxLength || !isValidFirstCharacter(value[0]))
        {
            return false;
        }

        for (var index = 1; index < value.Length; index++)
        {
            if (!isValidCharacter(value[index]))
            {
                return false;
            }
        }

        return true;
    }

    public static int CompareVersionsDescending(string? left, string? right)
    {
        var leftTokens = Tokenize(left);
        var rightTokens = Tokenize(right);
        var max = Math.Max(leftTokens.Count, rightTokens.Count);
        for (var index = 0; index < max; index++)
        {
            if (index >= leftTokens.Count)
            {
                return 1;
            }

            if (index >= rightTokens.Count)
            {
                return -1;
            }

            var comparison = CompareToken(leftTokens[index], rightTokens[index]);
            if (comparison != 0)
            {
                return -comparison;
            }
        }

        return string.Compare(right, left, StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> Tokenize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return Tokenizer().Matches(value)
            .Select(match => match.Value)
            .ToList();
    }

    private static int CompareToken(string left, string right)
    {
        if (long.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber) &&
            long.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber))
        {
            return leftNumber.CompareTo(rightNumber);
        }

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"[0-9]+|[a-zA-Z]+", RegexOptions.NonBacktracking, matchTimeoutMilliseconds: 100)]
    private static partial Regex Tokenizer();

    [GeneratedRegex(@"(?:alpha|beta|rc|milestone|snapshot|nightly|preview|ea)", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking, matchTimeoutMilliseconds: 100)]
    private static partial Regex PreReleasePattern();




}
