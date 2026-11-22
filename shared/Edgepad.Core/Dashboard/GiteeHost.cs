using System.Net.Http.Headers;
using System.Text.Json;

namespace Edgepad.Dashboard;

/// <summary>
/// Gitee, <c>https://gitee.com/api/v5</c>. The person pastes a personal access token. The reference (https://gitee.com/api/v5/swagger) documents the token only as the <c>access_token</c> query
/// parameter, so that is what is sent (an Authorization header is not documented). Because the token is then in the address, no
/// message, exception text or log line may carry a request address. Read endpoints: <c>GET /user</c>, <c>GET /user/repos</c> and
/// <c>GET /user/issues?filter=created|assigned</c> (Mine and Assigned; <c>number</c> is a text id such as <c>I1ABCD</c>, sent as
/// the item's label). Pull requests stay empty: the reference has no user-wide pull request list. Stars and open-issue counts come from the repository list;
/// its open pull request count is not in it and stays 0. The reference documents <c>public</c> (not <c>private</c>) on a
/// repository. The token's required scopes are unverified.
/// </summary>
internal sealed class GiteeHost(ISecretStore store, IntegrationSettings settings, HttpMessageHandler? handler = null, Func<DateTimeOffset>? clock = null)
    : TokenGitHost("gitee", store, settings, handler, clock)
{
    public static readonly Uri ApiRoot = new("https://gitee.com/api/v5/");

    public const string WebDomain = "gitee.com";

    public override string Domain => WebDomain;

    protected override string UserPath => "user";

    protected override Uri? ApiFor(string configuredUrl) => ApiRoot;

    protected override AuthenticationHeaderValue? Auth(string token, string user) => null;

    // The token travels in the address, so the address must never reach a message: CappedHttp logs only the exception type,
    // ReadOnlyGate names host and path only, and no code here formats a request address.
    protected override Uri Sign(Uri uri, string token) =>
        new(uri.AbsoluteUri + (uri.Query.Length == 0 ? "?" : "&") + "access_token=" + Uri.EscapeDataString(token));

    protected override string? ParseLogin(byte[] body)
    {
        using var doc = GitJson.Parse(body);
        return doc is null ? null : GitJson.Text(doc.RootElement, "login");
    }

    protected override async Task<GitRead> ReadAsync(GitGet get, Uri api, CancellationToken ct)
    {
        var who = await get(new Uri(api, UserPath), ct).ConfigureAwait(false);
        if (who.Result != GitHubResult.Ok)
        {
            return new GitRead(null, who);
        }

        using var user = GitJson.Parse(who.Body);
        if (user is null || GitJson.Text(user.RootElement, "login") is not { } login)
        {
            return new GitRead(null, GitReply.Unusable);
        }

        var repoReply = await get(new Uri(api, $"user/repos?sort=updated&direction=desc&per_page={GitLimits.MaxRepos}&page=1"), ct).ConfigureAwait(false);
        if (repoReply.Result != GitHubResult.Ok)
        {
            return new GitRead(null, repoReply);
        }

        using var repoDoc = GitJson.Parse(repoReply.Body);
        if (repoDoc is null)
        {
            return new GitRead(null, GitReply.Unusable);
        }

        var repos = GitJson.Items(repoDoc.RootElement)
            .Select(node => (Node: node, Updated: GitJson.Minutes(GitJson.Text(node, "updated_at"))))
            .OrderByDescending(r => r.Updated)
            .Select(r => Repo(r.Node))
            .OfType<GitRepo>()
            .Take(GitLimits.MaxRepos)
            .ToList();
        var total = GitJson.Whole(user.RootElement, "public_repos");

        var profile = new GitProfile(login, GitJson.Text(user.RootElement, "name") ?? string.Empty, total > 0 ? total : repos.Count, GitJson.Whole(user.RootElement, "followers"));
        var items = new List<GitItem>();
        foreach (var (filter, section) in new[] { ("created", GitSection.Mine), ("assigned", GitSection.Assigned) })
        {
            var reply = await get(new Uri(api, $"user/issues?filter={filter}&state=open&sort=updated&direction=desc&per_page={GitLimits.MaxItemsPerGroup}&page=1"), ct).ConfigureAwait(false);
            if (reply.Result != GitHubResult.Ok)
            {
                return new GitRead(null, reply);
            }

            using var doc = GitJson.Parse(reply.Body);
            if (doc is null)
            {
                return new GitRead(null, GitReply.Unusable);
            }

            items.AddRange(GitJson.Items(doc.RootElement).Take(GitLimits.MaxItemsPerGroup).Select(node => Issue(node, section)).OfType<GitItem>());
        }

        return new GitRead(new GitSnapshot(profile, repos, items), null);
    }

    private static GitItem? Issue(JsonElement node, GitSection section)
    {
        var repo = GitJson.Text(GitJson.Child(node, "repository"), "full_name");
        var id = GitJson.Text(node, "number");
        if (repo is null || id is not { Length: > 0 and <= 20 } || !id.All(char.IsAsciiLetterOrDigit))
        {
            return null;
        }

        return new GitItem(
            section,
            repo,
            0,
            GitJson.Text(node, "title") ?? string.Empty,
            GitJson.Text(node, "state") is "closed" or "rejected" ? GitState.Closed : GitState.Open,
            false,
            false,
            CiToken.None,
            GitJson.Minutes(GitJson.Text(node, "updated_at")),
            GitJson.Url(GitJson.Text(node, "html_url"), WebDomain),
            id);
    }

    private static GitRepo? Repo(JsonElement node) =>
        GitJson.Text(node, "full_name") is { } name
            ? new GitRepo(
                name,
                !GitJson.Flag(node, "public"),
                GitJson.Text(node, "language") ?? string.Empty,
                GitJson.Whole(node, "stargazers_count"),
                GitJson.Whole(node, "open_issues_count"),
                0,
                GitJson.Url(GitJson.Text(node, "html_url"), WebDomain))
            : null;
}
