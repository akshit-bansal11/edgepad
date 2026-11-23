using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Edgepad.Dashboard;

/// <summary>How a GitHub refresh went.</summary>
internal enum GitHubResult
{
    /// <summary>Asked and read.</summary>
    Ok,

    /// <summary>No usable token is saved; nobody was asked.</summary>
    NotSignedIn,

    /// <summary>GitHub refused the token (401) and it could not be renewed. It has been deleted; the screen shows "github:no-permission".</summary>
    NoPermission,

    /// <summary>GitHub's rate limit; the last result is kept and nothing is asked until the limit resets.</summary>
    RateLimited,

    /// <summary>Anything else; the last good result is kept.</summary>
    Failed,

    /// <summary>A refresh was already running; this one did nothing.</summary>
    Busy,
}

/// <summary>A refresh's outcome, and whether <see cref="GitHubApi.Page"/> changed so the blob is worth sending again.</summary>
internal readonly record struct GitHubRefreshResult(GitHubResult Result, bool Changed);

/// <summary>When the GitHub screen may be refreshed: only while the phone shows it (DASH_WATCH bit4), at least 60 s apart.</summary>
internal static class GitHubRefresh
{
    public static readonly TimeSpan Every = TimeSpan.FromSeconds(60);

    public static bool ShouldRun(DateTimeOffset? last, DateTimeOffset now, bool watchBit4) =>
        watchBit4 && (last is null || now - last.Value >= Every);
}

/// <summary>
/// The GitHub screen's data, over https from api.github.com only: one GraphQL query that never changes (the account, its newest
/// repositories, and the pull requests and issues that are the user's, wait for the user's review or are assigned to the user).
/// It only reads: every request goes through the <see cref="ReadOnlyGate"/>. The token is a GitHub App user token (read-only, and
/// limited to what the App is installed on); it is read from the <see cref="ISecretStore"/> for each request and goes only into the
/// Authorization header of these requests; it is never held, logged, or put in anything the phone receives. An expiring token is
/// renewed shortly before its end, and once after a 401, by <see cref="GitHubTokenRefresh"/>; when that fails the token is deleted
/// and the screen says so. Redirects are refused, a reply is capped at 1 MiB and 15 s, and refreshes never overlap. Notifications
/// are not asked: GitHub gives them only to a classic personal access token.
/// </summary>
internal sealed class GitHubApi(ISecretStore store, HttpMessageHandler? handler = null, Func<DateTimeOffset>? clock = null) : IGitHost
{
    public const int MaxBytes = 1024 * 1024;

    /// <summary>The one GraphQL query this app sends. Constant: nothing from the user, the phone or a reply is ever put into it, and it has no mutation.</summary>
    public const string Query = """
        query {
          viewer {
            login
            name
            followers { totalCount }
            repositories(first: 30, ownerAffiliations: OWNER, orderBy: { field: PUSHED_AT, direction: DESC }) {
              totalCount
              nodes {
                nameWithOwner
                isPrivate
                url
                stargazerCount
                primaryLanguage { name }
                issues(states: OPEN) { totalCount }
                pullRequests(states: OPEN) { totalCount }
              }
            }
          }
          mine: search(query: "is:open archived:false author:@me sort:updated-desc", type: ISSUE, first: 20) { nodes { ...item } }
          review: search(query: "is:pr is:open archived:false review-requested:@me sort:updated-desc", type: ISSUE, first: 20) { nodes { ...item } }
          assigned: search(query: "is:open archived:false assignee:@me sort:updated-desc", type: ISSUE, first: 20) { nodes { ...item } }
        }
        fragment item on SearchResultItem {
          __typename
          ... on PullRequest {
            number
            title
            isDraft
            state
            url
            updatedAt
            repository { nameWithOwner }
            commits(last: 1) { nodes { commit { statusCheckRollup { state } } } }
          }
          ... on Issue {
            number
            title
            state
            url
            updatedAt
            repository { nameWithOwner }
          }
        }
        """;

    public static readonly Uri GraphQlUri = new("https://api.github.com/graphql");
    public static readonly Uri UserUri = new("https://api.github.com/user");

    /// <summary>A rate limit is waited out for at least this long, and never longer than <see cref="MaxBackoff"/>, whatever the reply claims.</summary>
    public static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(60);

    public static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(1);

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly Lock gate = new();

    private readonly GitBook book = new("github", GitHubApp.InstallUri.AbsoluteUri);
    private readonly GitHubTokenRefresh renewal = new(store, GitHubApp.ClientId, handler, clock ?? (() => DateTimeOffset.UtcNow));

    private GitSnapshot? snapshot;
    private DateTimeOffset backoffUntil;
    private int running;

    public string Id => "github";

    public string Domain => GitHubParse.Domain;

    /// <summary>The last good result as BLOB GIT and the addresses behind its indexes; null before the first one and after a refused token.</summary>
    public GitPage? Page => book.Page;

    /// <summary>The request body: <c>{"query": Query}</c>.</summary>
    public static byte[] QueryBody()
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("query", Query);
            json.WriteEndObject();
        }

        return buffer.ToArray();
    }

    /// <summary>Asks for the account, repositories, pull requests and issues. Never throws but on cancellation.</summary>
    public async Task<GitHubRefreshResult> RefreshAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref running, 1) == 1)
        {
            return new GitHubRefreshResult(GitHubResult.Busy, false);
        }

        try
        {
            if (now() < Backoff)
            {
                return new GitHubRefreshResult(GitHubResult.RateLimited, false);
            }

            var before = book.Page?.Rev;
            var result = await RefreshPullsAsync(ct).ConfigureAwait(false);
            Rebuild();
            return new GitHubRefreshResult(result, book.Page?.Rev != before);
        }
        finally
        {
            Volatile.Write(ref running, 0);
        }
    }

    /// <summary>The signed-in user's login, for display only; null when it cannot be read. A refused token is deleted.</summary>
    public async Task<string?> LoginAsync(CancellationToken ct)
    {
        var (result, reply) = await CallAsync(HttpMethod.Get, UserUri, null, "Asking GitHub who is signed in", ct).ConfigureAwait(false);
        return result == GitHubResult.Ok && reply is not null ? GitHubParse.Login(reply.Body) : null;
    }

    private DateTimeOffset Backoff
    {
        get
        {
            lock (gate)
            {
                return backoffUntil;
            }
        }
    }

    private async Task<GitHubResult> RefreshPullsAsync(CancellationToken ct)
    {
        var (result, reply) = await CallAsync(HttpMethod.Post, GraphQlUri, QueryBody(), "Asking GitHub for pull requests", ct).ConfigureAwait(false);
        if (result != GitHubResult.Ok || reply is null)
        {
            return result;
        }

        if (GitHubParse.Snapshot(reply.Body) is not { } parsed)
        {
            return GitHubResult.Failed;
        }

        lock (gate)
        {
            snapshot = parsed;
        }

        return GitHubResult.Ok;
    }

    /// <summary>Makes the page from the last good snapshot; a revision is spent only when what the phone sees changed.</summary>
    private void Rebuild()
    {
        GitSnapshot? current;
        lock (gate)
        {
            current = snapshot;
        }

        if (current is not null)
        {
            book.Update(current);
        }
    }

    private void ClearScreen()
    {
        lock (gate)
        {
            snapshot = null;
        }

        book.Clear();
    }

    /// <summary>
    /// One request with the saved token: the token is renewed first when it is about to expire, and the request is sent once more with
    /// a new token after a 401 when a renewal makes one. A token from the old OAuth app is deleted first (<see cref="GitHubApp.Migrate"/>).
    /// </summary>
    private async Task<(GitHubResult Result, CappedReply? Reply)> CallAsync(HttpMethod method, Uri uri, byte[]? body, string what, CancellationToken ct)
    {
        GitHubApp.Migrate(store, GitHubApp.ClientId);
        if (renewal.IsDue() && await renewal.RefreshAsync(ct).ConfigureAwait(false) == RefreshOutcome.Rejected)
        {
            ClearScreen();
            return (GitHubResult.NoPermission, null);
        }

        for (var attempt = 0; ; attempt++)
        {
            var token = store.Get(GitHubApp.TokenName);
            if (!GitHubApp.IsToken(token))
            {
                return (GitHubResult.NotSignedIn, null);
            }

            using var request = new HttpRequestMessage(method, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
            request.Headers.UserAgent.ParseAdd("Edgepad");
            if (body is not null)
            {
                request.Content = new ByteArrayContent(body) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } };
            }

            var reply = await CappedHttp.SendAsync(request, MaxBytes, Timeout, handler, what, ct).ConfigureAwait(false);
            if (reply?.Status == HttpStatusCode.Unauthorized && attempt == 0 && renewal.CanRefresh && store.Get(GitHubApp.TokenName) == token)
            {
                var outcome = await renewal.RefreshAsync(ct).ConfigureAwait(false);
                if (outcome == RefreshOutcome.Refreshed)
                {
                    continue;
                }

                if (outcome == RefreshOutcome.Unavailable)
                {
                    return (GitHubResult.Failed, reply);
                }
            }

            return (Judge(reply, token), reply);
        }
    }

    /// <summary>Success is Ok; 401 deletes the token; 403 or 429 with rate-limit headers starts a back-off; anything else is Failed.</summary>
    private GitHubResult Judge(CappedReply? reply, string sent)
    {
        if (reply is null)
        {
            return GitHubResult.Failed;
        }

        if (reply.IsSuccess)
        {
            return GitHubResult.Ok;
        }

        // Only the token this request carried goes: one saved by a sign-in while it was out is a different one.
        if (reply.Status == HttpStatusCode.Unauthorized)
        {
            var saved = store.Get(GitHubApp.TokenName);
            if (saved is null || saved == sent)
            {
                GitHubApp.Forget(store);
                ClearScreen();
            }

            return GitHubResult.NoPermission;
        }

        if (reply.Status is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests && RateLimitEnd(reply) is { } end)
        {
            lock (gate)
            {
                backoffUntil = end;
            }

            return GitHubResult.RateLimited;
        }

        return GitHubResult.Failed;
    }

    /// <summary>When a rate-limited reply says the limit resets (Retry-After seconds, else x-ratelimit-reset epoch seconds), clamped to 1 min..1 h; null when it is not a rate limit.</summary>
    private DateTimeOffset? RateLimitEnd(CappedReply reply)
    {
        var at = now();
        DateTimeOffset target;
        if (long.TryParse(reply.Header("Retry-After"), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            target = at + TimeSpan.FromSeconds(Math.Min(seconds, (long)MaxBackoff.TotalSeconds));
        }
        else if (reply.Header("x-ratelimit-remaining") == "0")
        {
            target = long.TryParse(reply.Header("x-ratelimit-reset"), NumberStyles.None, CultureInfo.InvariantCulture, out var reset)
                && reset < DateTimeOffset.MaxValue.ToUnixTimeSeconds()
                    ? DateTimeOffset.FromUnixTimeSeconds(reset)
                    : at;
        }
        else
        {
            return null;
        }

        return target < at + MinBackoff ? at + MinBackoff : target > at + MaxBackoff ? at + MaxBackoff : target;
    }
}
