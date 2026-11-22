using System.Diagnostics;
using System.Globalization;
using System.Net;
using Edgepad.Protocol;

namespace Edgepad.Dashboard;

/// <summary>
/// TEXT SPEED_RESULT: <see cref="State"/> is a <see cref="SpeedState"/> token, the speeds are Kbit/s, the times ms, -1 when unknown.
/// <see cref="AtUnixMin"/> is when the test began, in minutes since the Unix epoch. <see cref="Bytes"/> is what the test actually moved
/// (download plus upload, 0 while it starts), so the phone can show how much data it used. The most one test moves is <see cref="SpeedTest.MaxTotalBytes"/>.
/// </summary>
internal sealed record SpeedResult(string State, int DownKbps, int UpKbps, int LatencyMs, int JitterMs, long AtUnixMin, long Bytes);

/// <summary>The text of <see cref="TextKind.SpeedResult"/>.</summary>
internal static class SpeedWire
{
    public static string Encode(SpeedResult r) => string.Join(
        '|',
        r.State,
        N(r.DownKbps),
        N(r.UpKbps),
        N(r.LatencyMs),
        N(r.JitterMs),
        N(r.AtUnixMin),
        N(r.Bytes));

    /// <summary>The record for a text, or null when it is not seven fields with a known state. For tests; the phone has its own parser.</summary>
    public static SpeedResult? Parse(string text)
    {
        var f = text.Split('|');
        if (f.Length != 7
            || f[0] is not (SpeedState.Running or SpeedState.Done or SpeedState.Failed or SpeedState.Cancelled or SpeedState.Capped)
            || !int.TryParse(f[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var down)
            || !int.TryParse(f[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var up)
            || !int.TryParse(f[3], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var latency)
            || !int.TryParse(f[4], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var jitter)
            || !long.TryParse(f[5], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var at)
            || !long.TryParse(f[6], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var bytes))
        {
            return null;
        }

        return new SpeedResult(f[0], down, up, latency, jitter, at, bytes);
    }

    private static string N(long value) => (value < 0 ? -1 : value).ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// One speed test at a time against Cloudflare's public speed endpoints, started only by the phone's SPEED_TEST 1 and never on a
/// schedule. Talks to <c>speed.cloudflare.com</c> over HTTPS and nowhere else; redirects are never followed, and any reply that is
/// not a success fails the test. Latency is the median of ten empty downloads, jitter the mean absolute difference of consecutive
/// ones. Download and upload each run four parallel streams for at most <see cref="PhaseLimit"/> or until
/// <see cref="MaxDownloadBytes"/> / <see cref="MaxUploadBytes"/> have been asked for, whichever is first. Cancelling stops every
/// request within a read.
/// </summary>
internal sealed class SpeedTest : IDisposable
{
    /// <summary>The most one test downloads.</summary>
    public const long MaxDownloadBytes = 150L * 1000 * 1000;

    /// <summary>The most one test uploads.</summary>
    public const long MaxUploadBytes = 30L * 1000 * 1000;

    /// <summary>The most one test moves in all, the number the phone shows as its data cost.</summary>
    public const long MaxTotalBytes = MaxDownloadBytes + MaxUploadBytes;

    /// <summary>The only host this class talks to.</summary>
    public const string Host = "speed.cloudflare.com";

    public static readonly TimeSpan PhaseLimit = TimeSpan.FromSeconds(10);

    private const int Streams = 4;
    private const int LatencySamples = 10;
    private const int DownloadChunk = 25_000_000;
    private const int UploadChunk = 2_000_000;
    private const int BufferBytes = 65536;

    private static readonly Uri UpUri = new($"https://{Host}/__up");

    private readonly HttpMessageHandler? handler;
    private readonly Func<double> nowMs;
    private readonly Func<DateTimeOffset> clock;
    private readonly Lock gate = new();
    private CancellationTokenSource? running;
    private bool disposed;

    /// <param name="handler">The transport; tests pass a fake, the app passes none and gets a client that never follows a redirect.</param>
    /// <param name="nowMs">A monotonic clock in ms; tests move their own.</param>
    /// <param name="clock">The wall clock for the result's timestamp.</param>
    public SpeedTest(HttpMessageHandler? handler = null, Func<double>? nowMs = null, Func<DateTimeOffset>? clock = null)
    {
        this.handler = handler;
        this.nowMs = nowMs ?? (() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency);
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>The last test's outcome (never a <see cref="SpeedState.Running"/> one), or null before the first.</summary>
    public SpeedResult? Last { get; private set; }

    public bool IsRunning
    {
        get
        {
            lock (gate)
            {
                return running is not null;
            }
        }
    }

    /// <summary>
    /// Starts a test and returns the task that ends with it, or null when one already runs. <paramref name="report"/> gets a
    /// <see cref="SpeedState.Running"/> result at once and the outcome at the end; it may throw (a dead link) and nothing escapes.
    /// </summary>
    public Task? Start(Action<SpeedResult> report)
    {
        CancellationTokenSource cts;
        lock (gate)
        {
            if (disposed || running is not null)
            {
                return null;
            }

            cts = running = new CancellationTokenSource();
        }

        return Task.Run(async () =>
        {
            var at = clock().ToUnixTimeSeconds() / 60;
            Send(report, new SpeedResult(SpeedState.Running, -1, -1, -1, -1, at, 0));
            var result = await MeasureAsync(at, cts.Token).ConfigureAwait(false);
            lock (gate)
            {
                Last = result;
                running = null;
            }

            cts.Dispose();
            Send(report, result);
        });
    }

    /// <summary>Stops the running test; it ends as <see cref="SpeedState.Cancelled"/>. Nothing happens when none runs.</summary>
    public void Cancel()
    {
        lock (gate)
        {
            running?.Cancel();
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            running?.Cancel();
        }
    }

    /// <summary>Kbit/s for <paramref name="bytes"/> moved in <paramref name="ms"/>; -1 when no time passed.</summary>
    internal static int Kbps(long bytes, double ms) =>
        ms <= 0 ? -1 : (int)Math.Min(int.MaxValue, Math.Round(bytes * 8 / ms));

    /// <summary>The middle sample (the mean of the two middle ones for an even count), in the samples' unit.</summary>
    internal static double Median(IReadOnlyList<double> samples)
    {
        var sorted = samples.Order().ToArray();
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    /// <summary>The mean absolute difference of consecutive samples; 0 for fewer than two.</summary>
    internal static double Jitter(IReadOnlyList<double> samples)
    {
        if (samples.Count < 2)
        {
            return 0;
        }

        var sum = 0.0;
        for (var i = 1; i < samples.Count; i++)
        {
            sum += Math.Abs(samples[i] - samples[i - 1]);
        }

        return sum / (samples.Count - 1);
    }

    private static void Send(Action<SpeedResult> report, SpeedResult result)
    {
        try
        {
            report(result);
        }
        catch (Exception)
        {
            // A lost report costs nothing: the read loop ends the session on a dead socket.
        }
    }

    private static Uri Down(long bytes) => new($"https://{Host}/__down?bytes={bytes.ToString(CultureInfo.InvariantCulture)}");


    private async Task<SpeedResult> MeasureAsync(long at, CancellationToken cancel)
    {
        int latency = -1, jitter = -1, down = -1, up = -1;
        long downBytes = 0, upBytes = 0;
        var state = SpeedState.Done;
        try
        {
            using var client = handler is null
                ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
                : new HttpClient(handler, disposeHandler: false);
            client.Timeout = Timeout.InfiniteTimeSpan;

            var samples = new List<double>();
            for (var i = 0; i < LatencySamples; i++)
            {
                var t = nowMs();
                await DrainAsync(client, new HttpRequestMessage(HttpMethod.Get, Down(0)), null, cancel).ConfigureAwait(false);
                samples.Add(nowMs() - t);
            }

            latency = (int)Math.Round(Median(samples));
            jitter = (int)Math.Round(Jitter(samples));

            var downMs = await RunStreamsAsync(MaxDownloadBytes, DownloadChunk, async (ask, token) =>
            {
                await DrainAsync(client, new HttpRequestMessage(HttpMethod.Get, Down(ask)), n => Interlocked.Add(ref downBytes, n), token).ConfigureAwait(false);
            }, cancel).ConfigureAwait(false);
            down = Kbps(downBytes, downMs);

            var upMs = await RunStreamsAsync(MaxUploadBytes, UploadChunk, async (ask, token) =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, UpUri) { Content = new ZeroContent(ask) };
                await DrainAsync(client, request, null, token).ConfigureAwait(false);
                Interlocked.Add(ref upBytes, ask);
            }, cancel).ConfigureAwait(false);
            up = Kbps(Volatile.Read(ref upBytes), upMs);

            if (Volatile.Read(ref downBytes) >= MaxDownloadBytes || Volatile.Read(ref upBytes) >= MaxUploadBytes)
            {
                state = SpeedState.Capped;
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            state = SpeedState.Cancelled;
        }
        catch (Exception e)
        {
            Log.Write($"speed test failed: {e.GetType().Name}");
            state = SpeedState.Failed;
        }

        return new SpeedResult(state, down, up, latency, jitter, at, Volatile.Read(ref downBytes) + Volatile.Read(ref upBytes));
    }

    /// <summary>
    /// Runs <see cref="Streams"/> workers that each take the next chunk of the <paramref name="cap"/> until it is all asked for or
    /// <see cref="PhaseLimit"/> passes (which is a normal end, not a failure). Returns the elapsed ms. A worker's failure stops the rest and is rethrown.
    /// </summary>
    private async Task<double> RunStreamsAsync(long cap, int chunk, Func<int, CancellationToken, Task> one, CancellationToken cancel)
    {
        var asked = 0L;
        var start = nowMs();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        limit.CancelAfter(PhaseLimit);
        var token = limit.Token;
        var workers = Enumerable.Range(0, Streams).Select(_ => Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    var before = Interlocked.Add(ref asked, chunk) - chunk;
                    if (before >= cap)
                    {
                        return;
                    }

                    await one((int)Math.Min(chunk, cap - before), token).ConfigureAwait(false);
                }
            }
            catch (Exception) when (!token.IsCancellationRequested)
            {
                limit.Cancel();
                throw;
            }
        })).ToArray();

        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Told apart below: the time limit is a normal end, a cancel or a fault is not.
        }

        cancel.ThrowIfCancellationRequested();
        if (workers.FirstOrDefault(w => w.IsFaulted) is { } faulted)
        {
            throw faulted.Exception!.InnerException!;
        }

        return nowMs() - start;
    }

    /// <summary>Sends the request, refuses anything but a success, and reads the whole body, reporting each read's size.</summary>
    private static async Task DrainAsync(HttpClient client, HttpRequestMessage request, Action<int>? read, CancellationToken token)
    {
        using (request)
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new InvalidDataException("The speed server did not answer 200.");
            }

            await using var body = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            var buffer = new byte[BufferBytes];
            int n;
            while ((n = await body.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                read?.Invoke(n);
            }
        }
    }

    /// <summary>A request body of <c>length</c> zero bytes, written in pieces so nothing the size of the upload is ever held.</summary>
    private sealed class ZeroContent : HttpContent
    {
        private static readonly byte[] Zeros = new byte[BufferBytes];
        private readonly long length;

        public ZeroContent(long length) => this.length = length;

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            for (var left = length; left > 0; left -= Zeros.Length)
            {
                await stream.WriteAsync(Zeros.AsMemory(0, (int)Math.Min(left, Zeros.Length))).ConfigureAwait(false);
            }
        }

        protected override bool TryComputeLength(out long result)
        {
            result = length;
            return true;
        }
    }
}
