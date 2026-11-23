using System.Text;
using Edgepad.Dashboard;
using Edgepad.Protocol;
using Xunit;

namespace Edgepad.Tests;

public sealed class GitHubParseTests
{
    /// <summary>A JSON escape built at run time, so no editor or tool can turn it into the raw character it stands for.</summary>
    private const string Esc = "\\" + "u001b";
    private const string Rlo = "\\" + "u202e";
    private const string Tab = "\\" + "t";
    private const string Lf = "\\" + "n";

    internal const string Normal = """
        {"data":{
          "viewer":{"login":"octo-cat","name":"Octo Cat","followers":{"totalCount":12},
            "repositories":{"totalCount":34,"nodes":[
              {"nameWithOwner":"octo-cat/edgepad","isPrivate":false,"url":"https://github.com/octo-cat/edgepad","stargazerCount":5,
               "primaryLanguage":{"name":"C#"},"issues":{"totalCount":2},"pullRequests":{"totalCount":1}},
              {"nameWithOwner":"octo-cat/secret","isPrivate":true,"url":"https://github.com/octo-cat/secret","stargazerCount":0,
               "primaryLanguage":null,"issues":{"totalCount":0},"pullRequests":{"totalCount":0}}
            ]}},
          "review":{"nodes":[
            {"__typename":"PullRequest","number":12,"title":"Fix the pairing timeout","isDraft":false,"state":"OPEN",
             "url":"https://github.com/octo/edgepad/pull/12","updatedAt":"2026-11-19T11:00:00Z","repository":{"nameWithOwner":"octo/edgepad"},
             "commits":{"nodes":[{"commit":{"statusCheckRollup":{"state":"SUCCESS"}}}]}},
            {"__typename":"PullRequest","number":7,"title":"Draft idea","isDraft":true,"state":"OPEN",
             "url":"https://github.com/octo/other.repo/pull/7","updatedAt":"2026-11-19T11:00:00Z","repository":{"nameWithOwner":"octo/other.repo"},
             "commits":{"nodes":[{"commit":{"statusCheckRollup":null}}]}}
          ]},
          "mine":{"nodes":[
            {"__typename":"PullRequest","number":3,"title":"Mine","isDraft":false,"state":"MERGED",
             "url":"https://github.com/me/my_repo-2/pull/3","updatedAt":"2026-11-19T11:00:00Z","repository":{"nameWithOwner":"me/my_repo-2"},
             "commits":{"nodes":[{"commit":{"statusCheckRollup":{"state":"FAILURE"}}}]}},
            {"__typename":"Issue","number":9,"title":"Crash on pair","state":"OPEN",
             "url":"https://github.com/me/my_repo-2/issues/9","updatedAt":"2026-11-19T11:00:00Z","repository":{"nameWithOwner":"me/my_repo-2"}},
            {}
          ]},
          "assigned":{"nodes":[
            {"__typename":"Issue","number":4,"title":"Assigned to me","state":"CLOSED",
             "url":"https://evil.example/octo/edgepad/issues/4","updatedAt":"not a time","repository":{"nameWithOwner":"octo/edgepad"}}
          ]}
        }}
        """;

    internal static long UpdatedMin => new DateTimeOffset(2026, 11, 19, 11, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds() / 60;

    [Fact]
    public void ANormalReplyGivesTheProfileTheReposAndEachGroupOfItems()
    {
        var snapshot = GitHubParse.Snapshot(Bytes(Normal))!;

        Assert.Equal(new GitProfile("octo-cat", "Octo Cat", 34, 12), snapshot.Profile);
        Assert.Equal(
            new List<GitRepo>
            {
                new("octo-cat/edgepad", false, "C#", 5, 2, 1, "https://github.com/octo-cat/edgepad"),
                new("octo-cat/secret", true, string.Empty, 0, 0, 0, "https://github.com/octo-cat/secret"),
            },
            snapshot.Repos);
        Assert.Equal(
            new List<GitItem>
            {
                new(GitSection.Mine, "me/my_repo-2", 3, "Mine", GitState.Merged, true, false, CiToken.Fail, UpdatedMin, "https://github.com/me/my_repo-2/pull/3"),
                new(GitSection.Mine, "me/my_repo-2", 9, "Crash on pair", GitState.Open, false, false, CiToken.None, UpdatedMin, "https://github.com/me/my_repo-2/issues/9"),
                new(GitSection.Review, "octo/edgepad", 12, "Fix the pairing timeout", GitState.Open, true, false, CiToken.Ok, UpdatedMin, "https://github.com/octo/edgepad/pull/12"),
                new(GitSection.Review, "octo/other.repo", 7, "Draft idea", GitState.Open, true, true, CiToken.None, UpdatedMin, "https://github.com/octo/other.repo/pull/7"),

                // An address off github.com is kept as empty, and a time that does not read as 0.
                new(GitSection.Assigned, "octo/edgepad", 4, "Assigned to me", GitState.Closed, false, false, CiToken.None, 0, string.Empty),
            },
            snapshot.Items);
    }

    [Fact]
    public void AnEmptyAccountGivesNoRowsButStillAProfile()
    {
        var snapshot = GitHubParse.Snapshot(Bytes("""{"data":{"viewer":{"login":"a"},"mine":{"nodes":[]}}}"""))!;
        Assert.Equal(new GitProfile("a", string.Empty, 0, 0), snapshot.Profile);
        Assert.Empty(snapshot.Repos);
        Assert.Empty(snapshot.Items);
    }

    [Theory]
    [InlineData("""{"errors":[{"message":"Bad credentials"}]}""")]
    [InlineData("""{"data":null}""")]
    [InlineData("""{"data":{"mine":{"nodes":[]}}}""")]
    [InlineData("""{"data":{"viewer":{"login":"evil name"}}}""")]
    [InlineData("""[]""")]
    [InlineData("""not json""")]
    [InlineData("")]
    public void AReplyWithoutAViewerIsNull(string body) => Assert.Null(GitHubParse.Snapshot(Bytes(body)));

    [Fact]
    public void HostileTitlesAreCleanedAndCut()
    {
        var title = "Tab" + Tab + "here" + Esc + "[31mred" + Esc + "[0m new" + Lf + "line " + Rlo + "evil" + new string('x', 100);
        var body = Mine("""{"__typename":"Issue","number":1,"title":"TITLE","repository":{"nameWithOwner":"a/b"}}""".Replace("TITLE", title, StringComparison.Ordinal));

        var item = Assert.Single(GitHubParse.Snapshot(Bytes(body))!.Items);
        Assert.DoesNotContain(item.Title, c => char.IsControl(c) || c == (char)0x202E);
        Assert.StartsWith("Tab here [31mred [0m new line evil", item.Title, StringComparison.Ordinal);
        Assert.Equal(GitHubParse.MaxTitle, item.Title.Length);
        Assert.Equal(CiToken.None, item.Ci);
    }

    [Theory]
    [InlineData("octo/my repo")]
    [InlineData("octo")]
    [InlineData("a/b/c")]
    [InlineData("octo/rep@")]
    [InlineData("octo/repo\t")]
    [InlineData("/repo")]
    public void ARowWithAnOddRepositoryIsDropped(string repo)
    {
        var body = Mine("""{"__typename":"Issue","number":1,"title":"x","repository":{"nameWithOwner":"REPO"}},{"__typename":"Issue","number":2,"title":"y","repository":{"nameWithOwner":"ok/fine"}}"""
            .Replace("REPO", repo.Replace("\t", Tab, StringComparison.Ordinal), StringComparison.Ordinal));
        var item = Assert.Single(GitHubParse.Snapshot(Bytes(body))!.Items);
        Assert.Equal("ok/fine", item.Repo);
    }

    [Theory]
    [InlineData("""{"__typename":"Issue","number":0,"title":"x","repository":{"nameWithOwner":"a/b"}}""")]
    [InlineData("""{"__typename":"Issue","number":-4,"title":"x","repository":{"nameWithOwner":"a/b"}}""")]
    [InlineData("""{"__typename":"Issue","number":"5","title":"x","repository":{"nameWithOwner":"a/b"}}""")]
    [InlineData("""{"__typename":"Issue","title":"x","repository":{"nameWithOwner":"a/b"}}""")]
    [InlineData("""{"__typename":"Issue","number":5,"title":"x","repository":null}""")]
    [InlineData("""{"__typename":"Repository","number":5,"title":"x","repository":{"nameWithOwner":"a/b"}}""")]
    [InlineData("""{"number":5,"title":"x","repository":{"nameWithOwner":"a/b"}}""")]
    [InlineData("""17""")]
    public void ARowMissingItsNumberTypeOrRepositoryIsDropped(string node) =>
        Assert.Empty(GitHubParse.Snapshot(Bytes(Mine(node)))!.Items);

    [Fact]
    public void AtMostTwentyItemsPerGroupAndThirtyReposAreKept()
    {
        var nodes = string.Join(",", Enumerable.Range(1, 30).Select(i => """{"__typename":"Issue","number":N,"title":"t","repository":{"nameWithOwner":"a/b"}}""".Replace("N", $"{i}", StringComparison.Ordinal)));
        var repos = string.Join(",", Enumerable.Range(1, 40).Select(i => """{"nameWithOwner":"a/rN"}""".Replace("N", $"{i}", StringComparison.Ordinal)));
        var body = """{"data":{"viewer":{"login":"a","repositories":{"totalCount":40,"nodes":[REPOS]}},"mine":{"nodes":[NODES]}}}"""
            .Replace("REPOS", repos, StringComparison.Ordinal)
            .Replace("NODES", nodes, StringComparison.Ordinal);

        var snapshot = GitHubParse.Snapshot(Bytes(body))!;

        Assert.Equal(GitLimits.MaxItemsPerGroup, snapshot.Items.Count);
        Assert.Equal(GitLimits.MaxRepos, snapshot.Repos.Count);
        Assert.Equal(40, snapshot.Profile.Repos);
    }

    [Fact]
    public void AReplyNestedPastTheLimitIsRefused()
    {
        var deep = new string('[', 40) + new string(']', 40);
        Assert.Null(GitHubParse.Snapshot(Bytes(Mine(deep))));
    }

    [Theory]
    [InlineData("SUCCESS", CiToken.Ok)]
    [InlineData("FAILURE", CiToken.Fail)]
    [InlineData("ERROR", CiToken.Fail)]
    [InlineData("PENDING", CiToken.Pending)]
    [InlineData("EXPECTED", CiToken.Pending)]
    [InlineData("success", CiToken.None)]
    [InlineData(null, CiToken.None)]
    public void RollupStatesMapToCiTokens(string? state, string expected) => Assert.Equal(expected, GitHubParse.Ci(state));

    [Theory]
    [InlineData("""{"login":"octo-cat"}""", "octo-cat")]
    [InlineData("""{"login":"evil name"}""", null)]
    [InlineData("""{"login":""}""", null)]
    [InlineData("""{"login":"a234567890123456789012345678901234567890"}""", null)]
    [InlineData("""{"id":1}""", null)]
    public void TheLoginMustLookLikeOne(string body, string? expected) => Assert.Equal(expected, GitHubParse.Login(Bytes(body)));

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>A reply for a plain account whose "mine" search lists <paramref name="nodes"/>, comma-separated JSON.</summary>
    private static string Mine(string nodes) =>
        """{"data":{"viewer":{"login":"a"},"mine":{"nodes":[NODES]}}}""".Replace("NODES", nodes, StringComparison.Ordinal);
}
