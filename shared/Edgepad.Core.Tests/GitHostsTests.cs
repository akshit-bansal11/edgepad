using System.Globalization;
using System.Net;
using System.Text;
using Edgepad.Dashboard;
using Xunit;

namespace Edgepad.Tests;

public sealed class GitHostsTests : IDisposable
{
    private const string Token = "pat_NeverLoggedNeverSent123456";

    private const string GiteaUser = """{"id":7,"login":"ana","full_name":"Ana B","followers_count":3}""";

    private const string GiteaRepos = """
        {"ok":true,"data":[
          {"full_name":"ana/old","private":false,"language":"Go","stars_count":1,"open_issues_count":2,"open_pr_counter":0,"html_url":"https://codeberg.org/ana/old","updated_at":"2026-01-01T00:00:00Z"},
          {"full_name":"ana/new","private":true,"language":"Rust","stars_count":5,"open_issues_count":0,"open_pr_counter":1,"html_url":"https://codeberg.org/ana/new","updated_at":"2026-11-01T00:00:00Z"}
        ]}
        """;

    private const string GiteaMine = """
        [{"number":4,"title":"Fix","state":"open","html_url":"https://codeberg.org/ana/new/pulls/4","updated_at":"2026-11-02T00:00:00Z",
          "repository":{"full_name":"ana/new"},"pull_request":{"merged":false,"draft":true}}]
        """;

    private const string GiteaAssigned = """
        [{"number":9,"title":"Bug","state":"open","html_url":"https://codeberg.org/ana/old/issues/9","updated_at":"2026-11-03T00:00:00Z",
          "repository":{"full_name":"ana/old"},"pull_request":null}]
        """;

    private const string BitbucketUser = """{"nickname":"ana","display_name":"Ana B","account_id":"557058:abc","uuid":"{1}"}""";

    private const string BitbucketWorkspaces = """{"pagelen":25,"page":1,"size":1,"values":[{"administrator":true,"workspace":{"slug":"ws","uuid":"{2}"}}]}""";

    private const string BitbucketRepos = """
        {"size":2,"values":[
          {"full_name":"ws/old","is_private":false,"language":"python","updated_on":"2026-01-01T00:00:00Z","links":{"html":{"href":"https://bitbucket.org/ws/old"}}},
          {"full_name":"ws/new","is_private":true,"language":"","updated_on":"2026-11-01T00:00:00Z","links":{"html":{"href":"https://bitbucket.org/ws/new"}}}
        ]}
        """;

    private const string BitbucketPulls = """
        {"values":[
          {"id":12,"title":"Ship","state":"OPEN","draft":true,"updated_on":"2026-11-02T00:00:00Z",
           "destination":{"repository":{"full_name":"ws/new"}},"links":{"html":{"href":"https://bitbucket.org/ws/new/pull-requests/12"}}}
        ]}
        """;

    private const string GiteeUser = """{"login":"ana","name":"Ana B","followers":4,"public_repos":9}""";

    private const string GiteeRepos = """
        [{"full_name":"ana/zh","public":true,"language":"Java","stargazers_count":8,"open_issues_count":3,"html_url":"https://gitee.com/ana/zh","updated_at":"2026-11-01T00:00:00Z"}]
        """;

    private const string GiteeIssuesMine = """
        [{"number":"I1ABCD","title":"Bug","state":"open","html_url":"https://gitee.com/ana/zh/issues/I1ABCD","updated_at":"2026-11-02T00:00:00Z","repository":{"full_name":"ana/zh"}}]
        """;

    private const string GiteeIssuesAssigned = """
        [{"number":"I2XYZ","title":"Fix","state":"open","html_url":"https://gitee.com/ana/zh/issues/I2XYZ","updated_at":"2026-11-03T00:00:00Z","repository":{"full_name":"ana/zh"}}]
        """;

    private readonly DirectoryInfo dir = Directory.CreateTempSubdirectory("edgepad-githosts");
    private readonly FakeClock clock = new();
    private readonly Secrets secrets = new();
    private readonly IntegrationSettings settings;

    public GitHostsTests()
    {
        settings = new IntegrationSettings(Path.Combine(dir.FullName, IntegrationSettings.FileName));
        secrets.Set(GitTokens.TokenName("gitea"), Token);
        secrets.Set(GitTokens.TokenName("bitbucket"), Token);
        secrets.Set(GitTokens.TokenName("gitee"), Token);
        settings.Set(GitTokens.UserKey("bitbucket"), "me@example.org");
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => dir.Delete(recursive: true);

    [Fact]
    public async Task GiteaMapsItsReplyAndOnlyEverReads()
    {
        var http = new FakeHttp(ServeGitea);
        var host = new GiteaHost(secrets, settings, http, () => clock.Now);

        var result = await host.RefreshAsync(Ct);

        Assert.Equal(new GitHubRefreshResult(GitHubResult.Ok, true), result);
        Assert.Equal("codeberg.org", host.Domain);
        Assert.Equal(
            string.Join(
                '\n',
                "H\tgitea\tana\tAna B\t12\t3\t1",
                "P\tana/new\tp\tRust\t5\t0\t1",
                "P\tana/old\t\tGo\t1\t2\t0",
                $"M\tana/new\t4\tFix\td\tnone\topen\t{Min("2026-11-02T00:00:00Z")}",
                $"A\tana/old\t9\tBug\ti\tnone\topen\t{Min("2026-11-03T00:00:00Z")}"),
            host.Page!.Body);
        Assert.Equal(
            new List<string> { "https://codeberg.org/ana/new", "https://codeberg.org/ana/old", "https://codeberg.org/ana/new/pulls/4", "https://codeberg.org/ana/old/issues/9" },
            host.Page.Urls);
        Assert.All(http.Seen, s =>
        {
            Assert.Equal(HttpMethod.Get, s.Method);
            Assert.Equal("https", s.Uri.Scheme);
            Assert.Equal("codeberg.org", s.Uri.Host);
            Assert.Equal("token " + Token, s.Header("Authorization"));
            Assert.DoesNotContain(Token, s.Uri.AbsoluteUri, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task GiteaReviewStaysEmptyWhenTheServerDoesNotKnowIt()
    {
        var http = new FakeHttp(s => s.Uri.Query.Contains("review_requested", StringComparison.Ordinal) ? FakeHttp.Status(HttpStatusCode.BadRequest) : ServeGitea(s));
        var host = new GiteaHost(secrets, settings, http, () => clock.Now);

        Assert.Equal(GitHubResult.Ok, (await host.RefreshAsync(Ct)).Result);
        Assert.DoesNotContain("\nR\t", "\n" + host.Page!.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GiteaTalksToTheConfiguredHttpsHostOnly()
    {
        settings.Set(GitTokens.UrlKey("gitea"), "https://git.example.org/forge/");
        var http = new FakeHttp(ServeGitea);
        var host = new GiteaHost(secrets, settings, http, () => clock.Now);

        Assert.Equal(GitHubResult.Ok, (await host.RefreshAsync(Ct)).Result);
        Assert.Equal("git.example.org", host.Domain);
        Assert.All(http.Seen, s =>
        {
            Assert.Equal("git.example.org", s.Uri.Host);
            Assert.StartsWith("/forge/api/v1/", s.Uri.AbsolutePath, StringComparison.Ordinal);
        });

        // The replies name codeberg.org, which is not this host's domain, so none of their links may ever be opened.
        Assert.All(host.Page!.Urls, url => Assert.Equal(string.Empty, url));
    }

    [Theory]
    [InlineData("http://git.example.org")]
    [InlineData("ftp://git.example.org")]
    [InlineData("git.example.org")]
    [InlineData("//git.example.org")]
    [InlineData("https://user:pass@git.example.org")]
    [InlineData("https://git.example.org/?a=b")]
    [InlineData("https://git.example.org/#x")]
    [InlineData("javascript:alert(1)")]
    public async Task ANonHttpsOrOddBaseUrlIsRejectedAndNothingIsAskedOrSaved(string url)
    {
        Assert.Null(GitTokens.GiteaApi(url));
        var http = new FakeHttp(ServeGitea);
        var host = new GiteaHost(new Secrets(), settings, http, () => clock.Now);

        var saved = await host.SaveAsync(Token, url, string.Empty, Ct);

        Assert.Equal(GitSaveStatus.BadUrl, saved.Status);
        Assert.Empty(http.Seen);
        Assert.Equal(string.Empty, settings.Text(GitTokens.UrlKey("gitea")));
    }

    [Fact]
    public void AGoodBaseUrlBecomesTheApiRoot()
    {
        Assert.Equal("https://git.example.org/forge/api/v1/", GitTokens.GiteaApi(" https://git.example.org/forge/ ")!.AbsoluteUri);
        Assert.Equal("https://codeberg.org/api/v1/", GitTokens.GiteaApi("https://codeberg.org")!.AbsoluteUri);
    }

    [Fact]
    public async Task BitbucketShowsWhatItHasAndLeavesTheRestEmpty()
    {
        var http = new FakeHttp(ServeBitbucket);
        var host = new BitbucketHost(secrets, settings, http, () => clock.Now);

        Assert.Equal(GitHubResult.Ok, (await host.RefreshAsync(Ct)).Result);

        Assert.Equal(
            string.Join(
                '\n',
                "H\tbitbucket\tana\tAna B\t2\t0\t1",
                "P\tws/new\tp\t\t0\t0\t0",
                "P\tws/old\t\tpython\t0\t0\t0",
                $"M\tws/new\t12\tShip\td\tnone\topen\t{Min("2026-11-02T00:00:00Z")}"),
            host.Page!.Body);
        Assert.Equal(new List<string> { "https://bitbucket.org/ws/new", "https://bitbucket.org/ws/old", "https://bitbucket.org/ws/new/pull-requests/12" }, host.Page.Urls);
        var basic = "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes("me@example.org:" + Token));
        Assert.All(http.Seen, s =>
        {
            Assert.Equal(HttpMethod.Get, s.Method);
            Assert.Equal("api.bitbucket.org", s.Uri.Host);
            Assert.Equal(basic, s.Header("Authorization"));
        });
        Assert.Contains(http.Seen, s => s.Uri.AbsolutePath == "/2.0/workspaces/ws/pullrequests/557058%3Aabc" || s.Uri.AbsolutePath == "/2.0/workspaces/ws/pullrequests/557058:abc");
    }

    [Fact]
    public async Task BitbucketWithoutAUserNameAsksNobody()
    {
        settings.Set(GitTokens.UserKey("bitbucket"), string.Empty);
        var http = new FakeHttp(ServeBitbucket);
        var host = new BitbucketHost(secrets, settings, http, () => clock.Now);

        Assert.Equal(GitHubResult.NotSignedIn, (await host.RefreshAsync(Ct)).Result);
        Assert.Empty(http.Seen);
    }

    [Fact]
    public async Task GiteeShowsTheProfileAndRepositoriesAndNeverInventsItems()
    {
        var http = new FakeHttp(ServeGitee);
        var host = new GiteeHost(secrets, settings, http, () => clock.Now);

        Assert.Equal(GitHubResult.Ok, (await host.RefreshAsync(Ct)).Result);

        Assert.Equal(
            string.Join(
                '\n',
                "H\tgitee\tana\tAna B\t9\t4\t1",
                "P\tana/zh\t\tJava\t8\t3\t0",
                $"M\tana/zh\tI1ABCD\tBug\ti\tnone\topen\t{Min("2026-11-02T00:00:00Z")}",
                $"A\tana/zh\tI2XYZ\tFix\ti\tnone\topen\t{Min("2026-11-03T00:00:00Z")}"),
            host.Page!.Body);
        Assert.Equal(new List<string> { "https://gitee.com/ana/zh", "https://gitee.com/ana/zh/issues/I1ABCD", "https://gitee.com/ana/zh/issues/I2XYZ" }, host.Page.Urls);
        Assert.All(http.Seen, s =>
        {
            Assert.Equal(HttpMethod.Get, s.Method);
            Assert.Equal("gitee.com", s.Uri.Host);
            Assert.Null(s.Header("Authorization"));
            Assert.Contains("access_token=" + Token, s.Uri.Query, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task AFailingGiteeRequestNeverPutsItsAddressOrTokenInAnErrorOrTheLog()
    {
        var http = new FakeHttp(s => throw new HttpRequestException("failed " + s.Uri.AbsoluteUri));
        var host = new GiteeHost(secrets, settings, http, () => clock.Now);
        Assert.Equal(GitHubResult.Failed, (await host.RefreshAsync(Ct)).Result);

        using var refused = new HttpRequestMessage(HttpMethod.Put, "https://gitee.com/api/v5/user?access_token=" + Token);
        var gate = new ReadOnlyViolation("x");
        using (var client = new HttpClient(new ReadOnlyGate(new FakeHttp(_ => FakeHttp.Status(HttpStatusCode.OK)))))
        {
            gate = await Assert.ThrowsAsync<ReadOnlyViolation>(() => client.SendAsync(refused, Ct));
        }

        Assert.DoesNotContain(Token, gate.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("access_token", gate.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Token, LogText.Read(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("POST")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task TheGateRefusesEveryWritingRequestToEveryHost(string method)
    {
        foreach (var url in new[] { "https://codeberg.org/api/v1/repos/a/b/issues", "https://api.bitbucket.org/2.0/repositories/a/b", "https://gitee.com/api/v5/repos/a/b/issues" })
        {
            var http = new FakeHttp(_ => FakeHttp.Json("{}"));
            using var request = new HttpRequestMessage(new HttpMethod(method), url + "?access_token=" + Token) { Content = new StringContent("{}") };

            var error = await Assert.ThrowsAsync<ReadOnlyViolation>(() => CappedHttp.SendAsync(request, 1024, TimeSpan.FromSeconds(5), http, "test", Ct));

            Assert.Empty(http.Seen);
            Assert.DoesNotContain(Token, error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task APlainHttpRequestIsRefusedByTheGate()
    {
        var http = new FakeHttp(_ => FakeHttp.Json("{}"));
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://codeberg.org/api/v1/user");
        await Assert.ThrowsAsync<ReadOnlyViolation>(() => CappedHttp.SendAsync(request, 1024, TimeSpan.FromSeconds(5), http, "test", Ct));
        Assert.Empty(http.Seen);
    }

    [Fact]
    public async Task A401DeletesTheTokenAndClearsThePage()
    {
        var calls = 0;
        var http = new FakeHttp(s => ++calls <= 2 ? ServeGitee(s) : FakeHttp.Status(HttpStatusCode.Unauthorized));
        var host = new GiteeHost(secrets, settings, http, () => clock.Now);
        Assert.Equal(GitHubResult.Ok, (await host.RefreshAsync(Ct)).Result);
        Assert.NotNull(host.Page);

        clock.Advance(TimeSpan.FromSeconds(61));
        var result = await host.RefreshAsync(Ct);

        Assert.Equal(new GitHubRefreshResult(GitHubResult.NoPermission, true), result);
        Assert.Null(secrets.Get(GitTokens.TokenName("gitee")));
        Assert.Null(host.Page);
        Assert.Equal(GitHubResult.NotSignedIn, (await host.RefreshAsync(Ct)).Result);
    }

    [Fact]
    public async Task A403WithoutARetryAfterKeepsTheTokenAndReportsNoPermission()
    {
        var http = new FakeHttp(_ => FakeHttp.Status(HttpStatusCode.Forbidden));
        var host = new GiteaHost(secrets, settings, http, () => clock.Now);

        Assert.Equal(GitHubResult.NoPermission, (await host.RefreshAsync(Ct)).Result);
        Assert.Equal(Token, secrets.Get(GitTokens.TokenName("gitea")));
    }

    [Fact]
    public async Task A429BacksOffForAtLeastAMinuteAndNeverLongerThanAnHour()
    {
        var http = new FakeHttp(_ => FakeHttp.Status(HttpStatusCode.TooManyRequests));
        var host = new BitbucketHost(secrets, settings, http, () => clock.Now);

        Assert.Equal(GitHubResult.RateLimited, (await host.RefreshAsync(Ct)).Result);
        Assert.Single(http.Seen);

        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(GitHubResult.RateLimited, (await host.RefreshAsync(Ct)).Result);
        Assert.Single(http.Seen);

        clock.Advance(TimeSpan.FromSeconds(2));
        await host.RefreshAsync(Ct);
        Assert.Equal(2, http.Seen.Count);
    }

    [Fact]
    public async Task AFailurePartWayKeepsTheLastGoodPage()
    {
        var fail = false;
        var http = new FakeHttp(s => fail && s.Uri.AbsolutePath.EndsWith("/user/repos", StringComparison.Ordinal) ? FakeHttp.Status(HttpStatusCode.InternalServerError) : ServeGitee(s));
        var host = new GiteeHost(secrets, settings, http, () => clock.Now);
        await host.RefreshAsync(Ct);
        var good = host.Page;

        fail = true;
        clock.Advance(TimeSpan.FromSeconds(61));
        var result = await host.RefreshAsync(Ct);

        Assert.Equal(new GitHubRefreshResult(GitHubResult.Failed, false), result);
        Assert.Same(good, host.Page);
    }

    [Fact]
    public async Task ARefreshWithinAMinuteOfTheLastAsksNobodyAndOneAfterItDoes()
    {
        var http = new FakeHttp(ServeGitea);
        var host = new GiteaHost(secrets, settings, http, () => clock.Now);
        Assert.Equal(GitHubResult.Ok, (await host.RefreshAsync(Ct)).Result);
        var asked = http.Seen.Count;

        // What a phone toggling the watch bit does: a restarted timer fires at once, again and again.
        for (var i = 0; i < 5; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(10));
            Assert.Equal(GitHubResult.RateLimited, (await host.RefreshAsync(Ct)).Result);
        }

        Assert.Equal(asked, http.Seen.Count);

        clock.Advance(TimeSpan.FromSeconds(11));
        Assert.Equal(GitHubResult.Ok, (await host.RefreshAsync(Ct)).Result);
        Assert.True(http.Seen.Count > asked);
    }

    [Fact]
    public async Task TheRefreshRightAfterASuccessfulSaveIsNotHeldBack()
    {
        var http = new FakeHttp(ServeGitea);
        var host = new GiteaHost(secrets, settings, http, () => clock.Now);
        Assert.Equal(GitHubResult.Ok, (await host.RefreshAsync(Ct)).Result);

        Assert.Equal(GitSaveStatus.Saved, (await host.SaveAsync(Token, "https://codeberg.org", string.Empty, Ct)).Status);
        var asked = http.Seen.Count;

        Assert.Equal(GitHubResult.Ok, (await host.RefreshAsync(Ct)).Result);
        Assert.True(http.Seen.Count > asked);
    }

    [Fact]
    public async Task AFailedTokenSaveLeavesNoTokenBesideTheNewAddress()
    {
        settings.Set(GitTokens.UrlKey("gitea"), "https://old.example.org");
        var http = new FakeHttp(ServeGitea);
        var host = new GiteaHost(secrets, settings, http, () => clock.Now);
        secrets.FailSets = true;

        var saved = await host.SaveAsync("pat_AnotherTokenForTheNewHost12345", "https://new.example.org", string.Empty, Ct);

        Assert.Equal(GitSaveStatus.Failed, saved.Status);
        Assert.Null(secrets.Get(GitTokens.TokenName("gitea")));
        Assert.False(host.IsSignedIn);

        // The next refresh finds no token to send to anyone.
        var before = http.Seen.Count;
        Assert.Equal(GitHubResult.NotSignedIn, (await host.RefreshAsync(Ct)).Result);
        Assert.Equal(before, http.Seen.Count);
    }

    [Fact]
    public async Task A401ForAnOldTokenKeepsANewerOneSavedWhileTheRequestWasOut()
    {
        const string newer = "pat_NewerTokenValue1234567890abc";
        var http = new FakeHttp(_ =>
        {
            secrets.Set(GitTokens.TokenName("gitee"), newer);
            return FakeHttp.Status(HttpStatusCode.Unauthorized);
        });
        var host = new GiteeHost(secrets, settings, http, () => clock.Now);

        Assert.Equal(GitHubResult.NoPermission, (await host.RefreshAsync(Ct)).Result);

        Assert.Equal(newer, secrets.Get(GitTokens.TokenName("gitee")));
    }

    [Theory]
    [InlineData("https://evil.example%23.codeberg.org")]
    [InlineData("https://evil.example%2f.codeberg.org/forge")]
    [InlineData("https://codeberg.org%40evil.example")]
    public async Task AnAddressWhoseHostHasAPercentIsRefusedBeforeAnyRequest(string url)
    {
        Assert.Null(GitTokens.GiteaApi(url));
        var http = new FakeHttp(ServeGitea);
        var host = new GiteaHost(new Secrets(), settings, http, () => clock.Now);

        Assert.Equal(GitSaveStatus.BadUrl, (await host.SaveAsync(Token, url, string.Empty, Ct)).Status);
        Assert.Empty(http.Seen);
    }

    [Fact]
    public async Task ALongLoginIsCutBeforeItIsShownOrKept()
    {
        var http = new FakeHttp(_ => FakeHttp.Json("{\"id\":7,\"login\":\"" + new string('a', 100) + "\"}"));
        var host = new GiteaHost(new Secrets(), settings, http, () => clock.Now);

        var saved = await host.SaveAsync(Token, "https://codeberg.org", string.Empty, Ct);

        Assert.Equal(GitSaveStatus.Saved, saved.Status);
        Assert.Equal(39, saved.Login.Length);
    }

    [Fact]
    public async Task NoTokenAsksNobody()
    {
        var http = new FakeHttp(ServeGitea);
        var host = new GiteaHost(new Secrets(), settings, http, () => clock.Now);

        Assert.Equal(GitHubResult.NotSignedIn, (await host.RefreshAsync(Ct)).Result);
        Assert.Empty(http.Seen);
    }

    [Theory]
    [InlineData(401, "Unauthorized")]
    [InlineData(403, "Forbidden")]
    [InlineData(404, "NotFound")]
    [InlineData(500, "Failed")]
    public async Task SavingChecksTheTokenAndMapsEachRefusal(int code, string name)
    {
        var expected = Enum.Parse<GitSaveStatus>(name);
        var store = new Secrets();
        var http = new FakeHttp(_ => FakeHttp.Status((HttpStatusCode)code));
        var host = new GiteeHost(store, settings, http, () => clock.Now);

        var saved = await host.SaveAsync(Token, string.Empty, string.Empty, Ct);

        Assert.Equal(new GitSaveResult(expected, string.Empty), saved);
        Assert.Null(store.Get(GitTokens.TokenName("gitee")));
        Assert.Single(http.Seen);
        Assert.Equal(HttpMethod.Get, http.Seen[0].Method);
    }

    [Fact]
    public async Task SavingWhenTheNetworkIsDownSaysSoAndKeepsTheTokenOutOfTheLog()
    {
        var store = new Secrets();
        var http = new FakeHttp(_ => throw new HttpRequestException("No route to host"));
        var host = new GiteeHost(store, settings, http, () => clock.Now);

        var saved = await host.SaveAsync(Token, string.Empty, string.Empty, Ct);

        Assert.Equal(GitSaveStatus.Network, saved.Status);
        Assert.Null(store.Get(GitTokens.TokenName("gitee")));
        Assert.DoesNotContain(Token, LogText.Read(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARefreshWhenTheNetworkIsDownKeepsTheTokenOutOfTheLog()
    {
        var http = new FakeHttp(_ => throw new HttpRequestException("No route to host"));
        foreach (TokenGitHost host in new TokenGitHost[] { new GiteaHost(secrets, settings, http), new BitbucketHost(secrets, settings, http), new GiteeHost(secrets, settings, http) })
        {
            Assert.Equal(GitHubResult.Failed, (await host.RefreshAsync(Ct)).Result);
        }

        Assert.DoesNotContain(Token, LogText.Read(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASavedTokenGoesToTheSecretStoreAndNeverToTheSettingsFile()
    {
        var store = new Secrets();
        var http = new FakeHttp(ServeGitea);
        var host = new GiteaHost(store, settings, http, () => clock.Now);

        var saved = await host.SaveAsync(" " + Token + " ", "https://codeberg.org/", string.Empty, Ct);

        Assert.Equal(new GitSaveResult(GitSaveStatus.Saved, "ana"), saved);
        Assert.Equal(Token, store.Get(GitTokens.TokenName("gitea")));
        Assert.Equal("https://codeberg.org", settings.Text(GitTokens.UrlKey("gitea")));
        Assert.DoesNotContain(Token, await File.ReadAllTextAsync(Path.Combine(dir.FullName, IntegrationSettings.FileName), Ct), StringComparison.Ordinal);
        Assert.True(host.IsSignedIn);

        host.SignOut();
        Assert.False(host.IsSignedIn);
        Assert.Null(host.Page);
    }

    [Fact]
    public async Task BitbucketSavingNeedsAUserNameAndKeepsItInSettingsOnly()
    {
        var store = new Secrets();
        var http = new FakeHttp(ServeBitbucket);
        var host = new BitbucketHost(store, settings, http, () => clock.Now);

        Assert.Equal(GitSaveStatus.BadUser, (await host.SaveAsync(Token, string.Empty, "a:b", Ct)).Status);
        Assert.Equal(GitSaveStatus.BadUser, (await host.SaveAsync(Token, string.Empty, string.Empty, Ct)).Status);
        Assert.Empty(http.Seen);

        Assert.Equal(GitSaveStatus.Saved, (await host.SaveAsync(Token, string.Empty, "you@example.org", Ct)).Status);
        Assert.Equal("you@example.org", settings.Text(GitTokens.UserKey("bitbucket")));
        Assert.Equal(Token, store.Get(GitTokens.TokenName("bitbucket")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("has space in it")]
    [InlineData("tab\there123")]
    public async Task ABadTokenIsRefusedBeforeAnyRequest(string token)
    {
        var http = new FakeHttp(ServeGitee);
        var host = new GiteeHost(new Secrets(), settings, http, () => clock.Now);

        Assert.Equal(GitSaveStatus.BadToken, (await host.SaveAsync(token, string.Empty, string.Empty, Ct)).Status);
        Assert.Empty(http.Seen);
    }

    private static long Min(string iso) => DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture).ToUnixTimeSeconds() / 60;

    private static HttpResponseMessage ServeGitea(SeenRequest s)
    {
        // Any path prefix: a Forgejo behind a sub-path answers under /forge/api/v1.
        var path = s.Uri.AbsolutePath;
        switch (path[path.IndexOf("/api/v1", StringComparison.Ordinal)..])
        {
            case "/api/v1/user":
                return FakeHttp.Json(GiteaUser);
            case "/api/v1/repos/search":
                var reply = FakeHttp.Json(GiteaRepos);
                reply.Headers.Add("x-total-count", "12");
                return reply;
            case "/api/v1/repos/issues/search":
                var query = s.Uri.Query;
                return FakeHttp.Json(
                    query.Contains("created=true", StringComparison.Ordinal) ? GiteaMine
                    : query.Contains("assigned=true", StringComparison.Ordinal) ? GiteaAssigned
                    : "[]");
            default:
                throw new InvalidOperationException($"Asked {s.Uri}");
        }
    }

    private static HttpResponseMessage ServeBitbucket(SeenRequest s) => s.Uri.AbsolutePath switch
    {
        "/2.0/user" => FakeHttp.Json(BitbucketUser),
        "/2.0/user/workspaces" => FakeHttp.Json(BitbucketWorkspaces),
        "/2.0/repositories/ws" => FakeHttp.Json(BitbucketRepos),
        _ when s.Uri.AbsolutePath.StartsWith("/2.0/workspaces/ws/pullrequests/", StringComparison.Ordinal) => FakeHttp.Json(BitbucketPulls),
        _ => throw new InvalidOperationException($"Asked {s.Uri}"),
    };

    private static HttpResponseMessage ServeGitee(SeenRequest s) => s.Uri.AbsolutePath switch
    {
        "/api/v5/user" => FakeHttp.Json(GiteeUser),
        "/api/v5/user/repos" => FakeHttp.Json(GiteeRepos),
        "/api/v5/user/issues" => FakeHttp.Json(s.Uri.Query.Contains("filter=created", StringComparison.Ordinal) ? GiteeIssuesMine : GiteeIssuesAssigned),
        _ => throw new InvalidOperationException($"Asked {s.Uri}"),
    };

    private sealed class Secrets : ISecretStore
    {
        private readonly Dictionary<string, string> values = [];

        public bool FailSets { get; set; }

        public string? Get(string name) => values.GetValueOrDefault(name);

        public bool Set(string name, string value)
        {
            if (FailSets)
            {
                return false;
            }

            values[name] = value;
            return true;
        }

        public void Delete(string name) => values.Remove(name);
    }
}
