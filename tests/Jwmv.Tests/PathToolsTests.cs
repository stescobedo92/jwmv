using Jwmv.Core.Utilities;

namespace Jwmv.Tests;

public sealed class PathToolsTests
{
    [Fact]
    public void PrependPathEntry_DeduplicatesExistingValue()
    {
        var original = string.Join(Path.PathSeparator, ["C:\\Java\\bin", "C:\\Tools"]);
        var updated = PathTools.PrependPathEntry(original, "C:\\Java\\bin");

        Assert.Equal(original, updated);
    }

    [Fact]
    public void RemovePathEntry_RemovesTargetIgnoringCase()
    {
        var original = string.Join(Path.PathSeparator, ["C:\\JAVA\\bin", "C:\\Tools"]);
        var updated = PathTools.RemovePathEntry(original, "c:\\java\\bin");

        Assert.Equal("C:\\Tools", updated);
    }

    [Theory]
    [InlineData(@"C:\jwmv\manifests", @"C:\jwmv\manifests\21-tem.json", true)]
    [InlineData(@"C:\jwmv\manifests", @"C:\jwmv\manifests", true)]
    [InlineData(@"C:\jwmv\manifests\", @"C:\jwmv\manifests\nested\x.json", true)]
    [InlineData(@"C:\jwmv\manifests", @"c:\JWMV\MANIFESTS\x.json", true)]
    [InlineData(@"C:\jwmv\manifests", @"C:\jwmv\manifests-evil\x.json", false)]
    [InlineData(@"C:\jwmv\manifests", @"C:\jwmv\manifests\..\config.json", false)]
    [InlineData(@"C:\jwmv\manifests", @"C:\jwmv\manifests\..\..\evil.json", false)]
    [InlineData(@"C:\jwmv\manifests", @"\\server\share\evil.json", false)]
    [InlineData(@"C:\jwmv\manifests", @"D:\other\x.json", false)]
    public void IsWithinDirectory_RequiresPathToStayUnderRoot(string root, string path, bool expected)
    {
        Assert.Equal(expected, PathTools.IsWithinDirectory(root, path));
    }
}
