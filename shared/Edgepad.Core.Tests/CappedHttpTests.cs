using System.Diagnostics;
using System.Net;
using Edgepad.Dashboard;
using Xunit;

namespace Edgepad.Tests;

public sealed class CappedHttpTests
{
    [Fact]
    public async Task ABodyThatStopsArrivingEndsInFailureAtTheDeadline()
    {
        using var http = new FakeHttp(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StuckStream()) });
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.invalid/");
        var watch = Stopwatch.StartNew();

        var reply = await CappedHttp.SendAsync(request, 1024, TimeSpan.FromMilliseconds(200), http, "Test", TestContext.Current.CancellationToken);

        Assert.Null(reply);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task CancellingTheCallerStillThrows()
    {
        using var http = new FakeHttp(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StuckStream()) });
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.invalid/");
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CappedHttp.SendAsync(request, 1024, TimeSpan.FromSeconds(30), http, "Test", cancel.Token));
    }

    /// <summary>A body whose bytes never come: every read waits until it is cancelled.</summary>
    private sealed class StuckStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
