using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Edgepad.Dashboard;

/// <summary>How saving a pasted token went. The windows (B7-B9) turn each into their own translated words; nothing here is English.</summary>
internal enum GitSaveStatus
{
    /// <summary>The host accepted the token; it and the address and name are saved.</summary>
    Saved,

    /// <summary>The token is empty, too long or has characters no token has. Nobody was asked.</summary>
    BadToken,

    /// <summary>The address is not an https address. Nobody was asked.</summary>
    BadUrl,

    /// <summary>The user name or e-mail is empty or unusable. Nobody was asked.</summary>
    BadUser,

    /// <summary>The host answered 401: the token is wrong, expired or revoked.</summary>
    Unauthorized,

    /// <summary>The host answered 403: the token lacks the read permission it needs.</summary>
    Forbidden,

    /// <summary>The host answered 404: the address is probably not that host's API.</summary>
    NotFound,

    /// <summary>The host could not be reached.</summary>
    Network,

    /// <summary>Anything else, including a reply that was not what that host sends or a token that could not be saved.</summary>
    Failed,
}

/// <summary>What <see cref="TokenGitHost.SaveAsync"/> found. <see cref="Login"/> is for display only and empty unless <see cref="Status"/> is <see cref="GitSaveStatus.Saved"/>.</summary>
internal readonly record struct GitSaveResult(GitSaveStatus Status, string Login);

/// <summary>One reply, judged. <see cref="Result"/> says how it went, <see cref="Status"/> is the HTTP status (null when nothing came back).</summary>
internal sealed record GitReply(GitHubResult Result, HttpStatusCode? Status, TimeSpan RetryAfter, IReadOnlyDictionary<string, string> Headers, byte[] Body)
{
    public static readonly GitReply Unusable = new(GitHubResult.Failed, null, TimeSpan.Zero, new Dictionary<string, string>(), []);
}

/// <summary>What a host read: the snapshot, or the reply that stopped it.</summary>
internal readonly record struct GitRead(GitSnapshot? Snapshot, GitReply? Failure);

/// <summary>Asks the host's API for one address (always GET) and judges the reply.</summary>
internal delegate Task<GitReply> GitGet(Uri uri, CancellationToken ct);

/// <summary>The names, limits and checks every token-pasting git host shares.</summary>
internal static class GitTokens
{
    public const int MaxToken = 512;
    public const int MaxUser = 254;
    public const int MaxUrl = 200;

    /// <summary>The <see cref="ISecretStore"/> name of the host's token.</summary>
    public static string TokenName(string id) => id + ".token";

    /// <summary>The <see cref="IntegrationSettings"/> key of the host's address (Gitea, Forgejo and Codeberg only). Not a secret.</summary>
    public static string UrlKey(string id) => id + "Url";

    /// <summary>The <see cref="IntegrationSettings"/> key of the host's user name or e-mail (Bitbucket only). Not a secret.</summary>
    public static string UserKey(string id) => id + "User";

    /// <summary>Visible ASCII only, 8 to 512 characters: every token these hosts issue.</summary>
    public static bool IsToken(string? token) =>
        token is { Length: >= 8 and <= MaxToken } && token.All(c => c is > ' ' and <= '~');

    /// <summary>Visible ASCII, no colon (it separates the name from the token in Basic authentication).</summary>
    public static bool IsUser(string? user) =>
        user is { Length: > 0 and <= MaxUser } && user.All(c => c is > ' ' and <= '~' and not ':');

    /// <summary>
    /// The API root (<c>https://host[:port][/path]/api/v1/</c>) of a Gitea-compatible server given its web address. Only an
    /// absolute https address with no user info, query or fragment passes; anything else is null.
    /// </summary>
    public static Uri? GiteaApi(string? url)
    {
        var text = url?.Trim();
        if (text is not { Length: > 0 and <= MaxUrl } || !Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.Host.Length == 0)
        {
            return null;
        }

        return new Uri(uri.GetLeftPart(UriPartial.Authority) + uri.AbsolutePath.TrimEnd('/') + "/api/v1/");
    }
}

/// <summary>Reading a git host's JSON without trusting it: every missing or wrongly typed field reads as absent.</summary>
internal static class GitJson
{
    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 12 };

    public static JsonDocument? Parse(byte[] body)
    {
        try
        {
            return JsonDocument.Parse(body, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? Text(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static int Whole(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)
            ? Math.Max(0, n)
            : 0;

    public static long Long(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)
            ? n
            : 0;

    public static bool Flag(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>The named child when it is an object, else an empty object-less element (every reader above then reports absent).</summary>
    public static JsonElement Child(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var v) ? v : default;

    /// <summary>The elements of the named array, or none.</summary>
    public static IEnumerable<JsonElement> Items(JsonElement parent, string name) =>
        Child(parent, name) is { ValueKind: JsonValueKind.Array } array ? array.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object) : [];

    /// <summary>The elements of a top-level array, or none.</summary>
    public static IEnumerable<JsonElement> Items(JsonElement array) =>
        array.ValueKind == JsonValueKind.Array ? array.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object) : [];

    /// <summary>UTC minutes since the Unix epoch of an ISO time; 0 when it does not read.</summary>
    public static long Minutes(string? time) =>
        DateTimeOffset.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? Math.Max(0, at.ToUnixTimeSeconds() / 60) : 0;

    /// <summary>The address when it is plain https on <paramref name="domain"/> (default port, no user info), else empty; the phone can then never open it.</summary>
    public static string Url(string? text, string domain) =>
        text is { Length: > 0 and <= GitLimits.MaxUrl } && Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0 && uri.IsDefaultPort
            && string.Equals(uri.Host, domain, StringComparison.OrdinalIgnoreCase)
                ? uri.AbsoluteUri
                : string.Empty;
}

/// <summary>
/// A git host that is read with a token the person pastes (Gitea, Forgejo, Codeberg, Bitbucket, Gitee). It only reads: every request
/// is a GET through the <see cref="ReadOnlyGate"/>. The token lives in the <see cref="ISecretStore"/> alone; it is read for each
/// refresh, goes only into the Authorization header of a request to the configured API, and is never logged, returned, kept in
/// <see cref="IntegrationSettings"/> or sent to the phone. Settings hold the host's address and the user name only. Redirects are
/// refused, a reply is capped at 1 MiB and 15 s, a refresh never overlaps another, and one refresh is one bounded list: a host that
/// fails part-way keeps the last good page.
/// </summary>
internal abstract class TokenGitHost(string id, ISecretStore store, IntegrationSettings settings, HttpMessageHandler? handler, Func<DateTimeOffset>? clock) : IGitHost
{
    public const int MaxBytes = 1024 * 1024;

    /// <summary>A rate limit is waited out for at least this long, and never longer than <see cref="MaxBackoff"/>, whatever the reply claims.</summary>
    public static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(60);

    public static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(1);

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly Lock gate = new();
    private readonly GitBook book = new(id);
    private DateTimeOffset backoffUntil;
    private int running;

    public string Id => id;

    /// <summary>The one web host name this host's links may open; empty when the address is not usable.</summary>
    public abstract string Domain { get; }

    public GitPage? Page => book.Page;

    /// <summary>Whether the host needs a user name or e-mail beside the token.</summary>
    protected virtual bool RequiresUser => false;

    /// <summary>The path (relative to the API root) that names the signed-in account, asked when a token is saved.</summary>
    protected abstract string UserPath { get; }

    /// <summary>The API root for a configured web address (ignored by hosts with a fixed API); null when unusable.</summary>
    protected abstract Uri? ApiFor(string configuredUrl);

    /// <summary>The Authorization header for this token (and name, for Basic).</summary>
    protected abstract AuthenticationHeaderValue Auth(string token, string user);

    /// <summary>The account's login from the <see cref="UserPath"/> reply; null when it is not one.</summary>
    protected abstract string? ParseLogin(byte[] body);

    /// <summary>Reads everything one refresh sends the phone with GETs under <paramref name="api"/>.</summary>
    protected abstract Task<GitRead> ReadAsync(GitGet get, Uri api, CancellationToken ct);

    /// <summary>The configured web address, empty when none is saved.</summary>
    protected string ConfiguredUrl => settings.Text(GitTokens.UrlKey(id));

    /// <summary>The API root from the saved settings; null when it is unusable.</summary>
    protected Uri? Api => ApiFor(ConfiguredUrl);

    public bool IsSignedIn => GitTokens.IsToken(store.Get(GitTokens.TokenName(id))) && Api is not null && (!RequiresUser || GitTokens.IsUser(settings.Text(GitTokens.UserKey(id))));

    /// <summary>Asks the host for the account, repositories, pull requests and issues. Never throws but on cancellation.</summary>
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

            var token = store.Get(GitTokens.TokenName(id));
            var user = settings.Text(GitTokens.UserKey(id));
            if (token is null || !GitTokens.IsToken(token) || Api is not { } api || (RequiresUser && !GitTokens.IsUser(user)))
            {
                return new GitHubRefreshResult(GitHubResult.NotSignedIn, false);
            }

            var before = book.Page?.Rev;
            var read = await ReadAsync(Getter(token, user), api, ct).ConfigureAwait(false);
            if (read.Snapshot is { } snapshot)
            {
                book.Update(snapshot);
                return new GitHubRefreshResult(GitHubResult.Ok, book.Page?.Rev != before);
            }

            var failure = read.Failure ?? GitReply.Unusable;
            if (failure.Status == HttpStatusCode.Unauthorized)
            {
                store.Delete(GitTokens.TokenName(id));
                book.Clear();
            }
            else if (failure.Result == GitHubResult.RateLimited)
            {
                lock (gate)
                {
                    backoffUntil = now() + failure.RetryAfter;
                }
            }

            return new GitHubRefreshResult(failure.Result, book.Page?.Rev != before);
        }
        finally
        {
            Volatile.Write(ref running, 0);
        }
    }

    /// <summary>
    /// Checks a pasted token with one GET of the account and, only when the host accepts it, saves the address and user name in the
    /// settings and then the token in the secret store (in that order, so a token is never saved beside a wrong address). Nothing is
    /// saved otherwise. Never throws but on cancellation.
    /// </summary>
    public async Task<GitSaveResult> SaveAsync(string token, string url, string user, CancellationToken ct)
    {
        var secret = token.Trim();
        var name = user.Trim();
        if (!GitTokens.IsToken(secret))
        {
            return new GitSaveResult(GitSaveStatus.BadToken, string.Empty);
        }

        if (ApiFor(url) is not { } api)
        {
            return new GitSaveResult(GitSaveStatus.BadUrl, string.Empty);
        }

        if (RequiresUser && !GitTokens.IsUser(name))
        {
            return new GitSaveResult(GitSaveStatus.BadUser, string.Empty);
        }

        var reply = await Getter(secret, name)(new Uri(api, UserPath), ct).ConfigureAwait(false);
        if (reply.Result != GitHubResult.Ok)
        {
            return new GitSaveResult(reply.Status switch
            {
                HttpStatusCode.Unauthorized => GitSaveStatus.Unauthorized,
                HttpStatusCode.Forbidden => GitSaveStatus.Forbidden,
                HttpStatusCode.NotFound => GitSaveStatus.NotFound,
                null => GitSaveStatus.Network,
                _ => GitSaveStatus.Failed,
            }, string.Empty);
        }

        if (ParseLogin(reply.Body) is not { } login)
        {
            return new GitSaveResult(GitSaveStatus.Failed, string.Empty);
        }

        var keep = url.Trim().TrimEnd('/');
        if (!settings.Set(GitTokens.UrlKey(id), ApiIsFixed ? string.Empty : keep)
            || !settings.Set(GitTokens.UserKey(id), RequiresUser ? name : string.Empty)
            || !store.Set(GitTokens.TokenName(id), secret))
        {
            return new GitSaveResult(GitSaveStatus.Failed, string.Empty);
        }

        book.Clear();
        return new GitSaveResult(GitSaveStatus.Saved, login);
    }

    /// <summary>Forgets the token and the page; the address and name stay for the next sign-in.</summary>
    public void SignOut()
    {
        store.Delete(GitTokens.TokenName(id));
        book.Clear();
    }

    /// <summary>Whether the API root never depends on the configured address.</summary>
    protected virtual bool ApiIsFixed => true;

    /// <summary>Reads each reply's judgement into a <see cref="GitReply"/>: 401 and 403 are refusals, 429 (or 403 with Retry-After) a rate limit, any other failure is Failed.</summary>
    private static GitReply Judge(CappedReply? reply)
    {
        if (reply is null)
        {
            return GitReply.Unusable;
        }

        var retry = long.TryParse(reply.Header("Retry-After"), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(Math.Min(seconds, (long)MaxBackoff.TotalSeconds))
            : (TimeSpan?)null;
        var wait = retry is { } r && r > MinBackoff ? r : MinBackoff;
        var result = reply.IsSuccess
            ? GitHubResult.Ok
            : reply.Status switch
            {
                HttpStatusCode.Unauthorized => GitHubResult.NoPermission,
                HttpStatusCode.TooManyRequests => GitHubResult.RateLimited,
                HttpStatusCode.Forbidden => retry is null ? GitHubResult.NoPermission : GitHubResult.RateLimited,
                _ => GitHubResult.Failed,
            };
        return new GitReply(result, reply.Status, wait, reply.Headers, reply.Body);
    }

    private GitGet Getter(string token, string user) => async (uri, ct) =>
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = Auth(token, user);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.UserAgent.ParseAdd("Edgepad");
        return Judge(await CappedHttp.SendAsync(request, MaxBytes, Timeout, handler, "Asking a git host", ct).ConfigureAwait(false));
    };

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
}
