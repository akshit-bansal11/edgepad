using System.Net.Http.Headers;
using System.Text.Json;

namespace Edgepad.Dashboard;

/// <summary>
/// Gitee, <c>https://gitee.com/api/v5</c>. The person pastes a personal access token, sent as <c>Authorization: Bearer ...</c> (never
/// in the address). The reference (https://gitee.com/api/v5/swagger) documents the token only as the <c>access_token</c> query
/// parameter; that the <c>Authorization: Bearer</c> header is accepted is NOT in it and is unverified. Only two read endpoints are
/// used: <c>GET /user</c> and <c>GET /user/repos</c>. Pull requests and issues stay empty: the reference has no user-wide pull
/// request list, and <c>GET /user/issues</c> returns <c>number</c> as a string (for example <c>I1ABCD</c>) that the phone's numeric
/// line cannot carry, so showing them would mean inventing a number. Stars and open-issue counts come from the repository list;
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

    protected override AuthenticationHeaderValue Auth(string token, string user) => new("Bearer", token);

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
        return new GitRead(new GitSnapshot(profile, repos, []), null);
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
