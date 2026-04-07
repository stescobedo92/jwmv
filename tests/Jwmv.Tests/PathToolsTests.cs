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
}
