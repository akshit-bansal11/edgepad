namespace Edgepad.Dashboard;

/// <summary>Which of the phone's three filters an item belongs to. The wire letters are M, R and A.</summary>
internal enum GitSection
{
    Mine,
    Review,
    Assigned,
}

/// <summary>The <see cref="GitItem.State"/> tokens. Anything else reads as <see cref="Open"/>.</summary>
internal static class GitState
{
    public const string Open = "open";
    public const string Closed = "closed";
    public const string Merged = "merged";
}

/// <summary>The bounds of everything a git host sends the phone. Counts are per refresh, so a screen is one bounded list.</summary>
internal static class GitLimits
{
    public const int MaxRepos = 30;
    public const int MaxItemsPerGroup = 20;
    public const int MaxTitle = 80;
    public const int MaxName = 40;
    public const int MaxLanguage = 30;
    public const int MaxUrl = 300;
    public const int MaxBytes = 16 * 1024;
}

/// <summary>The signed-in account. <see cref="Repos"/> and <see cref="Followers"/> are counts, not lists.</summary>
internal sealed record GitProfile(string Login, string Name, int Repos, int Followers);

/// <summary>One repository. <see cref="Url"/> is laptop-private (never sent) and empty when the host sent none this app may open.</summary>
internal sealed record GitRepo(string FullName, bool Private, string Language, int Stars, int OpenIssues, int OpenPulls, string Url);

/// <summary>
/// One pull request or issue. <see cref="Ci"/> is a <see cref="Edgepad.Protocol.CiToken"/>, <see cref="UpdatedMin"/> UTC minutes since the
/// Unix epoch (0 when unknown), <see cref="Url"/> laptop-private like <see cref="GitRepo.Url"/>. <see cref="Label"/> is the text id of a
/// host whose numbers are not integers (Gitee); when set it is sent in place of <see cref="Number"/>.
/// </summary>
internal sealed record GitItem(GitSection Section, string Repo, long Number, string Title, string State, bool IsPull, bool Draft, string Ci, long UpdatedMin, string Url, string? Label = null);

/// <summary>What one host knows about the account: at most <see cref="GitLimits.MaxRepos"/> repositories, recent first, and at most <see cref="GitLimits.MaxItemsPerGroup"/> items per section.</summary>
internal sealed record GitSnapshot(GitProfile Profile, IReadOnlyList<GitRepo> Repos, IReadOnlyList<GitItem> Items);

/// <summary>Opens an address in the person's default browser. Each laptop app implements it; <see cref="Open"/> says whether it tried.</summary>
internal interface IUrlOpener
{
    bool Open(Uri url);
}

/// <summary>One git host the laptop is signed in to. Read only: no member can change anything on the host.</summary>
internal interface IGitHost
{
    /// <summary>The <see cref="DevProvider.Id"/> and the first field of a <c>GitOpen</c> text.</summary>
    string Id { get; }

    /// <summary>The one web host name whose https links this host's pages may open, for example <c>github.com</c>.</summary>
    string Domain { get; }

    /// <summary>The last good result as the blob and the addresses its indexes stand for; null before the first one and after a sign-out.</summary>
    GitPage? Page { get; }

    /// <summary>Asks the host once. Never throws but on cancellation.</summary>
    Task<GitHubRefreshResult> RefreshAsync(CancellationToken ct);
}
