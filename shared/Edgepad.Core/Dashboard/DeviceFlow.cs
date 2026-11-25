using System.Text;
using System.Text.Json;

namespace Edgepad.Dashboard;

/// <summary>A token reply that passed its host's checks: the access token, and the refresh token and its life when it expires.</summary>
internal sealed record DeviceToken(string Value, string? Refresh, int? ExpiresIn);

/// <summary>
/// The half of an OAuth device authorization grant (RFC 8628) that GitHub and GitLab sign-in share: one flow at a time, the
/// poll loop with its interval and slow_down, and the capped form POST that asks. What a token reply must hold and where the
/// tokens are saved stay with each host, because the two hosts' rules for them differ.
/// </summary>
internal sealed class DeviceFlow(
    HttpMessageHandler? handler,
    Func<DateTimeOffset> now,
    Func<TimeSpan, CancellationToken, Task> wait)
{
    /// <summary>The cap on every reply from either host.</summary>
    public const int MaxBytes = 16 * 1024;

    /// <summary>Seconds added to the interval each time the host says slow_down, as RFC 8628 says.</summary>
    public const int SlowDownSeconds = 5;

    private const int DefaultInterval = 5;
    private const int MaxDeviceCode = 255;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly Lock gate = new();
    private Flow? current;

    /// <summary>
    /// The device code reply: device_code (1..255 printable ASCII), user_code matching <c>^[A-Z0-9-]{4,20}$</c>, interval 1..60
    /// (5 when absent) and expires_in 60..1800 seconds. verification_uri is never read: each host shows its own fixed page.
    /// </summary>
    public static IssuedCode? ParseCode(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var deviceCode = GitJson.Text(root, "device_code");
            var userCode = GitJson.Text(root, "user_code");
            var expiresIn = Number(root, "expires_in");
            var interval = root.TryGetProperty("interval", out _) ? Number(root, "interval") : DefaultInterval;
            return deviceCode is { Length: > 0 and <= MaxDeviceCode } && deviceCode.All(c => c is > ' ' and <= '~')
                && userCode is { Length: >= 4 and <= 20 } && userCode.All(c => c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-')
                && expiresIn is int expires and >= 60 and <= 1800 && interval is int every and >= 1 and <= 60
                ? new IssuedCode(deviceCode, userCode, every, expires)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A JSON number that fits an int, or null when <paramref name="name"/> is absent or not one. Unlike <see cref="GitJson.Whole"/>, a negative number stays negative, so a range check sees it.</summary>
    public static int? Number(JsonElement root, string name) =>
        root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n) ? n : null;

    /// <summary>
    /// Makes <paramref name="issued"/> the flow in progress, stopping any other. <paramref name="epoch"/> is handed back to
    /// the save in <see cref="WaitAsync"/>, for a host that has to tell whether another sign-in began since.
    /// </summary>
    public void Begin(IssuedCode issued, long epoch = 0)
    {
        var flow = new Flow(issued.DeviceCode, issued.Interval, now() + TimeSpan.FromSeconds(issued.ExpiresIn), epoch);
        lock (gate)
        {
            current?.Stop();
            current = flow;
        }
    }

    /// <summary>
    /// Polls <paramref name="tokenUri"/> with the form <paramref name="form"/> builds from the device code, until the person has
    /// entered the code, said no, or the code ran out. <paramref name="readToken"/> checks a token reply (null fails it) and
    /// <paramref name="save"/> stores it. Never throws: cancelling <paramref name="ct"/> or calling <see cref="Cancel"/> ends it as
    /// <see cref="SignInState.Cancelled"/>. A failed poll is tried again at the next interval until the code runs out.
    /// </summary>
    public async Task<SignInState> WaitAsync(
        Uri tokenUri,
        Func<string, string> form,
        string what,
        Func<JsonElement, string, DeviceToken?> readToken,
        Func<DeviceToken, long, bool> save,
        CancellationToken ct)
    {
        Flow? flow;
        lock (gate)
        {
            flow = current;
        }

        if (flow is null || Interlocked.Exchange(ref flow.Waiting, 1) == 1)
        {
            return SignInState.Failed;
        }

        try
        {
            var interval = flow.Interval;
            var body = form(flow.DeviceCode);
            while (true)
            {
                await Task.WhenAny(SafeWait(TimeSpan.FromSeconds(interval), ct), flow.Stopped.Task).ConfigureAwait(false);
                if (ct.IsCancellationRequested || flow.Stopped.Task.IsCompleted)
                {
                    return SignInState.Cancelled;
                }

                if (now() >= flow.Deadline)
                {
                    return SignInState.Expired;
                }

                var reply = await PostAsync(tokenUri, body, what, ct).ConfigureAwait(false);
                if (flow.Stopped.Task.IsCompleted)
                {
                    return SignInState.Cancelled;
                }

                switch (ReadPoll(reply, readToken))
                {
                    case Poll.Token token:
                        return save(token.Value, flow.Epoch) ? SignInState.SignedIn : SignInState.Failed;
                    case Poll.Pending:
                        break;
                    case Poll.SlowDown:
                        interval += SlowDownSeconds;
                        break;
                    case Poll.End end:
                        return end.State;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return SignInState.Cancelled;
        }
        finally
        {
            lock (gate)
            {
                if (current == flow)
                {
                    current = null;
                }
            }
        }
    }

    /// <summary>Stops a sign-in in progress; its <see cref="WaitAsync"/> ends as <see cref="SignInState.Cancelled"/>.</summary>
    public void Cancel()
    {
        lock (gate)
        {
            current?.Stop();
            current = null;
        }
    }

    /// <summary>A form POST that asks for JSON, refuses redirects and is capped at <see cref="MaxBytes"/> and 15 s.</summary>
    public async Task<CappedReply?> PostAsync(Uri uri, string form, string what, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(form, Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.UserAgent.ParseAdd("Edgepad");
        return await CappedHttp.SendAsync(request, MaxBytes, Timeout, handler, what, ct).ConfigureAwait(false);
    }

    private static Poll ReadPoll(CappedReply? reply, Func<JsonElement, string, DeviceToken?> readToken)
    {
        if (reply is null)
        {
            return new Poll.Pending();
        }

        try
        {
            using var doc = JsonDocument.Parse(reply.Body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new Poll.End(SignInState.Failed);
            }

            if (reply.IsSuccess && GitJson.Text(root, "access_token") is { } token)
            {
                return readToken(root, token) is { } fine ? new Poll.Token(fine) : new Poll.End(SignInState.Failed);
            }

            return GitJson.Text(root, "error") switch
            {
                "authorization_pending" => new Poll.Pending(),
                "slow_down" => new Poll.SlowDown(),
                "expired_token" => new Poll.End(SignInState.Expired),
                "access_denied" => new Poll.End(SignInState.Denied),
                null when !reply.IsSuccess => new Poll.Pending(),
                _ => new Poll.End(SignInState.Failed),
            };
        }
        catch (JsonException)
        {
            // A server error page, not an answer: try again at the next interval, until the code runs out.
            return reply.IsSuccess ? new Poll.End(SignInState.Failed) : new Poll.Pending();
        }
    }

    private async Task SafeWait(TimeSpan span, CancellationToken ct)
    {
        try
        {
            await wait(span, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Read as Cancelled by the loop.
        }
    }

    /// <summary>A device code reply that passed every check.</summary>
    public sealed record IssuedCode(string DeviceCode, string UserCode, int Interval, int ExpiresIn);

    /// <summary>One answer to a poll.</summary>
    private abstract record Poll
    {
        public sealed record Pending : Poll;

        public sealed record SlowDown : Poll;

        public sealed record Token(DeviceToken Value) : Poll;

        public sealed record End(SignInState State) : Poll;
    }

    /// <summary>One sign-in: its device code, the host's interval, the moment the code runs out, and the host's epoch.</summary>
    private sealed class Flow(string deviceCode, int interval, DateTimeOffset deadline, long epoch)
    {
        public int Waiting;

        public long Epoch { get; } = epoch;

        public string DeviceCode { get; } = deviceCode;

        public int Interval { get; } = interval;

        public DateTimeOffset Deadline { get; } = deadline;

        public TaskCompletionSource Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Stop() => Stopped.TrySetResult();
    }
}
