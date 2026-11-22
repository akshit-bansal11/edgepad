using System.Net;
using Edgepad.Blob;
using Edgepad.Dashboard;
using Edgepad.Net;
using Edgepad.Protocol;
using Xunit;

namespace Edgepad.Tests;

/// <summary>NET_INFO and the speed test: payloads, the watch gate, the arithmetic, the caps, the cancel. No test touches the network.</summary>
public sealed class NetTests : IDisposable
{
    private readonly DirectoryInfo dir = Directory.CreateTempSubdirectory("edgepad-net");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => dir.Delete(recursive: true);

    [Fact]
    public void ANetInfoSurvivesTheWire()
    {
        var info = new NetInfo(NetKind.Wifi, "5", 866, 72, "192.168.1.23", "192.168.1.1");
        var text = NetInfoWire.Encode(info);

        Assert.Equal("wifi|5|866|72|192.168.1.23|192.168.1.1", text);
        Assert.Equal(info, NetInfoWire.Parse(text));
        Assert.Equal(6, text.Split('|').Length);
    }

    [Fact]
    public void UnknownThingsAreWrittenAsUnknownsAndNothingElse()
    {
        Assert.Equal("none|?|-1|-1||", NetInfoWire.Encode(NetInfo.Unknown));
        Assert.Equal("other|?|-1|100|1.2.3.4 evil|", NetInfoWire.Encode(new NetInfo("satellite", "7", -5, 900, "1.2.3.4|evil", "")));
        Assert.Null(NetInfoWire.Parse("wifi|5|866|72|1.2.3.4"));
        Assert.Null(NetInfoWire.Parse("lan|5|866|72|1.2.3.4|1.2.3.1"));
    }

    [Fact]
    public void AnAddressNeverCarriesASeparatorOrMoreThanFifteenCharacters()
    {
        var text = NetInfoWire.Encode(new NetInfo(NetKind.Ethernet, "?", 1000, -1, "10.0.0.1|x/y:z", new string('9', 40)));

        Assert.Equal(6, text.Split('|').Length);
        Assert.All(text.Split('|')[4..], part => Assert.True(part.Length <= 15));
    }

    [Fact]
    public void ASpeedResultSurvivesTheWire()
    {
        var result = new SpeedResult(SpeedState.Done, 93500, 21400, 11, 2, 29499900, 163400000);
        var text = SpeedWire.Encode(result);

        Assert.Equal("done|93500|21400|11|2|29499900|163400000", text);
        Assert.Equal(result, SpeedWire.Parse(text));
        Assert.Null(SpeedWire.Parse("done|1|2|3|4|5"));
        Assert.Null(SpeedWire.Parse("fast|1|2|3|4|5|6"));
    }

    [Fact]
    public void TheCalculatorsAgreeWithTheirDefinitions()
    {
        Assert.Equal(10000, SpeedTest.Kbps(1_250_000, 1000));
        Assert.Equal(-1, SpeedTest.Kbps(1, 0));
        Assert.Equal(3.0, SpeedTest.Median([5, 1, 3]));
        Assert.Equal(2.5, SpeedTest.Median([4, 1, 2, 3]));
        Assert.Equal(0.0, SpeedTest.Jitter([7]));
        Assert.Equal(3.0, SpeedTest.Jitter([10, 12, 10, 14, 10]), 6);
    }

    [Fact]
    public void NetInfoIsReadAndSentOnlyWhileTheNetworkBitIsSetAndOnlyWhenItChanges()
    {
        var net = new FakeNet();
        List<Text> sent = [];
        using var reporter = new StatsReporter(new FakeStats(), sent.Add, _ => new Stopper(), work => work(), net);

        reporter.SetWatch(StatsReporter.WidgetsBit);
        reporter.Tick();
        Assert.Equal(0, net.Reads);
        Assert.Empty(NetTexts(sent));

        reporter.SetWatch(StatsReporter.WidgetsBit | StatsReporter.NetBit);
        reporter.Tick();
        reporter.Tick();
        Assert.Equal(2, net.Reads);
        Assert.Single(NetTexts(sent));

        net.Info = net.Info with { LinkMbps = 433 };
        reporter.Tick();
        Assert.Equal(2, NetTexts(sent).Count);

        reporter.SetWatch(StatsReporter.WidgetsBit);
        reporter.Tick();
        Assert.Equal(3, net.Reads);

        reporter.SetWatch(StatsReporter.WidgetsBit | StatsReporter.NetBit);
        reporter.Tick();
        Assert.Equal(3, NetTexts(sent).Count);
    }

    [Fact]
    public void TheNetworkBitAloneStartsTheTimerAndASourceThatThrowsPublishesUnknowns()
    {
        var net = new FakeNet { Throws = true };
        List<Text> sent = [];
        using var reporter = new StatsReporter(new FakeStats(), sent.Add, _ => new Stopper(), work => work(), net);

        reporter.SetWatch(StatsReporter.NetBit);
        reporter.Tick();

        Assert.True(reporter.Running);
        Assert.Equal(NetInfoWire.Encode(NetInfo.Unknown), Assert.Single(NetTexts(sent)).Value);
    }

    [Fact]
    public async Task ADownloadAndAnUploadAreMeasuredFromBytesAndTime()
    {
        var server = new SpeedServer { DownKbps = 80_000, UpKbps = 20_000 };
        using var test = new SpeedTest(server, server.Read, () => new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero));
        List<SpeedResult> reports = [];

        await Run(test, reports);

        var done = reports[^1];
        Assert.Equal([SpeedState.Running, SpeedState.Capped], reports.Select(r => r.State));
        Assert.InRange(done.DownKbps, 79_200, 80_800);
        Assert.InRange(done.UpKbps, 19_800, 20_200);
        Assert.Equal(11, done.LatencyMs);
        Assert.Equal(3, done.JitterMs);
        Assert.Equal(new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds() / 60, done.AtUnixMin);
        Assert.Equal(SpeedTest.MaxTotalBytes, done.Bytes); // a capped run moved exactly its caps
        Assert.Equal(0, reports[0].Bytes);
        Assert.Equal(done, test.Last);
    }

    [Fact]
    public async Task NoTestEverMovesMoreThanItsCapsOrCallsAnyOtherHost()
    {
        var server = new SpeedServer();
        using var test = new SpeedTest(server, server.Read);

        await Run(test, []);

        Assert.Equal(SpeedTest.MaxDownloadBytes, server.Asked);
        Assert.Equal(SpeedTest.MaxUploadBytes, server.Uploaded);
        Assert.All(server.Seen, uri =>
        {
            Assert.Equal("https", uri.Scheme);
            Assert.Equal(SpeedTest.Host, uri.Host);
        });
    }

    [Fact]
    public async Task ARedirectIsNeverFollowedAndFailsTheTest()
    {
        var server = new SpeedServer { Override = () => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://evil.example/x") } } };
        using var test = new SpeedTest(server, server.Read);
        List<SpeedResult> reports = [];

        await Run(test, reports);

        Assert.Equal(SpeedState.Failed, reports[^1].State);
        Assert.Single(server.Seen);
        Assert.Equal(-1, reports[^1].DownKbps);
    }

    [Fact]
    public async Task ASecondStartIsIgnoredAndACancelEndsTheTestQuickly()
    {
        var server = new SpeedServer { Block = true };
        using var test = new SpeedTest(server, server.Read);
        List<SpeedResult> reports = [];
        var task = test.Start(r => reports.Add(r));
        Assert.NotNull(task);
        Assert.True(SpinWait.SpinUntil(() => server.Seen.Count > 0, TimeSpan.FromSeconds(5)));

        Assert.True(test.IsRunning);
        Assert.Null(test.Start(r => reports.Add(r)));

        test.Cancel();
        await task.WaitAsync(TimeSpan.FromSeconds(1), Ct);

        Assert.False(test.IsRunning);
        Assert.Equal([SpeedState.Running, SpeedState.Cancelled], reports.Select(r => r.State));
        Assert.Equal(SpeedState.Cancelled, test.Last?.State);
    }

    [Fact]
    public async Task AReportThatThrowsDoesNotStopTheTest()
    {
        var server = new SpeedServer();
        using var test = new SpeedTest(server, server.Read);

        var task = test.Start(_ => throw new IOException("dead socket"));
        await task!.WaitAsync(TimeSpan.FromSeconds(30), Ct);

        Assert.Equal(SpeedState.Capped, test.Last?.State);
    }

    [Fact]
    public void ThePhoneIsToldAboutNetInfoOnlyWhenTheLaptopCanReadIt()
    {
        var settings = Settings();
        using var withNet = Host(settings, new List<Frame>(), new FakeNet(), new SpeedTest());
        using var withoutNet = Host(settings, new List<Frame>(), null, new SpeedTest());

        Assert.Contains(Feature.NetInfo, withNet.Features());
        Assert.DoesNotContain(Feature.NetInfo, withoutNet.Features());
    }

    [Fact]
    public void ASpeedTestRunsOnlyOnAskAndItsLastResultIsResentToAPhoneThatOpensThePage()
    {
        var settings = Settings();
        List<Frame> frames = [];
        var server = new SpeedServer();
        using var host = Host(settings, frames, new FakeNet(), new SpeedTest(server, server.Read));

        host.OnWatch(IntegrationHost.NetBit);
        Assert.Empty(Speeds(frames));
        Assert.Empty(server.Seen);

        host.OnSpeedTest(true);
        Assert.True(SpinWait.SpinUntil(() => Speeds(frames).Any(t => t.Value.StartsWith("capped|", StringComparison.Ordinal)), TimeSpan.FromSeconds(30)));
        Assert.Equal(2, Speeds(frames).Count);

        host.OnWatch(0);
        host.OnWatch(IntegrationHost.NetBit);
        Assert.Equal(3, Speeds(frames).Count);
        Assert.StartsWith("capped|", Speeds(frames)[^1].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void ASpeedTestIsIgnoredWhenTheLaptopHasNoNetworkSource()
    {
        var settings = Settings();
        List<Frame> frames = [];
        var server = new SpeedServer();
        using var host = Host(settings, frames, null, new SpeedTest(server, server.Read));

        host.OnSpeedTest(true);

        Assert.Empty(server.Seen);
        Assert.Empty(Speeds(frames));
    }

    private static List<Text> NetTexts(List<Text> sent) => [.. sent.Where(t => t.Kind == (byte)TextKind.NetInfo)];

    private static List<Text> Speeds(List<Frame> frames)
    {
        lock (frames)
        {
            return [.. frames.OfType<Text>().Where(t => t.Kind == (byte)TextKind.SpeedResult)];
        }
    }

    private static async Task Run(SpeedTest test, List<SpeedResult> reports)
    {
        var task = test.Start(r =>
        {
            lock (reports)
            {
                reports.Add(r);
            }
        });
        await task!.WaitAsync(TimeSpan.FromSeconds(30), Ct);
    }

    private IntegrationSettings Settings()
    {
        var settings = new IntegrationSettings(Path.Combine(dir.FullName, IntegrationSettings.FileName));
        settings.SetFlag(IntegrationSettings.Stats, true);
        return settings;
    }

    private static IntegrationHost Host(IntegrationSettings settings, List<Frame> frames, INetInfoSource? net, SpeedTest speed)
    {
        void Record(Frame frame)
        {
            lock (frames)
            {
                frames.Add(frame);
            }
        }

        var blobs = new BlobSender(Record, LinkKind.Bluetooth, () => 0, Ct, () => long.MaxValue, _ => true);
        return new IntegrationHost(
            Record, blobs, settings, new DashboardParts(), new FakeStats(), null, (_, _) => new Stopper(), work => work(), null, net, speed);
    }

    private sealed class Stopper : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private sealed class FakeNet : INetInfoSource
    {
        public NetInfo Info { get; set; } = new(NetKind.Wifi, "5", 866, -1, "192.168.1.23", "192.168.1.1");

        public bool Throws { get; set; }

        public int Reads { get; private set; }

        public NetInfo Read()
        {
            Reads++;
            return Throws ? throw new InvalidOperationException("no adapter") : Info;
        }
    }

    private sealed class FakeStats : IStatsSource
    {
        public StatsSample ReadFast() => new(1, 2, 3, 4, 5, 6, -1, -1, 7, 8, 9);

        public StatsInfo ReadInfo() => new("cpu", 1024, "gpu", 512, "net");

        public IReadOnlyList<DiskEntry> ReadDisks() => [];

        public ValueTask<int> PingAsync(CancellationToken cancel) => ValueTask.FromResult(1);
    }

    /// <summary>Cloudflare in memory. Time is a number the server moves: every byte read or sent costs its share of the line rate.</summary>
    private sealed class SpeedServer : HttpMessageHandler
    {
        private static readonly double[] Latencies = [10, 12, 10, 14, 10, 12, 10, 14, 10, 12];

        private readonly Lock gate = new();
        private double now;
        private int pings;
        private long asked;
        private long uploaded;

        public double DownKbps { get; set; } = 80_000;

        public double UpKbps { get; set; } = 20_000;

        public bool Block { get; set; }

        public Func<HttpResponseMessage>? Override { get; set; }

        public long Asked => Interlocked.Read(ref asked);

        public long Uploaded => Interlocked.Read(ref uploaded);

        public List<Uri> Seen { get; } = [];

        public double Read()
        {
            lock (gate)
            {
                return now;
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (gate)
            {
                Seen.Add(request.RequestUri!);
            }

            if (Block)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (Override is { } reply)
            {
                return reply();
            }

            if (request.Method == HttpMethod.Post)
            {
                await using var body = await request.Content!.ReadAsStreamAsync(cancellationToken);
                var buffer = new byte[65536];
                int n;
                while ((n = await body.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    Advance(n * 8 / UpKbps);
                    Interlocked.Add(ref uploaded, n);
                }

                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(string.Empty) };
            }

            var bytes = long.Parse(request.RequestUri!.Query.Split('=')[1], System.Globalization.CultureInfo.InvariantCulture);
            if (bytes == 0)
            {
                Advance(Latencies[(Interlocked.Increment(ref pings) - 1) % Latencies.Length]);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(string.Empty) };
            }

            Interlocked.Add(ref asked, bytes);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new Zeros(bytes, n => Advance(n * 8 / DownKbps))) };
        }

        private void Advance(double ms)
        {
            lock (gate)
            {
                now += ms;
            }
        }
    }

    /// <summary>A read-only stream of <c>length</c> zero bytes that tells <c>onRead</c> how much each read gave.</summary>
    private sealed class Zeros(long length, Action<int> onRead) : Stream
    {
        private long left = length;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(Span<byte> buffer)
        {
            var n = (int)Math.Min(left, buffer.Length);
            buffer[..n].Clear();
            left -= n;
            if (n > 0)
            {
                onRead(n);
            }

            return n;
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
