using System.Net;
using System.Text;
using Edgepad.Dashboard;
using Edgepad.Protocol;
using Xunit;

namespace Edgepad.Tests;

public sealed class GitTests
{
    private const string Query = """{"query":"query { viewer { login } }"}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- ReadOnlyGate

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task TheGateRefusesEveryWritingMethod(string method)
    {
        var (http, error) = await Send(new HttpMethod(method), "https://api.github.com/repos/o/r/issues/1", "{}");
        Assert.IsType<ReadOnlyViolation>(error);
        Assert.Empty(http.Seen);
    }

    [Theory]
    [InlineData("""{"query":"mutation { addStar(input: {starrableId: \"x\"}) { clientMutationId } }"}""")]
    [InlineData("""{"query":"  MUTATION M { x }"}""")]
    [InlineData("""{"query":"mutation { x }"}""")]
    [InlineData("""{"query":"query { a } mutation { b }"}""")]
    [InlineData("""{"operationName":"x"}""")]
    [InlineData("""{"query":7}""")]
    [InlineData("""[]""")]
    [InlineData("""not json""")]
    [InlineData("")]
    public async Task TheGateRefusesAGraphQlPostThatCouldMutateOrIsNotAQuery(string body)
    {
        var (http, error) = await Send(HttpMethod.Post, "https://api.github.com/graphql", body);
        Assert.IsType<ReadOnlyViolation>(error);
        Assert.Empty(http.Seen);
    }

    [Fact]
    public async Task TheGateAllowsAGraphQlQueryAndOurOwnQueryHasNoMutation()
    {
        var (http, error) = await Send(HttpMethod.Post, "https://api.github.com/graphql", Query);
        Assert.Null(error);
        Assert.Single(http.Seen);

        var (own, ownError) = await Send(HttpMethod.Post, "https://api.github.com/graphql", Encoding.UTF8.GetString(GitHubApi.QueryBody()));
        Assert.Null(ownError);
        Assert.Single(own.Seen);
    }

    [Theory]
    [InlineData("GET", "https://api.github.com/user")]
    [InlineData("HEAD", "https://api.github.com/user")]
    [InlineData("GET", "https://gitlab.com/api/v4/user")]
    public async Task TheGateAllowsReading(string method, string url)
    {
        var (http, error) = await Send(new HttpMethod(method), url, null);
        Assert.Null(error);
        Assert.Single(http.Seen);
    }

    [Theory]
    [InlineData("https://github.com/login/device/code")]
    [InlineData("https://github.com/login/oauth/access_token")]
    [InlineData("https://gitlab.com/oauth/authorize_device")]
    [InlineData("https://gitlab.com/oauth/token")]
    public async Task TheGateAllowsTheDeviceFlowPosts(string url)
    {
        var (http, error) = await Send(HttpMethod.Post, url, "client_id=x");
        Assert.Null(error);
        Assert.Single(http.Seen);
    }

    [Theory]
    [InlineData("POST", "https://github.com/login/oauth/authorize")]
    [InlineData("POST", "https://evil.example/login/device/code")]
    [InlineData("POST", "https://evil.example/oauth/token")]
    [InlineData("POST", "https://api.github.com/graphql/extra")]
    [InlineData("POST", "https://evil.example/graphql")]
    [InlineData("PUT", "https://github.com/login/device/code")]
    [InlineData("POST", "http://github.com/login/device/code")]
    [InlineData("GET", "http://api.github.com/user")]
    public async Task TheGateRefusesAnythingElse(string method, string url)
    {
        var (http, error) = await Send(new HttpMethod(method), url, Query);
        Assert.IsType<ReadOnlyViolation>(error);
        Assert.Empty(http.Seen);
    }

    [Fact]
    public async Task TheRefusalNamesNoQueryString()
    {
        var (_, error) = await Send(HttpMethod.Delete, "https://api.github.com/repos/o/r?token=SECRET", null);
        Assert.DoesNotContain("SECRET", Assert.IsType<ReadOnlyViolation>(error).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryDashboardRequestPassesTheGate()
    {
        var http = new FakeHttp(_ => FakeHttp.Json("{}"));
        using var request = new HttpRequestMessage(HttpMethod.Put, "https://api.github.com/user/starred/o/r");
        await Assert.ThrowsAsync<ReadOnlyViolation>(() => CappedHttp.SendAsync(request, 1024, TimeSpan.FromSeconds(5), http, "test", Ct));
        Assert.Empty(http.Seen);
    }

    // ---- GitBlob

    [Fact]
    public void ThePageHasTheHeaderThenReposThenTheGroupsAndOneAddressPerIndexedLine()
    {
        var page = GitBlob.Encode("github", Snapshot(), 5);
        var rows = page.Body.Split('\n').Select(r => r.Split('\t')).ToList();

        Assert.Equal(["H", "github", "octo", "Octo Cat", "34", "12", "5"], rows[0]);
        Assert.Equal(["P", "octo/edgepad", "p", "C#", "5", "2", "1"], rows[1]);
        Assert.Equal(["M", "octo/edgepad", "3", "Fix it", "d", CiToken.Fail, GitState.Merged, "99"], rows[2]);
        Assert.Equal(["A", "octo/edgepad", "4", "Bug", "i", CiToken.None, GitState.Open, "0"], rows[3]);
        Assert.Equal(["https://github.com/octo/edgepad", "https://github.com/octo/edgepad/pull/3", "https://github.com/octo/edgepad/issues/4"], page.Urls);
        Assert.Equal(5, page.Rev);
        Assert.DoesNotContain("https", page.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnreadCountIsAnOlderStyleLineThatDoesNotTakeAnIndex()
    {
        var page = GitBlob.Encode("github", Snapshot() with { Unread = 7 }, 1);
        Assert.Equal("N\t\t7\t\t\tnone", page.Body.Split('\n')[1]);
        Assert.Equal(3, page.Urls.Count);
    }

    [Fact]
    public void HostileTextIsCleanedAndCutAndABadRepositoryIsDropped()
    {
        var hostile = "a|b\tc\nd‮X" + new string('x', 10 * 1024);
        var snapshot = new GitSnapshot(
            new GitProfile("octo", hostile, 1, 1),
            [
                new GitRepo("bad repo/name", false, "C#", 1, 1, 1, "https://github.com/a/b"),
                new GitRepo("ok/repo", false, hostile, 1, 1, 1, "https://github.com/ok/repo"),
            ],
            [new GitItem(GitSection.Mine, "ok/repo", 1, hostile, "weird", true, false, "weird", 1, "https://github.com/ok/repo/pull/1")]);

        var page = GitBlob.Encode("github", snapshot, 1);
        var rows = page.Body.Split('\n').Select(r => r.Split('\t')).ToList();

        Assert.Equal(3, rows.Count);
        Assert.Equal(7, rows[0].Length);
        Assert.Equal(7, rows[1].Length);
        Assert.Equal(8, rows[2].Length);
        Assert.All(rows.SelectMany(r => r).Where(f => f.Length > 0), f => Assert.DoesNotContain(f, c => char.IsControl(c) || c == '‮'));
        Assert.Equal(GitLimits.MaxTitle, rows[2][3].Length);
        Assert.Equal(GitLimits.MaxLanguage, rows[1][3].Length);
        Assert.Equal(GitLimits.MaxName, rows[0][3].Length);
        Assert.Equal(CiToken.None, rows[2][5]);
        Assert.Equal(GitState.Open, rows[2][6]);
        Assert.Equal(["https://github.com/ok/repo", "https://github.com/ok/repo/pull/1"], page.Urls);
    }

    [Fact]
    public void AtMostThirtyReposAndTwentyItemsPerGroupAreSent()
    {
        var repos = Enumerable.Range(1, 50).Select(i => new GitRepo($"o/r{i}", false, "C#", i, 0, 0, $"https://github.com/o/r{i}")).ToList();
        var items = new[] { GitSection.Mine, GitSection.Review, GitSection.Assigned }
            .SelectMany(s => Enumerable.Range(1, 30).Select(i => new GitItem(s, "o/r1", i, "t", GitState.Open, true, false, CiToken.Ok, 1, $"https://github.com/o/r1/pull/{i}")))
            .ToList();

        var page = GitBlob.Encode("github", new GitSnapshot(new GitProfile("octo", string.Empty, 50, 0), repos, items), 1);
        var letters = page.Body.Split('\n').Select(r => r[0]).ToList();

        Assert.Equal(GitLimits.MaxRepos, letters.Count(c => c == 'P'));
        Assert.Equal(GitLimits.MaxItemsPerGroup, letters.Count(c => c == 'M'));
        Assert.Equal(GitLimits.MaxItemsPerGroup, letters.Count(c => c == 'R'));
        Assert.Equal(GitLimits.MaxItemsPerGroup, letters.Count(c => c == 'A'));
        Assert.Equal(GitLimits.MaxRepos + (3 * GitLimits.MaxItemsPerGroup), page.Urls.Count);
    }

    [Fact]
    public void WhenThePageIsTooBigTheLastLinesGoFirstAndEveryIndexStillHasItsAddress()
    {
        var fat = string.Concat(Enumerable.Repeat("\U0001F600", GitLimits.MaxTitle));
        var repo = "o/" + new string('r', 98);
        var repos = Enumerable.Range(1, 30).Select(i => new GitRepo(repo, false, fat, i, 0, 0, $"https://github.com/o/r{i}")).ToList();
        var items = new[] { GitSection.Mine, GitSection.Review, GitSection.Assigned }
            .SelectMany(s => Enumerable.Range(1, 20).Select(i => new GitItem(s, repo, i, fat, GitState.Open, true, false, CiToken.Ok, 1, $"https://github.com/{s}/{i}")))
            .ToList();

        var page = GitBlob.Encode("github", new GitSnapshot(new GitProfile("octo", "n", 1, 1), repos, items), 1);
        var rows = page.Body.Split('\n');

        Assert.InRange(Encoding.UTF8.GetByteCount(page.Body), 1, GitLimits.MaxBytes);
        Assert.Equal('H', rows[0][0]);
        Assert.True(rows.Length < 1 + 30 + 60);
        Assert.Equal(rows.Length - 1, page.Urls.Count);
        Assert.Equal("https://github.com/o/r1", page.Urls[0]);

        // Order is kept: nothing is skipped to make room, so a dropped line is always at the end.
        var letters = string.Concat(rows.Select(r => r[0]));
        Assert.Equal(string.Concat(letters.OrderBy(c => "HPMRA".IndexOf(c, StringComparison.Ordinal))), letters);
    }

    [Fact]
    public void TheRevisionOnlyGoesUpWhenTheContentChangesAndNeverGoesBack()
    {
        var book = new GitBook("github");
        Assert.Null(book.Page);

        var first = book.Update(Snapshot());
        Assert.Equal(1, first.Rev);
        Assert.Same(first, book.Update(Snapshot()));

        var changed = book.Update(Snapshot() with { Items = [] });
        Assert.Equal(2, changed.Rev);

        book.Clear();
        Assert.Null(book.Page);
        Assert.Equal(3, book.Update(Snapshot()).Rev);
    }

    [Fact]
    public void ANewAddressAloneIsANewRevision()
    {
        var book = new GitBook("github");
        var first = book.Update(Snapshot());
        var moved = Snapshot() with { Repos = [new GitRepo("octo/edgepad", true, "C#", 5, 2, 1, "https://github.com/octo/edgepad-renamed")] };
        Assert.Equal(first.Rev + 1, book.Update(moved).Rev);
    }

    // ---- GitOpenGate

    [Theory]
    [InlineData("github/4/0", "https://github.com/octo/edgepad")]
    [InlineData("github/4/2", "https://github.com/octo/edgepad/issues/4")]
    public void OnlyTheFetchedAddressForTheCurrentRevisionAndIndexOpens(string wire, string expected)
    {
        Assert.True(GitOpenGate.TryResolve(wire, Hosts(Page(4, "https://github.com/octo/edgepad", "https://github.com/octo/edgepad/pull/3", "https://github.com/octo/edgepad/issues/4")), out var url));
        Assert.Equal(new Uri(expected), url);
    }

    [Theory]
    [InlineData("github/3/0")]
    [InlineData("github/5/0")]
    [InlineData("github/4/1")]
    [InlineData("github/4/-1")]
    [InlineData("github/4/x")]
    [InlineData("github/x/0")]
    [InlineData("github/4/")]
    [InlineData("github/4")]
    [InlineData("github/4/0/0")]
    [InlineData("gitlab/4/0")]
    [InlineData("/4/0")]
    [InlineData("github//0")]
    [InlineData("")]
    [InlineData(null)]
    public void AStaleRevisionAnIndexOutOfRangeAnUnknownHostOrAnyOtherShapeOpensNothing(string? wire) =>
        Assert.False(GitOpenGate.TryResolve(wire, Hosts(Page(4, "https://github.com/octo/edgepad")), out _));

    [Fact]
    public void ALongTextOpensNothing() =>
        Assert.False(GitOpenGate.TryResolve("github/4/" + new string('0', 100), Hosts(Page(4, "https://github.com/octo/edgepad")), out _));

    [Theory]
    [InlineData("http://github.com/octo/edgepad")]
    [InlineData("https://evil.example/octo/edgepad")]
    [InlineData("https://github.com.evil.example/octo/edgepad")]
    [InlineData("https://user@github.com/octo/edgepad")]
    [InlineData("https://github.com:8443/octo/edgepad")]
    [InlineData("ftp://github.com/octo/edgepad")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/octo/edgepad")]
    [InlineData("")]
    public void AnAddressThatIsNotHttpsOnTheHostsOwnDomainNeverOpens(string address) =>
        Assert.False(GitOpenGate.TryResolve("github/4/0", Hosts(Page(4, address)), out _));

    [Fact]
    public void AHostWithNoPageOpensNothing() =>
        Assert.False(GitOpenGate.TryResolve("github/4/0", id => new FakeHost(id, "github.com", null), out _));

    // ---- helpers

    private static GitSnapshot Snapshot() => new(
        new GitProfile("octo", "Octo Cat", 34, 12),
        [new GitRepo("octo/edgepad", true, "C#", 5, 2, 1, "https://github.com/octo/edgepad")],
        [
            new GitItem(GitSection.Mine, "octo/edgepad", 3, "Fix it", GitState.Merged, true, true, CiToken.Fail, 99, "https://github.com/octo/edgepad/pull/3"),
            new GitItem(GitSection.Assigned, "octo/edgepad", 4, "Bug", GitState.Open, false, false, CiToken.None, 0, "https://github.com/octo/edgepad/issues/4"),
        ]);

    private static GitPage Page(long rev, params string[] urls) => new(rev, "H", urls);

    private static Func<string, IGitHost?> Hosts(GitPage page) => id => id == "github" ? new FakeHost(id, "github.com", page) : null;

    private static async Task<(FakeHttp Http, Exception? Error)> Send(HttpMethod method, string url, string? body)
    {
        var http = new FakeHttp(_ => FakeHttp.Json("{}"));
        using var client = new HttpClient(new ReadOnlyGate(http));
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        try
        {
            using var response = await client.SendAsync(request, Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (http, null);
        }
        catch (ReadOnlyViolation e)
        {
            return (http, e);
        }
    }

    private sealed class FakeHost(string id, string domain, GitPage? page) : IGitHost
    {
        public string Id => id;

        public string Domain => domain;

        public GitPage? Page => page;

        public Task<GitHubRefreshResult> RefreshAsync(CancellationToken ct) =>
            Task.FromResult(new GitHubRefreshResult(GitHubResult.Ok, false));
    }
}
