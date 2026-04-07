namespace Jwmv.Core.Utilities;

public static class DistributionAlias
{
    private static readonly Dictionary<string, string> DistributionToAlias = new(StringComparer.OrdinalIgnoreCase)
    {
        ["temurin"] = "tem",
        ["zulu"] = "zulu",
        ["microsoft"] = "ms",
        ["graalvm_community"] = "graalvm",
        ["graalvm"] = "graalvm",
        ["corretto"] = "cor",
        ["liberica"] = "lib",
        ["sap_machine"] = "sap",
        ["oracle_open_jdk"] = "ojdk",
        ["oracle"] = "oracle"
    };

    private static readonly Dictionary<string, string> AliasToDistribution = DistributionToAlias
        .GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.First())
        .ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static string ToAlias(string distribution) =>
        DistributionToAlias.TryGetValue(distribution, out var alias)
            ? alias
            : distribution.Replace("_", "-", StringComparison.Ordinal).ToLowerInvariant();

    public static string ToDistribution(string aliasOrDistribution) =>
        AliasToDistribution.TryGetValue(aliasOrDistribution, out var distribution)
            ? distribution
            : aliasOrDistribution;
}
