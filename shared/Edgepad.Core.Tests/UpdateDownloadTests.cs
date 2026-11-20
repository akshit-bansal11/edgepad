using System.Net;
using System.Security.Cryptography;
using Edgepad.Updates;
using Xunit;

namespace Edgepad.Tests;

public sealed class UpdateDownloadTests : IDisposable
{
    private static readonly Uri Start = new("https://github.com/akshit-bansal11/edgepad/releases/download/v5.0.0/a.exe");
    private static readonly byte[] Body = [.. Enumerable.Range(0, 200).Select(i => (byte)i)];

    private readonly string directory = Directory.CreateTempSubdirectory("edgepad-download").FullName;

    private string Dest => Path.Combine(directory, "Edgepad.exe");

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public async Task AFileIsWrittenBesideItsDestinationAndHashedButNeverRenamed()
    {
        var progress = new List<int>();
        var hash = await UpdateDownload.DownloadAsync(
            Start, Dest, 1000, new Progress(progress), Serve(_ => Ok(Body)), TestContext.Current.CancellationToken);

        Assert.Equal(SHA256.HashData(Body), hash);
        Assert.Equal(Body, await File.ReadAllBytesAsync(Dest + ".download", TestContext.Current.CancellationToken));
        Assert.False(File.Exists(Dest));
        Assert.Equal(100, progress[^1]);
    }

    [Fact]
    public async Task ASmallFileComesBackAsBytes()
    {
        var bytes = await UpdateDownload.GetBytesAsync(Start, 1000, Serve(_ => Ok(Body)), TestContext.Current.CancellationToken);
        Assert.Equal(Body, bytes);
    }

    [Fact]
    public async Task ABodyOverTheCapIsRefusedByItsAnnouncedLength()
    {
        Assert.Null(await UpdateDownload.GetBytesAsync(Start, 100, Serve(_ => Ok(Body)), TestContext.Current.CancellationToken));
        Assert.Null(await UpdateDownload.DownloadAsync(
            Start, Dest, 100, null, Serve(_ => Ok(Body)), TestContext.Current.CancellationToken));
        Assert.False(File.Exists(Dest + ".download"));
    }

    [Fact]
    public async Task ABodyOverTheCapIsRefusedByTheBytesActuallyRead()
    {
        HttpResponseMessage Unannounced(HttpRequestMessage _) => new(HttpStatusCode.OK) { Content = new UnknownLength(Body) };

        Assert.Null(await UpdateDownload.GetBytesAsync(Start, 100, Serve(Unannounced), TestContext.Current.CancellationToken));
        Assert.Null(await UpdateDownload.DownloadAsync(
            Start, Dest, 100, null, Serve(Unannounced), TestContext.Current.CancellationToken));
        Assert.False(File.Exists(Dest + ".download"));
    }

    [Fact]
    public async Task ARedirectWithinGitHubIsFollowed()
    {
        var calls = 0;
        var handler = Serve(request =>
        {
            calls++;
            return request.RequestUri == Start
                ? Redirect("https://release-assets.githubusercontent.com/x")
                : Ok(Body);
        });

        Assert.Equal(Body, await UpdateDownload.GetBytesAsync(Start, 1000, handler, TestContext.Current.CancellationToken));
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("http://github.com/a.exe")]
    [InlineData("https://evil.example/a.exe")]
    [InlineData("https://githubusercontent.com.evil.example/a.exe")]
    [InlineData("https://evilgithub.com/a.exe")]
    [InlineData("https://user@github.com/a.exe")]
    [InlineData("https://github.com:8443/a.exe")]
    public async Task ARedirectToAnythingButHttpsOnGitHubIsRefused(string target)
    {
        var handler = Serve(request => request.RequestUri == Start ? Redirect(target) : Ok(Body));
        Assert.Null(await UpdateDownload.GetBytesAsync(Start, 1000, handler, TestContext.Current.CancellationToken));
        Assert.Null(await UpdateDownload.DownloadAsync(Start, Dest, 1000, null, handler, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(Dest + ".download"));
    }

    [Fact]
    public async Task FiveRedirectsAreFollowedAndASixthIsRefused()
    {
        HttpMessageHandler Chain(int hops)
        {
            var seen = 0;
            return Serve(_ => ++seen <= hops ? Redirect($"https://github.com/hop{seen}") : Ok(Body));
        }

        Assert.Equal(Body, await UpdateDownload.GetBytesAsync(Start, 1000, Chain(5), TestContext.Current.CancellationToken));
        Assert.Null(await UpdateDownload.GetBytesAsync(Start, 1000, Chain(6), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AFirstAddressThatIsNotHttpsOnGitHubIsRefusedWithoutAskingAnyone()
    {
        var asked = false;
        var handler = Serve(_ =>
        {
            asked = true;
            return Ok(Body);
        });

        Assert.Null(await UpdateDownload.GetBytesAsync(new Uri("http://github.com/a"), 1000, handler, TestContext.Current.CancellationToken));
        Assert.Null(await UpdateDownload.GetBytesAsync(new Uri("https://evil.example/a"), 1000, handler, TestContext.Current.CancellationToken));
        Assert.False(asked);
    }

    [Fact]
    public async Task AFailedStatusIsRefused() =>
        Assert.Null(await UpdateDownload.GetBytesAsync(
            Start, 1000, Serve(_ => new HttpResponseMessage(HttpStatusCode.NotFound)), TestContext.Current.CancellationToken));

    private static FuncHandler Serve(Func<HttpRequestMessage, HttpResponseMessage> respond) => new(respond);

    private static HttpResponseMessage Ok(byte[] body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    private static HttpResponseMessage Redirect(string to) =>
        new(HttpStatusCode.Found) { Headers = { Location = new Uri(to) } };

    private sealed class FuncHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    /// <summary>A body that does not say how long it is, so only counting the bytes can enforce a cap.</summary>
    private sealed class UnknownLength(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class Progress(List<int> seen) : IProgress<int>
    {
        public void Report(int value) => seen.Add(value);
    }
}
