using System.Net;
using Jwmv.Core.Exceptions;
using Jwmv.Infrastructure.Net;

namespace Jwmv.Tests;

public sealed class HttpArchiveDownloaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jwmv-download-tests", Guid.NewGuid().ToString("n"));

    [Fact]
    public async Task DownloadAsync_WritesTheFileAndLeavesNoTemporaryFiles()
    {
        var factory = new FakeHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1, 2, 3 }) });
        var destination = Path.Combine(_root, "archive.zip");

        await new HttpArchiveDownloader(factory).DownloadAsync(new Uri("https://example.invalid/archive.zip"), destination, null, CancellationToken.None);

        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(destination));
        Assert.Equal(["archive.zip"], Directory.GetFiles(_root).Select(Path.GetFileName));
    }

    [Fact]
    public async Task DownloadAsync_RejectsNonHttpsUrisWithoutSendingRequests()
    {
        var factory = new FakeHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) });
        var destination = Path.Combine(_root, "archive.zip");

        await Assert.ThrowsAnyAsync<JwmvException>(() => new HttpArchiveDownloader(factory).DownloadAsync(new Uri("http://example.invalid/archive.zip"), destination, null, CancellationToken.None));
        Assert.Equal(0, factory.Requests);
        Assert.False(Directory.Exists(_root) && Directory.GetFiles(_root).Length > 0);
    }

    [Fact]
    public async Task DownloadAsync_DoesNotLeaveAPartialFileWhenTheStreamFails()
    {
        var factory = new FakeHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new FailingStream(failAfterBytes: 10)) });
        var destination = Path.Combine(_root, "archive.zip");

        await Assert.ThrowsAnyAsync<Exception>(() => new HttpArchiveDownloader(factory).DownloadAsync(new Uri("https://example.invalid/archive.zip"), destination, null, CancellationToken.None));
        Assert.False(File.Exists(destination));
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task DownloadAsync_RejectsResponsesLargerThanTheLimit()
    {
        var factory = new FakeHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[2048]) });
        var destination = Path.Combine(_root, "archive.zip");

        await Assert.ThrowsAnyAsync<JwmvException>(() => new HttpArchiveDownloader(factory, maxBytes: 1024).DownloadAsync(new Uri("https://example.invalid/archive.zip"), destination, null, CancellationToken.None));
        Assert.False(File.Exists(destination));
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task DownloadAsync_RejectsStreamsThatGrowBeyondTheLimitWithoutContentLength()
    {
        var factory = new FakeHttpClientFactory(_ =>
        {
            var content = new StreamContent(new MemoryStream(new byte[2048]));
            content.Headers.ContentLength = null;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        var destination = Path.Combine(_root, "archive.zip");

        await Assert.ThrowsAnyAsync<JwmvException>(() => new HttpArchiveDownloader(factory, maxBytes: 1024).DownloadAsync(new Uri("https://example.invalid/archive.zip"), destination, null, CancellationToken.None));
        Assert.False(File.Exists(destination));
        Assert.Empty(Directory.GetFiles(_root));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FakeHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> responder) : IHttpClientFactory
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder = responder;

        public int Requests { get; private set; }

        public HttpClient CreateClient(string name) => new(new Handler(this));

        private sealed class Handler(FakeHttpClientFactory owner) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                owner.Requests++;
                return Task.FromResult(owner._responder(request));
            }
        }
    }

    private sealed class FailingStream(int failAfterBytes) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= failAfterBytes)
            {
                throw new IOException("simulated connection reset");
            }

            var toRead = Math.Min(count, failAfterBytes - _position);
            Array.Fill(buffer, (byte)7, offset, toRead);
            _position += toRead;
            return toRead;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
