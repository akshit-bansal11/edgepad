using System.Text;
using System.Text.Json;

namespace Edgepad.Dashboard;

/// <summary>
/// gitlab.com sign-in by the OAuth device authorization grant (https://docs.gitlab.com/api/oauth2/): ask for a code with
/// <see cref="GitLabApp.ClientId"/> and the scopes <see cref="GitLabApp.Scope"/>, show it, and poll until the person has entered it at
/// <see cref="GitLabApp.DevicePage"/>. The tokens go straight into the <see cref="ISecretStore"/>; they are never logged, never
/// returned and never sent to the phone. Only gitlab.com is asked, redirects are refused, every reply is capped at 16 KiB and 15 s,
/// and there is no client secret anywhere. One flow at a time: starting another cancels the first. A self-hosted server does not
/// use this: it takes a pasted token (see <see cref="GitLabHost.SaveServerAsync"/>).
/// </summary>
internal sealed class GitLabSignIn(
    ISecretStore store,
    IntegrationSettings settings,
    HttpMessageHandler? handler = null,
    Func<DateTimeOffset>? clock = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private const int MaxLifeSeconds = 31 * 24 * 3600;
    private const string GrantType = "urn:ietf:params:oauth:grant-type:device_code";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly Func<TimeSpan, CancellationToken, Task> wait = delay ?? ((span, token) => Task.Delay(span, token));
    private readonly Lock gate = new();
    private Flow? current;

    /// <summary>Whether a usable token is saved (from this sign-in or pasted for a server).</summary>
    public bool IsSignedIn => GitTokens.IsToken(store.Get(GitLabApp.TokenName));

    /// <summary>The form body of the device request: this application and the two read scopes, spaces written as <c>+</c> as GitLab's docs do.</summary>
    public static string DeviceForm() =>
        $"client_id={Uri.EscapeDataString(GitLabApp.ClientId)}&scope={Uri.EscapeDataString(GitLabApp.Scope).Replace("%20", "+", StringComparison.Ordinal)}";

    /// <summary>Asks gitlab.com for a code. <see cref="SignInState.Failed"/> when it cannot be asked or answers outside the documented ranges.</summary>
    public async Task<DeviceCode> StartAsync(CancellationToken ct)
    {
        Cancel();
        var reply = await PostAsync(GitLabApp.DeviceCodeUri, DeviceForm(), "Asking GitLab for a sign-in code", ct).ConfigureAwait(false);
        if (reply is not { IsSuccess: true } || GitHubSignIn.ParseDeviceCode(reply.Body) is not { } issued)
        {
            return new DeviceCode(SignInState.Failed, string.Empty, GitLabApp.DevicePage, TimeSpan.Zero);
        }

        var flow = new Flow(issued.DeviceCode, issued.Interval, now() + TimeSpan.FromSeconds(issued.ExpiresIn));
        lock (gate)
        {
            current?.Stop();
            current = flow;
        }

        return new DeviceCode(SignInState.Started, issued.UserCode, GitLabApp.DevicePage, TimeSpan.FromSeconds(issued.ExpiresIn));
    }

    /// <summary>
    /// Polls until the person has entered the code, said no, or the code ran out, honouring GitLab's interval and its slow_down. Never
    /// throws: cancelling <paramref name="ct"/> or calling <see cref="Cancel"/> ends it as <see cref="SignInState.Cancelled"/>.
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
            var form = $"client_id={Uri.EscapeDataString(GitLabApp.ClientId)}&device_code={Uri.EscapeDataString(flow.DeviceCode)}&grant_type={Uri.EscapeDataString(GrantType)}";
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

                var reply = await PostAsync(GitLabApp.TokenUri, form, "Waiting for GitLab sign-in", ct).ConfigureAwait(false);
                if (flow.Stopped.Task.IsCompleted)
                {
                    return SignInState.Cancelled;
                }

                switch (ReadPoll(reply))
                {
                    case Poll.Token token:
                        return Save(token) ? SignInState.SignedIn : SignInState.Failed;
                    case Poll.Pending:
                        break;
                    case Poll.SlowDown:
                        interval += GitHubSignIn.SlowDownSeconds;
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

            if (reply.IsSuccess && GitJson.Text(root, "access_token") is { } token)
            {
                return ReadToken(root, token);
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

    /// <summary>
    /// A token reply: a refresh token needs an expires_in (1 s..31 days) beside it. A reply without a refresh token is kept as a token
    /// that is used until GitLab refuses it. A value outside the ranges is a failure.
    /// </summary>
    private static Poll ReadToken(JsonElement root, string token)
    {
        var refresh = GitJson.Text(root, "refresh_token");
        var life = GitJson.Whole(root, "expires_in");
        var fine = GitTokens.IsToken(token) && (refresh is null || (GitTokens.IsToken(refresh) && life is > 0 and <= MaxLifeSeconds));
        return fine ? new Poll.Token(token, refresh, life) : new Poll.End(SignInState.Failed);
    }

    /// <summary>Saves the tokens for gitlab.com: the server address goes first, so a token is never saved beside a self-hosted address.</summary>
    private bool Save(Poll.Token token) =>
        settings.Set(GitTokens.UrlKey("gitlab"), string.Empty)
        && GitLabApp.Save(store, GitLabApp.ClientId, token.Value, token.Refresh, token.Refresh is null ? null : now() + TimeSpan.FromSeconds(token.ExpiresIn));

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
        return await CappedHttp.SendAsync(request, GitHubSignIn.MaxBytes, Timeout, handler, what, ct).ConfigureAwait(false);
    }

    /// <summary>One answer to a poll.</summary>
    private abstract record Poll
    {
        public sealed record Pending : Poll;

        public sealed record SlowDown : Poll;

        public sealed record Token(string Value, string? Refresh, int ExpiresIn) : Poll;

        public sealed record End(SignInState State) : Poll;
    }

    /// <summary>One sign-in: its device code, GitLab's interval and the moment the code runs out.</summary>
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
