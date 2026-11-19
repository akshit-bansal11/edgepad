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
          "review":{"nodes":[
            {"number":12,"title":"Fix the pairing timeout","isDraft":false,"repository":{"nameWithOwner":"octo/edgepad"},
             "commits":{"nodes":[{"commit":{"statusCheckRollup":{"state":"SUCCESS"}}}]}},
            {"number":7,"title":"Draft idea","isDraft":true,"repository":{"nameWithOwner":"octo/other.repo"},
             "commits":{"nodes":[{"commit":{"statusCheckRollup":null}}]}}
          ]},
          "mine":{"nodes":[
            {"number":3,"title":"Mine","isDraft":false,"repository":{"nameWithOwner":"me/my_repo-2"},
             "commits":{"nodes":[{"commit":{"statusCheckRollup":{"state":"FAILURE"}}}]}},
            {}
          ]}
        }}
        """;

    [Fact]
    public void ANormalReplyGivesReviewThenMineLines()
    {
        var lines = GitHubParse.Pulls(Bytes(Normal));
        Assert.Equal(
            new List<GitHubLine>
            {
                new("R", "octo/edgepad", 12, "Fix the pairing timeout", false, CiToken.Ok),
                new("R", "octo/other.repo", 7, "Draft idea", true, CiToken.None),
                new("M", "me/my_repo-2", 3, "Mine", false, CiToken.Fail),
            },
            lines);
    }

    [Fact]
    public void AnEmptyReplyGivesNoLines() =>
        Assert.Empty(GitHubParse.Pulls(Bytes("""{"data":{"review":{"nodes":[]},"mine":{"nodes":[]}}}"""))!);

    [Theory]
    [InlineData("""{"errors":[{"message":"Bad credentials"}]}""")]
    [InlineData("""{"data":null}""")]
    [InlineData("""[]""")]
    [InlineData("""not json""")]
    [InlineData("")]
    public void AReplyWithoutDataIsNull(string body) => Assert.Null(GitHubParse.Pulls(Bytes(body)));

    [Fact]
    public void HostileTitlesAreCleanedAndCut()
    {
        var title = "Tab" + Tab + "here" + Esc + "[31mred" + Esc + "[0m new" + Lf + "line " + Rlo + "evil" + new string('x', 100);
        var body = Mine("""{"number":1,"title":"TITLE","isDraft":false,"repository":{"nameWithOwner":"a/b"}}""".Replace("TITLE", title, StringComparison.Ordinal));

        var line = Assert.Single(GitHubParse.Pulls(Bytes(body))!);
        Assert.DoesNotContain(line.Title, c => char.IsControl(c) || c == (char)0x202E);
        Assert.StartsWith("Tab here [31mred [0m new line evil", line.Title, StringComparison.Ordinal);
        Assert.Equal(GitHubParse.MaxTitle, line.Title.Length);
        Assert.Equal(CiToken.None, line.Ci);
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
        var body = Mine("""{"number":1,"title":"x","repository":{"nameWithOwner":"REPO"}},{"number":2,"title":"y","repository":{"nameWithOwner":"ok/fine"}}"""
            .Replace("REPO", repo.Replace("\t", Tab, StringComparison.Ordinal), StringComparison.Ordinal));
        var line = Assert.Single(GitHubParse.Pulls(Bytes(body))!);
        Assert.Equal("ok/fine", line.Repo);
    }

    [Theory]
    [InlineData("""{"number":0,"title":"x","repository":{"nameWithOwner":"a/b"}}""")]
    [InlineData("""{"number":-4,"title":"x","repository":{"nameWithOwner":"a/b"}}""")]
    [InlineData("""{"number":"5","title":"x","repository":{"nameWithOwner":"a/b"}}""")]
    [InlineData("""{"title":"x","repository":{"nameWithOwner":"a/b"}}""")]
    [InlineData("""{"number":5,"title":"x","repository":null}""")]
    [InlineData("""17""")]
    public void ARowMissingItsNumberOrRepositoryIsDropped(string node) =>
        Assert.Empty(GitHubParse.Pulls(Bytes(Mine(node)))!);

    [Fact]
    public void AtMostTenRowsPerSectionAreKept()
    {
        var nodes = string.Join(",", Enumerable.Range(1, 15).Select(i => """{"number":N,"title":"t","repository":{"nameWithOwner":"a/b"}}""".Replace("N", $"{i}", StringComparison.Ordinal)));
        Assert.Equal(GitHubBlob.MaxPerSection, GitHubParse.Pulls(Bytes(Mine(nodes)))!.Count);
    }

    [Fact]
    public void AReplyNestedPastTheLimitIsRefused()
    {
        var deep = new string('[', 40) + new string(']', 40);
        Assert.Null(GitHubParse.Pulls(Bytes(Mine(deep))));
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

    [Fact]
    public void TheUnreadCountIsTheListLengthCappedAtFifty()
    {
        Assert.Equal(2, GitHubParse.Unread(Bytes("""[{"id":"1","unread":true},{"id":"2","unread":true}]""")));
        Assert.Equal(0, GitHubParse.Unread(Bytes("[]")));
        Assert.Equal(50, GitHubParse.Unread(Bytes("[" + string.Join(",", Enumerable.Repeat("{}", 60)) + "]")));
        Assert.Null(GitHubParse.Unread(Bytes("""{"message":"x"}""")));
    }

    [Theory]
    [InlineData("""{"login":"octo-cat"}""", "octo-cat")]
    [InlineData("""{"login":"evil name"}""", null)]
    [InlineData("""{"login":""}""", null)]
    [InlineData("""{"login":"a234567890123456789012345678901234567890"}""", null)]
    [InlineData("""{"id":1}""", null)]
    public void TheLoginMustLookLikeOne(string body, string? expected) => Assert.Equal(expected, GitHubParse.Login(Bytes(body)));

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>A reply whose "mine" search lists <paramref name="nodes"/>, comma-separated JSON.</summary>
    private static string Mine(string nodes) => """{"data":{"mine":{"nodes":[NODES]}}}""".Replace("NODES", nodes, StringComparison.Ordinal);
}
