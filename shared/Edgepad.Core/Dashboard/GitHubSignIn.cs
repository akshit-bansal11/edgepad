using System.Text;
using System.Text.Json;

namespace Edgepad.Dashboard;

/// <summary>Where a sign-in stands.</summary>
public enum SignInState
{
    /// <summary>A code was issued; show it and wait.</summary>
    Started,

    /// <summary>The token is saved.</summary>
    SignedIn,

    /// <summary>No Client ID was given (an empty or malformed one); nobody was asked.</summary>
    NotConfigured,

    /// <summary>The user said no on GitHub.</summary>
    Denied,

    /// <summary>The code ran out before the user entered it.</summary>
    Expired,

    /// <summary>Stopped here, or replaced by a newer sign-in.</summary>
    Cancelled,

    /// <summary>GitHub could not be asked, answered something unusable, or the token could not be saved.</summary>
    Failed,
}

/// <summary>What <see cref="GitHubSignIn.StartAsync"/> gives the window: the code to show and the page to open, only when <see cref="State"/> is <see cref="SignInState.Started"/>.</summary>
internal sealed record DeviceCode(SignInState State, string UserCode, Uri Page, TimeSpan ExpiresIn)
{
    public static DeviceCode Not(SignInState state) => new(state, string.Empty, GitHubApp.DevicePage, TimeSpan.Zero);
}

/// <summary>
/// GitHub sign-in by the OAuth device flow (GitHub docs, "Authorizing OAuth apps", device flow): ask github.com for a code,
/// show it, and poll until the user has entered it at https://github.com/login/device. The token goes straight into the
/// <see cref="ISecretStore"/> under <see cref="GitHubApp.TokenName"/>; it is never logged, never returned, and never sent to
/// the phone. github.com is the only host asked, redirects are refused, every reply is capped at 16 KiB and 15 s, and there
/// is no client secret anywhere. One flow at a time: starting another cancels the first.
/// </summary>
internal sealed class GitHubSignIn(
    ISecretStore store,
    string clientId = GitHubApp.ClientId,
    HttpMessageHandler? handler = null,
    Func<DateTimeOffset>? clock = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    public const int MaxBytes = 16 * 1024;
    public const int SlowDownSeconds = 5;

    private const int DefaultInterval = 5;
    private const int MaxDeviceCode = 255;
    private const string GrantType = "urn:ietf:params:oauth:grant-type:device_code";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly Func<TimeSpan, CancellationToken, Task> wait = delay ?? ((span, token) => Task.Delay(span, token));
    private readonly Lock gate = new();
    private Flow? current;

    /// <summary>Whether a usable token is saved.</summary>
    public bool IsSignedIn => GitHubApp.IsToken(store.Get(GitHubApp.TokenName));

    /// <summary>
    /// Asks GitHub for a code. <see cref="SignInState.NotConfigured"/> without a request while the Client ID is the
    /// placeholder; <see cref="SignInState.Failed"/> when GitHub cannot be asked or answers outside the documented ranges.
    /// </summary>
    public async Task<DeviceCode> StartAsync(bool includePrivate, CancellationToken ct)
    {
        if (!GitHubApp.IsConfigured(clientId))
        {
            return DeviceCode.Not(SignInState.NotConfigured);
        }

        Cancel();
        var scope = includePrivate ? GitHubApp.ScopePrivate : GitHubApp.ScopePublic;
        var reply = await PostAsync(GitHubApp.DeviceCodeUri, $"client_id={Uri.EscapeDataString(clientId)}&scope={Uri.EscapeDataString(scope)}", "Asking GitHub for a sign-in code", ct).ConfigureAwait(false);
        if (reply is not { IsSuccess: true } || ParseDeviceCode(reply.Body) is not { } issued)
        {
            return DeviceCode.Not(SignInState.Failed);
        }

        var flow = new Flow(issued.DeviceCode, issued.Interval, now() + TimeSpan.FromSeconds(issued.ExpiresIn));
        lock (gate)
        {
            current?.Stop();
            current = flow;
        }

        return new DeviceCode(SignInState.Started, issued.UserCode, GitHubApp.DevicePage, TimeSpan.FromSeconds(issued.ExpiresIn));
    }

    /// <summary>
    /// Polls until the user has entered the code, said no, or the code ran out, honouring GitHub's interval and its
    /// slow_down. Never throws: cancelling <paramref name="ct"/> or calling <see cref="Cancel"/> ends it as
    /// <see cref="SignInState.Cancelled"/>. A failed poll is tried again at the next interval until the code runs out.
    /// </summary>
    public async Task<SignInState> WaitAsync(CancellationToken ct)
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
            var form = $"client_id={Uri.EscapeDataString(clientId)}&device_code={Uri.EscapeDataString(flow.DeviceCode)}&grant_type={Uri.EscapeDataString(GrantType)}";
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

                var reply = await PostAsync(GitHubApp.TokenUri, form, "Waiting for GitHub sign-in", ct).ConfigureAwait(false);
                if (flow.Stopped.Task.IsCompleted)
                {
                    return SignInState.Cancelled;
                }

                switch (ReadPoll(reply))
                {
                    case Poll.Token token:
                        return store.Set(GitHubApp.TokenName, token.Value) ? SignInState.SignedIn : SignInState.Failed;
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

    /// <summary>Stops any sign-in and forgets the token.</summary>
    public void SignOut()
    {
        Cancel();
        store.Delete(GitHubApp.TokenName);
    }

    /// <summary>
    /// The device code reply: device_code (1..255 printable ASCII), user_code matching <c>^[A-Z0-9-]{4,20}$</c>, interval 1..60
    /// (5 when absent) and expires_in 60..1800 seconds. verification_uri is never read: the page shown is <see cref="GitHubApp.DevicePage"/>.
    /// </summary>
    internal static IssuedCode? ParseDeviceCode(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var deviceCode = Text(root, "device_code");
            var userCode = Text(root, "user_code");
            var expiresIn = Whole(root, "expires_in");
            var interval = root.TryGetProperty("interval", out _) ? Whole(root, "interval") : DefaultInterval;
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

    private static Poll ReadPoll(CappedReply? reply)
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

            if (reply.IsSuccess && Text(root, "access_token") is { } token)
            {
                return GitHubApp.IsToken(token) ? new Poll.Token(token) : new Poll.End(SignInState.Failed);
            }

            return Text(root, "error") switch
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

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

    private static int? Whole(JsonElement root, string name) =>
        root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n) ? n : null;

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

    private async Task<CappedReply?> PostAsync(Uri uri, string form, string what, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(form, Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.UserAgent.ParseAdd("Edgepad");
        return await CappedHttp.SendAsync(request, MaxBytes, Timeout, handler, what, ct).ConfigureAwait(false);
    }

    /// <summary>A device code reply that passed every check.</summary>
    internal sealed record IssuedCode(string DeviceCode, string UserCode, int Interval, int ExpiresIn);

    /// <summary>One answer to a poll.</summary>
    private abstract record Poll
    {
        public sealed record Pending : Poll;

        public sealed record SlowDown : Poll;

        public sealed record Token(string Value) : Poll;

        public sealed record End(SignInState State) : Poll;
    }

    /// <summary>One sign-in: its device code, GitHub's interval and the moment the code runs out.</summary>
    private sealed class Flow(string deviceCode, int interval, DateTimeOffset deadline)
    {
        public int Waiting;

        public string DeviceCode { get; } = deviceCode;

        public int Interval { get; } = interval;

        public DateTimeOffset Deadline { get; } = deadline;

        public TaskCompletionSource Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Stop() => Stopped.TrySetResult();
    }
}
