using System.Globalization;
using Edgepad.Protocol;

namespace Edgepad.Dashboard;

/// <summary>
/// One state of a host as the phone is told it. <see cref="Body"/> is the BLOB GIT payload; <see cref="Urls"/> holds, in order, the
/// address behind each repository and item line the body kept, so a <c>GitOpen</c> index (0 is the first such line) is a position in
/// it. The addresses are never in the body.
/// </summary>
internal sealed record GitPage(long Rev, string Body, IReadOnlyList<string> Urls);

/// <summary>
/// The body of a BLOB GIT: UTF-8 lines joined by line feeds, fields by tabs, at most <see cref="GitLimits.MaxBytes"/>, the last lines
/// dropped until it fits. Line one is <c>H host login name repos followers rev</c>. There is no <c>N</c> (unread count) line any more: GitHub gives notifications only to a classic
/// token, and a phone still reads one from an older laptop. Then <c>P repo private language stars issues
/// pulls</c> (private is <c>p</c> or empty) for each repository, and <c>M|R|A repo number title flags ci state updatedMin</c> for each
/// item (flags: <c>d</c> draft, <c>i</c> an issue). Every free-text field is cleaned and cut.
/// </summary>
internal static class GitBlob
{
    public static GitPage Encode(string host, GitSnapshot snapshot, long rev, string? extraUrl = null)
    {
        var rows = new List<(string Row, string? Url)>
        {
            (Join("H", DashWire.Clean(host, 16), DashWire.Clean(snapshot.Profile.Login, 39), DashWire.Clean(snapshot.Profile.Name, GitLimits.MaxName), Num(snapshot.Profile.Repos), Num(snapshot.Profile.Followers), Num(rev)), null),
        };

        foreach (var repo in snapshot.Repos.Where(r => GitHubParse.IsRepo(r.FullName)).Take(GitLimits.MaxRepos))
        {
            rows.Add((Join("P", repo.FullName, repo.Private ? "p" : string.Empty, DashWire.Clean(repo.Language, GitLimits.MaxLanguage), Num(repo.Stars), Num(repo.OpenIssues), Num(repo.OpenPulls)), repo.Url));
        }

        foreach (var section in new[] { GitSection.Mine, GitSection.Review, GitSection.Assigned })
        {
            foreach (var item in snapshot.Items.Where(i => i.Section == section && (i.Number > 0 || i.Label is { Length: > 0 }) && GitHubParse.IsRepo(i.Repo)).Take(GitLimits.MaxItemsPerGroup))
            {
                var flags = (item.Draft ? "d" : string.Empty) + (item.IsPull ? string.Empty : "i");
                var state = item.State is GitState.Closed or GitState.Merged ? item.State : GitState.Open;
                var ci = item.Ci is CiToken.Ok or CiToken.Fail or CiToken.Pending ? item.Ci : CiToken.None;
                var number = item.Label is { Length: > 0 } label ? DashWire.Clean(label, 20) : Num(item.Number);
                var title = DashWire.Clean(item.Title, GitLimits.MaxTitle);
                rows.Add((Join(Letter(section), item.Repo, number, title, flags, ci, state, Num(item.UpdatedMin)), item.Url));
            }
        }

        var body = DashWire.Fit([.. rows.Select(r => r.Row)], '\n', GitLimits.MaxBytes);
        var kept = body.Length == 0 ? 0 : body.Count(c => c == '\n') + 1;
        var urls = rows.Take(kept).Where(r => r.Url is not null).Select(r => r.Url!).ToList();
        if (extraUrl is not null)
        {
            // An address that is not a list line (GitHub's "choose repositories"). Last, so its index is the number of repository and
            // item lines the phone counted. A constant of the host, never text from the phone.
            urls.Add(extraUrl);
        }

        return new GitPage(rev, body, urls);
    }

    private static string Letter(GitSection section) => section switch
    {
        GitSection.Review => "R",
        GitSection.Assigned => "A",
        _ => "M",
    };

    private static string Num(long value) => Math.Max(0, value).ToString(CultureInfo.InvariantCulture);

    private static string Join(params string[] fields) => string.Join('\t', fields);
}

/// <summary>
/// One host's pages. <see cref="Update"/> gives the new snapshot a revision only when what the phone would see (or open) changed, so a
/// refresh that finds nothing new sends nothing. The revision never goes back, not even across a <see cref="Clear"/>, so a
/// <c>GitOpen</c> aimed at a page from before a sign-out can never match a page after it.
/// </summary>
internal sealed class GitBook(string host, string? extraUrl = null)
{
    private readonly Lock gate = new();
    private long rev;
    private string? key;
    private GitPage? page;

    public GitPage? Page
    {
        get
        {
            lock (gate)
            {
                return page;
            }
        }
    }

    public GitPage Update(GitSnapshot snapshot)
    {
        lock (gate)
        {
            var probe = GitBlob.Encode(host, snapshot, 0, extraUrl);
            var next = probe.Body + '\u0001' + string.Join('\u0001', probe.Urls);
            if (page is null || next != key)
            {
                key = next;
                rev++;
                page = GitBlob.Encode(host, snapshot, rev, extraUrl);
            }

            return page;
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            page = null;
            key = null;
        }
    }
}
