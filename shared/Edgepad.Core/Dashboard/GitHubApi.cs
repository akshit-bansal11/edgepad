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

    /// <summary>GitHub refused the token (401). It has been deleted; the screen shows "github:no-permission".</summary>
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
/// repositories, and the pull requests and issues that are the user's, wait for the user's review or are assigned to the user)
/// and one REST call for the unread notification count, kept by ETag. It only reads: every request goes through the
/// <see cref="ReadOnlyGate"/>. The token is read from the <see cref="ISecretStore"/> for each refresh and goes only into the
/// Authorization header of these requests; it is never held, logged, or put in anything the phone receives. Redirects are
/// refused, a reply is capped at 1 MiB and 15 s, and refreshes never overlap.
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
    public static readonly Uri NotificationsUri = new("https://api.github.com/notifications?per_page=50");
    public static readonly Uri UserUri = new("https://api.github.com/user");

    /// <summary>A rate limit is waited out for at least this long, and never longer than <see cref="MaxBackoff"/>, whatever the reply claims.</summary>
    public static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(60);

    public static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(1);

    private const int MaxETag = 200;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly Lock gate = new();

    private readonly GitBook book = new("github");

    private GitSnapshot? snapshot;
    private int? unread;
    private string? etag;
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

    /// <summary>Asks for the account, repositories, pull requests and issues, then the notification count. Never throws but on cancellation.</summary>
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
            if (result is GitHubResult.Ok or GitHubResult.Failed)
            {
                var notifications = await RefreshUnreadAsync(ct).ConfigureAwait(false);
                result = result == GitHubResult.Ok || notifications is not (GitHubResult.Ok or GitHubResult.Failed) ? notifications : result;
            }

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
        using var request = Request(HttpMethod.Get, UserUri);
        if (request is null)
        {
            return null;
        }

        var reply = await CappedHttp.SendAsync(request, MaxBytes, Timeout, handler, "Asking GitHub who is signed in", ct).ConfigureAwait(false);
        return Judge(reply) == GitHubResult.Ok && reply is not null ? GitHubParse.Login(reply.Body) : null;
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
        using var request = Request(HttpMethod.Post, GraphQlUri);
        if (request is null)
        {
            return GitHubResult.NotSignedIn;
        }

        request.Content = new ByteArrayContent(QueryBody()) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } };
        var reply = await CappedHttp.SendAsync(request, MaxBytes, Timeout, handler, "Asking GitHub for pull requests", ct).ConfigureAwait(false);
        var result = Judge(reply);
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

    /// <summary>Makes the page from the last good snapshot and unread count; a revision is spent only when what the phone sees changed.</summary>
    private void Rebuild()
    {
        GitSnapshot? current;
        int? count;
        lock (gate)
        {
            current = snapshot;
            count = unread;
        }

        if (current is not null)
        {
            book.Update(current with { Unread = count });
        }
    }

    private async Task<GitHubResult> RefreshUnreadAsync(CancellationToken ct)
    {
        using var request = Request(HttpMethod.Get, NotificationsUri);
        if (request is null)
        {
            return GitHubResult.NotSignedIn;
        }

        string? known;
        lock (gate)
        {
            known = unread is null ? null : etag;
        }

        if (known is not null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", known);
        }

        var reply = await CappedHttp.SendAsync(request, MaxBytes, Timeout, handler, "Asking GitHub for notifications", ct).ConfigureAwait(false);
        if (reply?.Status == HttpStatusCode.NotModified)
        {
            return GitHubResult.Ok;
        }

        var result = Judge(reply);
        if (result != GitHubResult.Ok || reply is null)
        {
            return result;
        }

        if (GitHubParse.Unread(reply.Body) is not int count)
        {
            return GitHubResult.Failed;
        }

        lock (gate)
        {
            unread = count;
            etag = reply.Header("ETag") is { Length: > 0 and <= MaxETag } tag && tag.All(c => c is >= ' ' and <= '~') ? tag : null;
        }

        return GitHubResult.Ok;
    }

    /// <summary>A request carrying the saved token; null when there is no usable one.</summary>
    private HttpRequestMessage? Request(HttpMethod method, Uri uri)
    {
        var token = store.Get(GitHubApp.TokenName);
        if (!GitHubApp.IsToken(token))
        {
            return null;
        }

        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.UserAgent.ParseAdd("Edgepad");
        return request;
    }

    /// <summary>Success is Ok; 401 deletes the token; 403 or 429 with rate-limit headers starts a back-off; anything else is Failed.</summary>
    private GitHubResult Judge(CappedReply? reply)
    {
        if (reply is null)
        {
            return GitHubResult.Failed;
        }

        if (reply.IsSuccess)
        {
            return GitHubResult.Ok;
        }

        if (reply.Status == HttpStatusCode.Unauthorized)
        {
            store.Delete(GitHubApp.TokenName);
            lock (gate)
            {
                snapshot = null;
                unread = null;
                etag = null;
            }

            book.Clear();

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
