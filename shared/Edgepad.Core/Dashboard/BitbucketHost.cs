using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Edgepad.Protocol;

namespace Edgepad.Dashboard;

/// <summary>
/// Bitbucket Cloud, <c>https://api.bitbucket.org/2.0</c>. Since 9 June 2026 only API tokens work (app passwords ended): the person
/// pastes an API token and the e-mail of the Atlassian account, sent as Basic authentication
/// (https://support.atlassian.com/bitbucket-cloud/docs/using-api-tokens/). The token's scope names are not listed in the REST
/// reference and are unverified. Endpoints, all from https://developer.atlassian.com/cloud/bitbucket/rest/: <c>GET /user</c>,
/// <c>GET /user/workspaces</c> (workspace slugs), <c>GET /repositories/{workspace}?role=member</c> and
/// <c>GET /workspaces/{workspace}/pullrequests/{selected_user}?state=OPEN</c>. The older <c>/repositories</c> and
/// <c>/pullrequests/{user}</c> lists are not in the current reference. Up to three workspaces are read. This shows the account,
/// its repositories newest first, and the open pull requests the account wrote (Mine). Bitbucket documents no stars or follower
/// count, no per-repository open counts in that list, no pull request assignee and no "waiting for my review" list, so those
/// stay 0 or empty, never invented.
/// </summary>
internal sealed class BitbucketHost(ISecretStore store, IntegrationSettings settings, HttpMessageHandler? handler = null, Func<DateTimeOffset>? clock = null)
    : TokenGitHost("bitbucket", store, settings, handler, clock)
{
    public static readonly Uri ApiRoot = new("https://api.bitbucket.org/2.0/");

    public const string WebDomain = "bitbucket.org";

    private const int MaxWorkspaces = 3;

    public override string Domain => WebDomain;

    protected override bool RequiresUser => true;

    protected override string UserPath => "user";

    protected override Uri? ApiFor(string configuredUrl) => ApiRoot;

    protected override AuthenticationHeaderValue Auth(string token, string user) =>
        new("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(user + ":" + token)));

    protected override string? ParseLogin(byte[] body)
    {
        using var doc = GitJson.Parse(body);
        return doc is null ? null : Login(doc.RootElement);
    }

    protected override async Task<GitRead> ReadAsync(GitGet get, Uri api, CancellationToken ct)
    {
        var who = await get(new Uri(api, UserPath), ct).ConfigureAwait(false);
        if (who.Result != GitHubResult.Ok)
        {
            return new GitRead(null, who);
        }

        using var user = GitJson.Parse(who.Body);
        var account = user is null ? null : GitJson.Text(user.RootElement, "account_id") ?? GitJson.Text(user.RootElement, "uuid");
        if (user is null || Login(user.RootElement) is not { } login || account is not { Length: > 0 })
        {
            return new GitRead(null, GitReply.Unusable);
        }

        var spaceReply = await get(new Uri(api, $"user/workspaces?pagelen={MaxWorkspaces}"), ct).ConfigureAwait(false);
        if (spaceReply.Result != GitHubResult.Ok)
        {
            return new GitRead(null, spaceReply);
        }

        using var spaceDoc = GitJson.Parse(spaceReply.Body);
        if (spaceDoc is null)
        {
            return new GitRead(null, GitReply.Unusable);
        }

        var slugs = GitJson.Items(spaceDoc.RootElement, "values")
            .Select(node => GitJson.Text(GitJson.Child(node, "workspace"), "slug"))
            .OfType<string>()
            .Where(slug => slug.Length > 0)
            .Take(MaxWorkspaces)
            .ToList();

        var found = new List<(GitRepo Repo, long Updated)>();
        var items = new List<GitItem>();
        var size = 0;
        foreach (var slug in slugs)
        {
            var name = Uri.EscapeDataString(slug);
            var repoReply = await get(new Uri(api, $"repositories/{name}?role=member&sort=-updated_on&pagelen={GitLimits.MaxRepos}"), ct).ConfigureAwait(false);
            if (repoReply.Result != GitHubResult.Ok)
            {
                return new GitRead(null, repoReply);
            }

            using var repoDoc = GitJson.Parse(repoReply.Body);
            if (repoDoc is null)
            {
                return new GitRead(null, GitReply.Unusable);
            }

            foreach (var node in GitJson.Items(repoDoc.RootElement, "values"))
            {
                if (Repo(node) is { } repo)
                {
                    found.Add((repo, GitJson.Minutes(GitJson.Text(node, "updated_on"))));
                }
            }

            size += GitJson.Whole(repoDoc.RootElement, "size");

            var pullReply = await get(new Uri(api, $"workspaces/{name}/pullrequests/{Uri.EscapeDataString(account)}?state=OPEN&pagelen={GitLimits.MaxItemsPerGroup}"), ct).ConfigureAwait(false);
            if (pullReply.Result != GitHubResult.Ok)
            {
                return new GitRead(null, pullReply);
            }

            using var pullDoc = GitJson.Parse(pullReply.Body);
            if (pullDoc is null)
            {
                return new GitRead(null, GitReply.Unusable);
            }

            items.AddRange(GitJson.Items(pullDoc.RootElement, "values").Select(Pull).OfType<GitItem>());
        }

        var repos = found.OrderByDescending(r => r.Updated).Select(r => r.Repo).Take(GitLimits.MaxRepos).ToList();
        items = [.. items.OrderByDescending(i => i.UpdatedMin).Take(GitLimits.MaxItemsPerGroup)];

        var profile = new GitProfile(login, GitJson.Text(user.RootElement, "display_name") ?? string.Empty, size > 0 ? size : repos.Count, 0);
        return new GitRead(new GitSnapshot(profile, repos, items), null);
    }

    private static string? Login(JsonElement user) => GitJson.Text(user, "nickname") is { Length: > 0 } nick ? nick : GitJson.Text(user, "display_name");

    private static GitRepo? Repo(JsonElement node) =>
        GitJson.Text(node, "full_name") is { } name
            ? new GitRepo(
                name,
                GitJson.Flag(node, "is_private"),
                GitJson.Text(node, "language") ?? string.Empty,
                0,
                0,
                0,
                Link(node))
            : null;

    private static GitItem? Pull(JsonElement node)
    {
        var repo = GitJson.Text(GitJson.Child(GitJson.Child(node, "destination"), "repository"), "full_name");
        var number = GitJson.Long(node, "id");
        if (repo is null || number <= 0)
        {
            return null;
        }

        var state = GitJson.Text(node, "state") switch
        {
            "MERGED" => GitState.Merged,
            "DECLINED" or "SUPERSEDED" => GitState.Closed,
            _ => GitState.Open,
        };
        return new GitItem(
            GitSection.Mine,
            repo,
            number,
            GitJson.Text(node, "title") ?? string.Empty,
            state,
            true,
            GitJson.Flag(node, "draft"),
            CiToken.None,
            GitJson.Minutes(GitJson.Text(node, "updated_on")),
            Link(node));
    }

    private static string Link(JsonElement node) => GitJson.Url(GitJson.Text(GitJson.Child(GitJson.Child(node, "links"), "html"), "href"), WebDomain);
}
