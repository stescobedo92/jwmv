using System.Text;
using System.Text.RegularExpressions;

namespace Jwmv.Core.Utilities;

public static partial class JavaIdentifier
{
    public static string NormalizeVersion(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        return version.Trim().ToLowerInvariant().Replace('+', '.');
    }

    public static string BuildAlias(string javaVersion, string distribution)
    {
        var alias = DistributionAlias.ToAlias(distribution);
        return $"{NormalizeVersion(javaVersion)}-{alias}";
    }

    public static bool Matches(string alias, string identifier)
    {
        if (string.Equals(alias, identifier, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalizedIdentifier = identifier.Trim().ToLowerInvariant();
        var normalizedAlias = alias.Trim().ToLowerInvariant();
        if (normalizedAlias.StartsWith(normalizedIdentifier, StringComparison.Ordinal))
        {
            return true;
        }

        var aliasParts = normalizedAlias.Split('-', 2, StringSplitOptions.TrimEntries);
        var identifierParts = normalizedIdentifier.Split('-', 2, StringSplitOptions.TrimEntries);
        if (identifierParts.Length == 2 && aliasParts.Length == 2 && !string.Equals(aliasParts[1], identifierParts[1], StringComparison.Ordinal))
        {
            return false;
        }

        return aliasParts[0].StartsWith(identifierParts[0], StringComparison.Ordinal);
    }

    public static int CompareAliasesDescending(string? left, string? right)
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

        return IdentifierTokenizer().Matches(value)
            .Select(match => match.Value)
            .ToList();
    }

    private static int CompareToken(string left, string right)
    {
        if (int.TryParse(left, out var leftNumber) && int.TryParse(right, out var rightNumber))
        {
            return leftNumber.CompareTo(rightNumber);
        }

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"[0-9]+|[a-zA-Z]+")]
    private static partial Regex IdentifierTokenizer();
}
