using Jwmv.Core.Utilities;

namespace Jwmv.Tests;

public sealed class JavaIdentifierTests
{
    [Theory]
    [InlineData("21.0.4+7", "temurin", "21.0.4.7-tem")]
    [InlineData("17.0.12", "microsoft", "17.0.12-ms")]
    public void BuildAlias_NormalizesVersionsAndDistributions(string javaVersion, string distribution, string expected)
    {
        var alias = JavaIdentifier.BuildAlias(javaVersion, distribution);
        Assert.Equal(expected, alias);
    }

    [Theory]
    [InlineData("21.0.4.7-tem", "21-tem")]
    [InlineData("21.0.4.7-tem", "21.0.4-tem")]
    [InlineData("21.0.4.7-tem", "21.0.4.7-tem")]
    public void Matches_AcceptsExactAndFuzzyIdentifiers(string alias, string identifier)
    {
        Assert.True(JavaIdentifier.Matches(alias, identifier));
    }

    [Fact]
    public void CompareAliasesDescending_PrefersNewerVersionFirst()
    {
        var values = new List<string> { "17.0.12-tem", "21.0.4.7-tem", "21.0.2.9-tem" };
        values.Sort(JavaIdentifier.CompareAliasesDescending);
        Assert.Equal("21.0.4.7-tem", values[0]);
    }
}
