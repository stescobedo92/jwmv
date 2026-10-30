using Jwmv.Core.Utilities;

namespace Jwmv.Tests;

public sealed class SdkIdentifierTests
{
    [Theory]
    [InlineData("9.5.1", true)]
    [InlineData("4.0.0-rc-5", false)]
    [InlineData("9.6.0-milestone-2", false)]
    [InlineData("2.3.0-Beta", false)]
    public void IsStableVersion_FiltersPrereleaseMarkers(string version, bool expected)
    {
        Assert.Equal(expected, SdkIdentifier.IsStableVersion(version));
    }

    [Fact]
    public void CompareVersionsDescending_PrefersNewestStableVersion()
    {
        var versions = new[] { "3.9.8", "3.9.15", "3.8.9" };

        var sorted = versions.OrderBy(item => item, Comparer<string>.Create(SdkIdentifier.CompareVersionsDescending)).ToList();

        Assert.Equal(["3.9.15", "3.9.8", "3.8.9"], sorted);
    }

    [Theory]
    [InlineData("21.0.4-tem", true)]
    [InlineData("21-tem", true)]
    [InlineData("9.5.1", true)]
    [InlineData("21.0.4+7-tem", true)]
    [InlineData("17.0.2.8.1-graalvm", true)]
    [InlineData("../../evil", false)]
    [InlineData(@"..\evil", false)]
    [InlineData(@"C:\evil", false)]
    [InlineData(@"\\server\share\evil", false)]
    [InlineData("/etc/passwd", false)]
    [InlineData("..", false)]
    [InlineData(".", false)]
    [InlineData("-21", false)]
    [InlineData("21 tem", false)]
    [InlineData("21-tem'; Remove-Item", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void IsSafeAlias_AllowsOnlyPlainVersionAliases(string? value, bool expected)
    {
        Assert.Equal(expected, SdkIdentifier.IsSafeAlias(value));
    }

    [Fact]
    public void IsSafeAlias_RejectsOverlongValues()
    {
        Assert.True(SdkIdentifier.IsSafeAlias(new string('1', 128)));
        Assert.False(SdkIdentifier.IsSafeAlias(new string('1', 129)));
    }

    [Theory]
    [InlineData("java", true)]
    [InlineData("JAVA", true)]
    [InlineData("gradle", true)]
    [InlineData("my-sdk2", true)]
    [InlineData("../java", false)]
    [InlineData(@"..\java", false)]
    [InlineData("java;evil", false)]
    [InlineData("java home", false)]
    [InlineData("-java", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSafeCandidateName_AllowsOnlyPlainIdentifiers(string? value, bool expected)
    {
        Assert.Equal(expected, SdkIdentifier.IsSafeCandidateName(value));
    }

    [Theory]
    [InlineData("apache-maven-3.9.15-bin.zip", true)]
    [InlineData("OpenJDK21U-jdk_x64_windows_hotspot_21.0.4_7.zip", true)]
    [InlineData("kotlin-compiler-2.3.21.zip", true)]
    [InlineData("../evil.zip", false)]
    [InlineData(@"..\evil.zip", false)]
    [InlineData(@"C:\evil.zip", false)]
    [InlineData("dir/evil.zip", false)]
    [InlineData("evil.zip.", false)]
    [InlineData(".hidden", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSafeFileName_AllowsOnlySimpleFileNames(string? value, bool expected)
    {
        Assert.Equal(expected, SdkIdentifier.IsSafeFileName(value));
    }

    [Theory]
    [InlineData("JAVA_HOME", true)]
    [InlineData("_private", true)]
    [InlineData("JWMV_ACTIVE_JAVA_BIN", true)]
    [InlineData("9HOME", false)]
    [InlineData("JAVA HOME", false)]
    [InlineData("JAVA_HOME = 'x'; Remove-Item", false)]
    [InlineData("JAVA-HOME", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSafeEnvironmentVariableName_AllowsOnlyIdentifierCharacters(string? value, bool expected)
    {
        Assert.Equal(expected, SdkIdentifier.IsSafeEnvironmentVariableName(value));
    }
}
