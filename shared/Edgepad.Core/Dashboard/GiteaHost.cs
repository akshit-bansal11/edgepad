using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Edgepad.Protocol;

namespace Edgepad.Dashboard;

/// <summary>
/// Gitea, Forgejo and Codeberg (Forgejo's public server): one client for the Gitea-compatible API under <c>/api/v1</c>, with the
/// address the person configures (default <see cref="DefaultUrl"/>; https only). The token is a personal access token with the read
/// scopes (<c>read:user</c>, <c>read:repository</c>, <c>read:issue</c>), sent as <c>Authorization: token ...</c>. Mine, Assigned and
/// Review come from <c>/repos/issues/search</c> (<c>created</c>, <c>assigned</c> and, for pull requests, <c>review_requested</c>); a
/// server too old to know <c>review_requested</c> answers 400 and Review stays empty. There is no CI state on this API, so every
/// item's CI is none.
/// </summary>
internal sealed class GiteaHost(ISecretStore store, IntegrationSettings settings, HttpMessageHandler? handler = null, Func<DateTimeOffset>? clock = null)
    : TokenGitHost("gitea", store, settings, handler, clock)
{
    public const string DefaultUrl = "https://codeberg.org";

    public override string Domain => Api?.Host ?? string.Empty;

    protected override string UserPath => "user";

    protected override bool ApiIsFixed => false;

    protected override Uri? ApiFor(string configuredUrl) => GitTokens.GiteaApi(configuredUrl.Trim().Length == 0 ? DefaultUrl : configuredUrl);

    protected override AuthenticationHeaderValue Auth(string token, string user) => new("token", token);

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
        var uid = user is null ? 0 : GitJson.Long(user.RootElement, "id");
        if (user is null || GitJson.Text(user.RootElement, "login") is not { } login || uid <= 0)
        {
            return new GitRead(null, GitReply.Unusable);
        }

        var search = $"repos/search?uid={uid.ToString(CultureInfo.InvariantCulture)}&exclusive=true&sort=updated&order=desc&limit={GitLimits.MaxRepos}";
        var repoReply = await get(new Uri(api, search), ct).ConfigureAwait(false);
        if (repoReply.Result != GitHubResult.Ok)
        {
            return new GitRead(null, repoReply);
        }

        using var repoDoc = GitJson.Parse(repoReply.Body);
        if (repoDoc is null)
        {
            return new GitRead(null, GitReply.Unusable);
        }

        var repos = GitJson.Items(repoDoc.RootElement, "data")
            .Select(node => (Node: node, Updated: GitJson.Minutes(GitJson.Text(node, "updated_at"))))
            .OrderByDescending(r => r.Updated)
            .Select(r => Repo(r.Node, api.Host))
            .OfType<GitRepo>()
            .Take(GitLimits.MaxRepos)
            .ToList();
        var total = repoReply.Headers.TryGetValue("x-total-count", out var count) && int.TryParse(count, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : repos.Count;

        var items = new List<GitItem>();
        foreach (var (query, section, optional) in new[]
        {
            ("created=true", GitSection.Mine, false),
            ("type=pulls&review_requested=true", GitSection.Review, true),
            ("assigned=true", GitSection.Assigned, false),
        })
        {
            var (found, failure) = await SectionAsync(get, api, query, section, optional, ct).ConfigureAwait(false);
            if (failure is not null)
            {
                return new GitRead(null, failure);
            }

            items.AddRange(found);
        }

        var profile = new GitProfile(login, GitJson.Text(user.RootElement, "full_name") ?? string.Empty, total, GitJson.Whole(user.RootElement, "followers_count"));
        return new GitRead(new GitSnapshot(profile, repos, items), null);
    }

    private static GitRepo? Repo(JsonElement node, string domain) =>
        GitJson.Text(node, "full_name") is { } name
            ? new GitRepo(
                name,
                GitJson.Flag(node, "private"),
                GitJson.Text(node, "language") ?? string.Empty,
                GitJson.Whole(node, "stars_count"),
                GitJson.Whole(node, "open_issues_count"),
                GitJson.Whole(node, "open_pr_counter"),
                GitJson.Url(GitJson.Text(node, "html_url"), domain))
            : null;

    private static async Task<(List<GitItem> Items, GitReply? Failure)> SectionAsync(
        GitGet get, Uri api, string query, GitSection section, bool optional, CancellationToken ct)
    {
        var reply = await get(new Uri(api, $"repos/issues/search?state=open&limit={GitLimits.MaxItemsPerGroup}&{query}"), ct).ConfigureAwait(false);
        if (optional && reply.Status is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
        {
            return ([], null);
        }

        if (reply.Result != GitHubResult.Ok)
        {
            return ([], reply);
        }

        using var doc = GitJson.Parse(reply.Body);
        if (doc is null)
        {
            return ([], GitReply.Unusable);
        }

        var items = new List<GitItem>();
        foreach (var node in GitJson.Items(doc.RootElement).Take(GitLimits.MaxItemsPerGroup))
        {
            var repo = GitJson.Text(GitJson.Child(node, "repository"), "full_name");
            var number = GitJson.Long(node, "number");
            if (repo is null || number <= 0)
            {
                continue;
            }

            var pull = GitJson.Child(node, "pull_request");
            var isPull = pull.ValueKind == JsonValueKind.Object;
            var state = isPull && GitJson.Flag(pull, "merged") ? GitState.Merged : GitJson.Text(node, "state") == "closed" ? GitState.Closed : GitState.Open;
            items.Add(new GitItem(
                section,
                repo,
                number,
                GitJson.Text(node, "title") ?? string.Empty,
                state,
                isPull,
                isPull && GitJson.Flag(pull, "draft"),
                CiToken.None,
                GitJson.Minutes(GitJson.Text(node, "updated_at")),
                GitJson.Url(GitJson.Text(node, "html_url"), api.Host)));
        }

        return (items, null);
    }
}
