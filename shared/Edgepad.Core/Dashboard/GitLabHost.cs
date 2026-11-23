using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Edgepad.Protocol;

namespace Edgepad.Dashboard;

/// <summary>
/// GitLab, read only, over the REST API v4 (https://docs.gitlab.com/api/). With no server address saved it is gitlab.com, signed in by
/// <see cref="GitLabSignIn"/> (device authorization grant, scopes read_api and read_user); the access token is renewed shortly before
/// it ends and once after a 401 by <see cref="GitLabRenewal"/>. With an address saved it is that self-hosted server, read with a
/// <c>read_api</c> personal access token the person pastes (<see cref="SaveServerAsync"/>); a server would need its own OAuth
/// application, so it never sees a POST. Either way every request is a GET with <c>Authorization: Bearer</c> through the
/// <see cref="ReadOnlyGate"/>. Endpoints, from the GitLab docs: <c>GET /user</c>, <c>GET /projects?membership=true&amp;order_by=last_activity_at</c>,
/// <c>GET /merge_requests</c> with <c>scope</c> created_by_me, reviews_for_me and assigned_to_me, and <c>GET /issues</c> with <c>scope</c>
/// created_by_me and assigned_to_me, all with <c>state=opened</c>. A project's language is not in the projects list, an open
/// merge-request count is not either, and the merge request list is not documented to carry a pipeline: those read as empty, 0 and
/// no CI unless a reply names <c>head_pipeline.status</c>.
/// </summary>
internal sealed class GitLabHost(ISecretStore store, IntegrationSettings settings, HttpMessageHandler? handler = null, Func<DateTimeOffset>? clock = null)
    : TokenGitHost("gitlab", store, settings, handler, clock)
{
    private readonly ISecretStore secrets = store;
    private readonly GitLabRenewal renewal = new(store, handler, clock ?? (() => DateTimeOffset.UtcNow));

    public override string Domain => Api?.Host ?? string.Empty;

    /// <summary>The self-hosted server's address as saved; empty for gitlab.com.</summary>
    public string Server => ConfiguredUrl;

    protected override string UserPath => "user";

    protected override bool ApiIsFixed => false;

    protected override bool RenewDue => ConfiguredUrl.Length == 0 && renewal.IsDue();

    protected override bool CanRenew => ConfiguredUrl.Length == 0 && renewal.CanRefresh;

    protected override Task<RefreshOutcome> RenewAsync() => renewal.RefreshAsync();

    protected override Uri? ApiFor(string configuredUrl) => configuredUrl.Trim().Length == 0 ? GitLabApp.Api : GitTokens.GitLabApi(configuredUrl);

    protected override AuthenticationHeaderValue Auth(string token, string user) => new("Bearer", token);

    protected override string? ParseLogin(byte[] body)
    {
        using var doc = GitJson.Parse(body);
        return doc is null ? null : GitJson.Text(doc.RootElement, "username");
    }

    /// <summary>Checks a <c>read_api</c> token for the self-hosted server at <paramref name="url"/> and saves both when the server accepts it; an empty address is refused (gitlab.com signs in by the device flow).</summary>
    public async Task<GitSaveResult> SaveServerAsync(string token, string url, CancellationToken ct)
    {
        if (url.Trim().Length == 0)
        {
            return new GitSaveResult(GitSaveStatus.BadUrl, string.Empty);
        }

        var result = await SaveAsync(token, url, string.Empty, ct).ConfigureAwait(false);
        if (result.Status == GitSaveStatus.Saved)
        {
            // A pasted token has no refresh token: nothing of a gitlab.com sign-in may stay beside it.
            GitLabApp.ForgetRenewal(secrets);
        }

        return result;
    }

    /// <summary>Forgets the token, its refresh token and the page; the server address stays for the next sign-in.</summary>
    public override void SignOut()
    {
        base.SignOut();
        GitLabApp.ForgetRenewal(secrets);
    }

    protected override async Task<GitRead> ReadAsync(GitGet get, Uri api, CancellationToken ct)
    {
        var who = await get(new Uri(api, UserPath), ct).ConfigureAwait(false);
        if (who.Result != GitHubResult.Ok)
        {
            return new GitRead(null, who);
        }

        using var user = GitJson.Parse(who.Body);
        if (user is null || GitJson.Text(user.RootElement, "username") is not { } login)
        {
            return new GitRead(null, GitReply.Unusable);
        }

        var projects = await get(new Uri(api, $"projects?membership=true&order_by=last_activity_at&sort=desc&per_page={GitLimits.MaxRepos}"), ct).ConfigureAwait(false);
        if (projects.Result != GitHubResult.Ok)
        {
            return new GitRead(null, projects);
        }

        using var projectDoc = GitJson.Parse(projects.Body);
        if (projectDoc is null)
        {
            return new GitRead(null, GitReply.Unusable);
        }

        var repos = GitJson.Items(projectDoc.RootElement).Select(node => Repo(node, api.Host)).OfType<GitRepo>().Take(GitLimits.MaxRepos).ToList();
        var total = projects.Headers.TryGetValue("x-total", out var count) && int.TryParse(count, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : repos.Count;

        var items = new List<GitItem>();
        foreach (var (section, merges, issues, optional) in new (GitSection, string, string?, bool)[]
        {
            (GitSection.Mine, "created_by_me", "created_by_me", false),
            (GitSection.Review, "reviews_for_me", null, true),
            (GitSection.Assigned, "assigned_to_me", "assigned_to_me", false),
        })
        {
            var found = new List<GitItem>();
            var (pulls, failure) = await ListAsync(get, api, "merge_requests", merges, section, true, optional, ct).ConfigureAwait(false);
            if (failure is null)
            {
                found.AddRange(pulls);
                if (issues is not null)
                {
                    (pulls, failure) = await ListAsync(get, api, "issues", issues, section, false, false, ct).ConfigureAwait(false);
                    found.AddRange(pulls);
                }
            }

            if (failure is not null)
            {
                return new GitRead(null, failure);
            }

            items.AddRange(found.OrderByDescending(item => item.UpdatedMin).Take(GitLimits.MaxItemsPerGroup));
        }

        var profile = new GitProfile(login, GitJson.Text(user.RootElement, "name") ?? string.Empty, total, GitJson.Whole(user.RootElement, "followers"));
        return new GitRead(new GitSnapshot(profile, repos, items), null);
    }

    /// <summary>
    /// The pipeline state of a merge request as a <see cref="CiToken"/>, from GitLab's pipeline statuses
    /// (https://docs.gitlab.com/api/pipelines/): success is Ok, failed is Fail, a pipeline that has not finished is Pending, the rest is none.
    /// </summary>
    internal static string Ci(string? status) => status switch
    {
        "success" => CiToken.Ok,
        "failed" => CiToken.Fail,
        "created" or "waiting_for_resource" or "preparing" or "pending" or "running" or "scheduled" => CiToken.Pending,
        _ => CiToken.None,
    };

    private static GitRepo? Repo(JsonElement node, string domain) =>
        GitJson.Text(node, "path_with_namespace") is { } name
            ? new GitRepo(
                name,
                GitJson.Text(node, "visibility") == "private",
                string.Empty,
                GitJson.Whole(node, "star_count"),
                GitJson.Whole(node, "open_issues_count"),
                0,
                GitJson.Url(GitJson.Text(node, "web_url"), domain))
            : null;

    private static async Task<(List<GitItem> Items, GitReply? Failure)> ListAsync(
        GitGet get, Uri api, string path, string scope, GitSection section, bool pull, bool optional, CancellationToken ct)
    {
        var reply = await get(new Uri(api, $"{path}?scope={scope}&state=opened&order_by=updated_at&sort=desc&per_page={GitLimits.MaxItemsPerGroup}"), ct).ConfigureAwait(false);
        if (optional && reply.Status is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
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
            // references.full is "group/project!12" for a merge request and "group/project#12" for an issue.
            var full = GitJson.Text(GitJson.Child(node, "references"), "full");
            var cut = full?.LastIndexOf(pull ? '!' : '#') ?? -1;
            var number = GitJson.Long(node, "iid");
            if (full is null || cut <= 0 || number <= 0)
            {
                continue;
            }

            var state = GitJson.Text(node, "state") switch
            {
                "merged" => GitState.Merged,
                "closed" => GitState.Closed,
                _ => GitState.Open,
            };
            items.Add(new GitItem(
                section,
                full[..cut],
                number,
                GitJson.Text(node, "title") ?? string.Empty,
                state,
                pull,
                pull && GitJson.Flag(node, "draft"),
                pull ? Ci(GitJson.Text(GitJson.Child(node, "head_pipeline"), "status")) : CiToken.None,
                GitJson.Minutes(GitJson.Text(node, "updated_at")),
                GitJson.Url(GitJson.Text(node, "web_url"), api.Host)));
        }

        return (items, null);
    }
}
