using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Edgepad.Protocol;

namespace Edgepad.Dashboard;

/// <summary>
/// Bitbucket Cloud, <c>https://api.bitbucket.org/2.0</c>. Since 9 June 2026 only API tokens work (app passwords ended): the person
/// pastes an API token and the e-mail of the Atlassian account, sent as Basic authentication. The token needs
/// <c>read:user:bitbucket</c>, <c>read:repository:bitbucket</c> and <c>read:pullrequest:bitbucket</c>. What Bitbucket has, this
/// shows: the account, its repositories newest first, and the open pull requests the account wrote (Mine). Bitbucket has no stars
/// or follower count, no per-repository open counts in that list, no assignee on a pull request and no endpoint for "waiting for my
/// review" that was verified, so those stay 0 or empty, never invented.
/// </summary>
internal sealed class BitbucketHost(ISecretStore store, IntegrationSettings settings, HttpMessageHandler? handler = null, Func<DateTimeOffset>? clock = null)
    : TokenGitHost("bitbucket", store, settings, handler, clock)
{
    public static readonly Uri ApiRoot = new("https://api.bitbucket.org/2.0/");

    public const string WebDomain = "bitbucket.org";

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

        var repoReply = await get(new Uri(api, $"repositories?role=member&sort=-updated_on&pagelen={GitLimits.MaxRepos}"), ct).ConfigureAwait(false);
        if (repoReply.Result != GitHubResult.Ok)
        {
            return new GitRead(null, repoReply);
        }

        using var repoDoc = GitJson.Parse(repoReply.Body);
        if (repoDoc is null)
        {
            return new GitRead(null, GitReply.Unusable);
        }

        var repos = GitJson.Items(repoDoc.RootElement, "values")
            .Select(node => (Node: node, Updated: GitJson.Minutes(GitJson.Text(node, "updated_on"))))
            .OrderByDescending(r => r.Updated)
            .Select(r => Repo(r.Node))
            .OfType<GitRepo>()
            .Take(GitLimits.MaxRepos)
            .ToList();
        var size = GitJson.Whole(repoDoc.RootElement, "size");

        var pullReply = await get(new Uri(api, $"pullrequests/{Uri.EscapeDataString(account)}?state=OPEN&pagelen={GitLimits.MaxItemsPerGroup}"), ct).ConfigureAwait(false);
        if (pullReply.Result != GitHubResult.Ok)
        {
            return new GitRead(null, pullReply);
        }

        using var pullDoc = GitJson.Parse(pullReply.Body);
        if (pullDoc is null)
        {
            return new GitRead(null, GitReply.Unusable);
        }

        var items = GitJson.Items(pullDoc.RootElement, "values")
            .Select(Pull)
            .OfType<GitItem>()
            .OrderByDescending(i => i.UpdatedMin)
            .Take(GitLimits.MaxItemsPerGroup)
            .ToList();

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
