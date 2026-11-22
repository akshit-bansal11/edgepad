using System.Globalization;
using System.Text.Json;
using Edgepad.Protocol;

namespace Edgepad.Dashboard;

/// <summary>
/// Reads GitHub's replies into a <see cref="GitSnapshot"/>. Everything here came from the network: every string is cleaned and cut, a
/// repository that is not plain <c>owner/name</c> drops its row, an address that is not https on github.com is kept as empty, and a
/// reply that does not read gives null, never a throw.
/// </summary>
internal static class GitHubParse
{
    /// <summary>The GraphQL reply nests ten deep (data, alias, nodes, node, commits, nodes, node, commit, rollup); two to spare.</summary>
    public const int MaxDepth = 12;

    public const int MaxTitle = 80;
    public const int MaxUnread = 50;

    private const int MaxRepoPart = 100;
    private const int MaxLogin = 39;

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = MaxDepth };

    /// <summary>The web host every address kept from a reply must be on.</summary>
    public const string Domain = "github.com";

    /// <summary>
    /// The account, its newest repositories and its pull requests and issues, from the reply to <see cref="GitHubApi.Query"/>; null
    /// when the reply carries no data or no viewer. A row that does not read is dropped, never the whole reply.
    /// </summary>
    public static GitSnapshot? Snapshot(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body, Options);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("viewer", out var viewer) || viewer.ValueKind != JsonValueKind.Object
                || Text(viewer, "login") is not { } login || !IsLogin(login))
            {
                return null;
            }

            var repos = new List<GitRepo>();
            var repoTotal = 0;
            if (viewer.TryGetProperty("repositories", out var repositories) && repositories.ValueKind == JsonValueKind.Object)
            {
                repoTotal = Count(repositories);
                foreach (var node in Nodes(repositories))
                {
                    if (repos.Count < GitLimits.MaxRepos && Repo(node) is { } repo)
                    {
                        repos.Add(repo);
                    }
                }
            }

            var followers = viewer.TryGetProperty("followers", out var f) && f.ValueKind == JsonValueKind.Object ? Count(f) : 0;
            var items = new List<GitItem>();
            AddItems(items, data, "mine", GitSection.Mine);
            AddItems(items, data, "review", GitSection.Review);
            AddItems(items, data, "assigned", GitSection.Assigned);
            return new GitSnapshot(new GitProfile(login, DashWire.Clean(Text(viewer, "name") ?? string.Empty, GitLimits.MaxName), repoTotal, followers), repos, items);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>How many unread notifications a <c>GET /notifications</c> reply lists, at most 50; null when it is not a list.</summary>
    public static int? Unread(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body, Options);
            return doc.RootElement.ValueKind == JsonValueKind.Array
                ? Math.Min(doc.RootElement.EnumerateArray().Count(n => n.ValueKind == JsonValueKind.Object), MaxUnread)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The login of a <c>GET /user</c> reply when it is a plausible GitHub login (1..39 letters, digits, hyphens); else null.</summary>
    public static string? Login(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body, Options);
            return doc.RootElement.ValueKind == JsonValueKind.Object && Text(doc.RootElement, "login") is { } login && IsLogin(login)
                ? login
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A <c>statusCheckRollup.state</c> as a <see cref="CiToken"/>: SUCCESS ok, FAILURE and ERROR fail, PENDING and EXPECTED pending, anything else none.</summary>
    public static string Ci(string? state) => state switch
    {
        "SUCCESS" => CiToken.Ok,
        "FAILURE" or "ERROR" => CiToken.Fail,
        "PENDING" or "EXPECTED" => CiToken.Pending,
        _ => CiToken.None,
    };

    /// <summary>Exactly <c>owner/name</c>, each part 1..100 of ASCII letters, digits, "_", "." and "-".</summary>
    public static bool IsRepo(string? repo)
    {
        var parts = (repo ?? string.Empty).Split('/');
        return parts.Length == 2 && parts.All(part => part.Length is >= 1 and <= MaxRepoPart
            && part.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-'));
    }

    /// <summary>1..39 letters, digits and hyphens.</summary>
    public static bool IsLogin(string login) =>
        login is { Length: > 0 and <= MaxLogin } && login.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    private static void AddItems(List<GitItem> items, JsonElement data, string alias, GitSection section)
    {
        if (!data.TryGetProperty(alias, out var search) || search.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var added = 0;
        foreach (var node in Nodes(search))
        {
            if (added < GitLimits.MaxItemsPerGroup && Item(node, section) is { } item)
            {
                items.Add(item);
                added++;
            }
        }
    }

    private static GitItem? Item(JsonElement node, GitSection section)
    {
        if (node.ValueKind != JsonValueKind.Object
            || !node.TryGetProperty("number", out var n) || n.ValueKind != JsonValueKind.Number || !n.TryGetInt64(out var number) || number <= 0
            || !node.TryGetProperty("repository", out var repository) || repository.ValueKind != JsonValueKind.Object
            || Text(repository, "nameWithOwner") is not { } repo || !IsRepo(repo)
            || Text(node, "__typename") is not ("PullRequest" or "Issue"))
        {
            return null;
        }

        var pull = Text(node, "__typename") == "PullRequest";
        var state = Text(node, "state") switch
        {
            "CLOSED" => GitState.Closed,
            "MERGED" => GitState.Merged,
            _ => GitState.Open,
        };
        var draft = pull && node.TryGetProperty("isDraft", out var d) && d.ValueKind == JsonValueKind.True;
        return new GitItem(
            section,
            repo,
            number,
            DashWire.Clean(Text(node, "title") ?? string.Empty, MaxTitle),
            state,
            pull,
            draft,
            pull ? Ci(RollupState(node)) : CiToken.None,
            Minutes(Text(node, "updatedAt")),
            Url(node));
    }

    private static GitRepo? Repo(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object || Text(node, "nameWithOwner") is not { } name || !IsRepo(name))
        {
            return null;
        }

        var language = node.TryGetProperty("primaryLanguage", out var l) && l.ValueKind == JsonValueKind.Object ? Text(l, "name") : null;
        return new GitRepo(
            name,
            node.TryGetProperty("isPrivate", out var p) && p.ValueKind == JsonValueKind.True,
            DashWire.Clean(language ?? string.Empty, GitLimits.MaxLanguage),
            Whole(node, "stargazerCount"),
            node.TryGetProperty("issues", out var issues) && issues.ValueKind == JsonValueKind.Object ? Count(issues) : 0,
            node.TryGetProperty("pullRequests", out var pulls) && pulls.ValueKind == JsonValueKind.Object ? Count(pulls) : 0,
            Url(node));
    }

    private static List<JsonElement> Nodes(JsonElement parent) =>
        parent.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Array ? [.. nodes.EnumerateArray()] : [];

    private static int Count(JsonElement parent) => Whole(parent, "totalCount");

    private static int Whole(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n) ? Math.Max(0, n) : 0;

    /// <summary>UTC minutes since the Unix epoch of an ISO time; 0 when it does not read.</summary>
    private static long Minutes(string? time) =>
        DateTimeOffset.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? Math.Max(0, at.ToUnixTimeSeconds() / 60) : 0;

    /// <summary>The node's <c>url</c> when it is plain https on <see cref="Domain"/>, else empty; the phone can then never open it.</summary>
    private static string Url(JsonElement node) =>
        Text(node, "url") is { Length: > 0 and <= GitLimits.MaxUrl } text
            && Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0 && uri.IsDefaultPort && uri.Host == Domain
                ? uri.AbsoluteUri
                : string.Empty;

    private static string? RollupState(JsonElement pr)
    {
        if (!pr.TryGetProperty("commits", out var commits) || commits.ValueKind != JsonValueKind.Object
            || !commits.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array || nodes.GetArrayLength() == 0)
        {
            return null;
        }

        var last = nodes[nodes.GetArrayLength() - 1];
        return last.ValueKind == JsonValueKind.Object
            && last.TryGetProperty("commit", out var commit) && commit.ValueKind == JsonValueKind.Object
            && commit.TryGetProperty("statusCheckRollup", out var rollup) && rollup.ValueKind == JsonValueKind.Object
                ? Text(rollup, "state")
                : null;
    }

    private static string? Text(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
}
