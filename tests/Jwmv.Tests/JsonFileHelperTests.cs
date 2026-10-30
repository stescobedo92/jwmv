using Jwmv.Infrastructure.Storage;

namespace Jwmv.Tests;

public sealed class JsonFileHelperTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jwmv-json-tests", Guid.NewGuid().ToString("n"));

    [Fact]
    public async Task WriteAsync_ReplacesExistingContentAndLeavesNoTemporaryFiles()
    {
        var path = Path.Combine(_root, "config.json");
        await JsonFileHelper.WriteAsync(path, new Payload { Name = "first" }, CancellationToken.None);

        await JsonFileHelper.WriteAsync(path, new Payload { Name = "second" }, CancellationToken.None);

        var read = await JsonFileHelper.ReadAsync<Payload>(path, CancellationToken.None);
        Assert.Equal("second", read?.Name);
        Assert.Equal(["config.json"], Directory.GetFiles(_root).Select(Path.GetFileName));
    }

    [Fact]
    public async Task WriteAsync_KeepsThePreviousFileWhenSerializationFails()
    {
        var path = Path.Combine(_root, "config.json");
        await JsonFileHelper.WriteAsync(path, new Payload { Name = "first" }, CancellationToken.None);

        await Assert.ThrowsAnyAsync<Exception>(() => JsonFileHelper.WriteAsync(path, new ExplodingPayload(), CancellationToken.None));

        var read = await JsonFileHelper.ReadAsync<Payload>(path, CancellationToken.None);
        Assert.Equal("first", read?.Name);
        Assert.Equal(["config.json"], Directory.GetFiles(_root).Select(Path.GetFileName));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class Payload
    {
        public string? Name { get; init; }
    }

    private sealed class ExplodingPayload
    {
        public string Name => throw new InvalidOperationException("boom");
    }
}
