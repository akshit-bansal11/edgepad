using System.Globalization;
using System.Net;
using System.Text.Json;
using Edgepad.Dashboard;
using Xunit;

namespace Edgepad.Tests;

public sealed class GitHubApiTests
{
    private const string Token = "ghu_ApiTokenNeverLogged0987654321";
    private const string Refresh = "ghr_RefreshTokenNeverLogged1234567890";
    private const string NewToken = "ghu_RenewedTokenValue1234567890abcdef";
    private const string NewRefresh = "ghr_RenewedRefreshValue1234567890abcdef";
    private const string Renewed = """{"access_token":"ghu_RenewedTokenValue1234567890abcdef","expires_in":28800,"refresh_token":"ghr_RenewedRefreshValue1234567890abcdef","refresh_token_expires_in":15897600,"scope":"","token_type":"bearer"}""";

    private readonly FakeClock clock = new();
    private readonly Secrets secrets = new();

    public GitHubApiTests()
    {
        secrets.Set(GitHubApp.TokenName, Token);
        secrets.Set(GitHubApp.ClientName, GitHubApp.ClientId);
    }

    [Fact]
    public async Task ARefreshSendsTheConstantQueryAndNothingElse()
    {
        var http = Serve(GitHubParseTests.Normal);
        var api = New(http);

        var result = await api.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new GitHubRefreshResult(GitHubResult.Ok, true), result);
        Assert.Single(http.Seen);

        var graphql = http.Seen[0];
        Assert.DoesNotContain("mutation", GitHubApi.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpMethod.Post, graphql.Method);
        Assert.Equal("https://api.github.com/graphql", graphql.Uri.AbsoluteUri);
        using (var body = JsonDocument.Parse(graphql.Body))
        {
            Assert.Equal(GitHubApi.Query, body.RootElement.GetProperty("query").GetString());
            Assert.Single(body.RootElement.EnumerateObject());
        }

        Assert.Equal("application/json", graphql.ContentType);

        Assert.DoesNotContain(http.Seen, s => s.Uri.AbsolutePath.Contains("notifications", StringComparison.Ordinal));

        Assert.All(http.Seen, s =>
        {
            Assert.Equal("api.github.com", s.Uri.Host);
            Assert.Equal("Bearer " + Token, s.Header("Authorization"));
            Assert.Equal("application/vnd.github+json", s.Header("Accept"));
            Assert.Equal("2022-11-28", s.Header("X-GitHub-Api-Version"));
            Assert.Equal("Edgepad", s.Header("User-Agent"));
        });

        var min = GitHubParseTests.UpdatedMin;
        var page = api.Page!;
        Assert.Equal(1, page.Rev);
        Assert.Equal(
            string.Join(
                '\n',
                "H\tgithub\tocto-cat\tOcto Cat\t34\t12\t1",
                "P\tocto-cat/edgepad\t\tC#\t5\t2\t1",
                "P\tocto-cat/secret\tp\t\t0\t0\t0",
                $"M\tme/my_repo-2\t3\tMine\t\tfail\tmerged\t{min}",
                $"M\tme/my_repo-2\t9\tCrash on pair\ti\tnone\topen\t{min}",
                $"R\tocto/edgepad\t12\tFix the pairing timeout\t\tok\topen\t{min}",
                $"R\tocto/other.repo\t7\tDraft idea\td\tnone\topen\t{min}",
                "A\tocto/edgepad\t4\tAssigned to me\ti\tnone\tclosed\t0"),
            page.Body);
        Assert.Equal(
            new List<string>
            {
                "https://github.com/octo-cat/edgepad",
                "https://github.com/octo-cat/secret",
                "https://github.com/me/my_repo-2/pull/3",
                "https://github.com/me/my_repo-2/issues/9",
                "https://github.com/octo/edgepad/pull/12",
                "https://github.com/octo/other.repo/pull/7",
                string.Empty,
                "https://github.com/apps/edgepad-dashboard/installations/new",
            },
            page.Urls);
    }

    [Fact]
    public async Task TheRevisionOnlyGoesUpWhenThePageChanges()
    {
        var http = Serve(GitHubParseTests.Normal);
        var api = New(http);
        var ct = TestContext.Current.CancellationToken;

        await api.RefreshAsync(ct);
        Assert.Equal(new GitHubRefreshResult(GitHubResult.Ok, false), await api.RefreshAsync(ct));
        Assert.Equal(1, api.Page!.Rev);
    }

    [Fact]
    public async Task TheQueryNeverChanges()
    {
        Assert.Equal(GitHubApi.QueryBody(), GitHubApi.QueryBody());
        Assert.DoesNotContain("{{", GitHubApi.Query, StringComparison.Ordinal);
        var http = Serve(GitHubParseTests.Normal);
        var api = New(http);
        await api.RefreshAsync(TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromMinutes(2));
        await api.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(http.Seen[0].Body, http.Seen[1].Body);
    }

    [Fact]
    public async Task WithoutATokenNobodyIsAsked()
    {
        secrets.Delete(GitHubApp.TokenName);
        var http = Serve(GitHubParseTests.Normal);
        Assert.Equal(GitHubResult.NotSignedIn, (await New(http).RefreshAsync(TestContext.Current.CancellationToken)).Result);
        Assert.Empty(http.Seen);
    }

    [Fact]
    public async Task ATokenThatCouldInjectAHeaderIsNeverSent()
    {
        secrets.Set(GitHubApp.TokenName, "ghu_x\r\nX-Evil: 1");
        var http = Serve(GitHubParseTests.Normal);
        Assert.Equal(GitHubResult.NotSignedIn, (await New(http).RefreshAsync(TestContext.Current.CancellationToken)).Result);
        Assert.Empty(http.Seen);
    }

    [Fact]
    public async Task ATokenFromTheOldOAuthAppIsDeletedAndNobodyIsAsked()
    {
        secrets.Delete(GitHubApp.ClientName);
        var http = Serve(GitHubParseTests.Normal);

        Assert.Equal(GitHubResult.NotSignedIn, (await New(http).RefreshAsync(TestContext.Current.CancellationToken)).Result);

        Assert.Empty(http.Seen);
        Assert.Null(secrets.Get(GitHubApp.TokenName));
        Assert.True(GitHubApp.NeedsReSignIn(secrets));
    }

    [Fact]
    public void ATokenWithTheNewClientIdIsKeptAndANewSignInClearsTheNote()
    {
        GitHubApp.Migrate(secrets, GitHubApp.ClientId);
        Assert.Equal(Token, secrets.Get(GitHubApp.TokenName));
        Assert.False(GitHubApp.NeedsReSignIn(secrets));

        secrets.Delete(GitHubApp.ClientName);
        GitHubApp.Migrate(secrets, GitHubApp.ClientId);
        Assert.True(GitHubApp.NeedsReSignIn(secrets));

        Assert.True(GitHubApp.Save(secrets, GitHubApp.ClientId, Token, null, null));
        Assert.False(GitHubApp.NeedsReSignIn(secrets));
        Assert.Equal(GitHubApp.ClientId, secrets.Get(GitHubApp.ClientName));
    }

    [Fact]
    public async Task AnExpiringTokenIsRenewedBeforeItRunsOutWithNoSecretAndTheNewOneIsUsed()
    {
        Expiring(clock.Now.AddMinutes(2));
        var http = Routes(Renewed);

        var result = await New(http).RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(GitHubResult.Ok, result.Result);
        Assert.Equal(2, http.Seen.Count);
        var renew = http.Seen[0];
        Assert.Equal("https://github.com/login/oauth/access_token", renew.Uri.AbsoluteUri);
        Assert.Equal(HttpMethod.Post, renew.Method);
        Assert.Equal($"client_id={GitHubApp.ClientId}&grant_type=refresh_token&refresh_token={Refresh}", renew.Body);
        Assert.DoesNotContain("secret", renew.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Bearer " + NewToken, http.Seen[1].Header("Authorization"));
        Assert.Equal(NewToken, secrets.Get(GitHubApp.TokenName));
        Assert.Equal(NewRefresh, secrets.Get(GitHubApp.RefreshName));
        Assert.Equal(clock.Now.AddSeconds(28800).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), secrets.Get(GitHubApp.ExpiresName));
    }

    [Fact]
    public async Task ATokenWithTimeLeftIsNotRenewed()
    {
        Expiring(clock.Now.AddHours(2));
        var http = Routes(Renewed);

        await New(http).RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Single(http.Seen);
        Assert.Equal("/graphql", http.Seen[0].Uri.AbsolutePath);
    }

    [Fact]
    public async Task ARefusalIsAnsweredByOneRenewalAndOneRetryThenTheTokenGoes()
    {
        Expiring(clock.Now.AddHours(2));
        var http = new FakeHttp(s => s.Uri.AbsolutePath == "/graphql" ? FakeHttp.Status(HttpStatusCode.Unauthorized) : FakeHttp.Json(Renewed));

        var result = await New(http).RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(GitHubResult.NoPermission, result.Result);
        Assert.Equal(3, http.Seen.Count);
        Assert.Null(secrets.Get(GitHubApp.TokenName));
        Assert.Null(secrets.Get(GitHubApp.RefreshName));
    }

    [Fact]
    public async Task ARefusalThatARenewalFixesIsRetriedOnce()
    {
        Expiring(clock.Now.AddHours(2));
        var graphql = 0;
        var http = new FakeHttp(s => s.Uri.AbsolutePath == "/graphql"
            ? (++graphql == 1 ? FakeHttp.Status(HttpStatusCode.Unauthorized) : FakeHttp.Json(GitHubParseTests.Normal))
            : FakeHttp.Json(Renewed));

        Assert.Equal(GitHubResult.Ok, (await New(http).RefreshAsync(TestContext.Current.CancellationToken)).Result);
        Assert.Equal(3, http.Seen.Count);
        Assert.Equal("Bearer " + NewToken, http.Seen[2].Header("Authorization"));
    }

    [Fact]
    public async Task ARefusedRefreshTokenSignsOutWithAClearResult()
    {
        Expiring(clock.Now.AddMinutes(1));
        var http = new FakeHttp(_ => FakeHttp.Json("""{"error":"bad_refresh_token","error_description":"The refresh token passed is incorrect or expired."}"""));
        var api = New(http);

        var result = await api.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(GitHubResult.NoPermission, result.Result);
        Assert.Single(http.Seen);
        Assert.Null(secrets.Get(GitHubApp.TokenName));
        Assert.Null(secrets.Get(GitHubApp.RefreshName));
        Assert.Null(api.Page);
    }

    [Fact]
    public async Task ARenewalThatCannotReachGitHubKeepsTheTokens()
    {
        Expiring(clock.Now.AddMinutes(1));
        var http = new FakeHttp(s => s.Uri.AbsolutePath == "/graphql" ? FakeHttp.Json(GitHubParseTests.Normal) : throw new HttpRequestException("No route to host"));

        await New(http).RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Token, secrets.Get(GitHubApp.TokenName));
        Assert.Equal(Refresh, secrets.Get(GitHubApp.RefreshName));
    }

    [Fact]
    public async Task ATokenThatDoesNotExpireIsNeverRenewedAndARefusalDeletesIt()
    {
        var http = new FakeHttp(_ => FakeHttp.Status(HttpStatusCode.Unauthorized));

        Assert.Equal(GitHubResult.NoPermission, (await New(http).RefreshAsync(TestContext.Current.CancellationToken)).Result);
        Assert.Single(http.Seen);
        Assert.Equal("/graphql", http.Seen[0].Uri.AbsolutePath);
        Assert.Null(secrets.Get(GitHubApp.TokenName));
    }

    [Fact]
    public async Task ARefusedTokenIsDeletedAndTheScreenEmptied()
    {
        var refuse = false;
        var http = new FakeHttp(_ => refuse ? FakeHttp.Status(HttpStatusCode.Unauthorized) : FakeHttp.Json(GitHubParseTests.Normal));
        var api = New(http);
        var ct = TestContext.Current.CancellationToken;

        await api.RefreshAsync(ct);
        refuse = true;
        var result = await api.RefreshAsync(ct);

        Assert.Equal(new GitHubRefreshResult(GitHubResult.NoPermission, true), result);
        Assert.Null(secrets.Get(GitHubApp.TokenName));
        Assert.Null(api.Page);
        Assert.Equal(2, http.Seen.Count);
    }

    [Fact]
    public async Task ARateLimitKeepsTheLastResultAndWaitsForTheReset()
    {
        var limited = false;
        var reset = clock.Now.AddMinutes(10).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var http = new FakeHttp(s =>
        {
            if (limited)
            {
                var reply = FakeHttp.Status(HttpStatusCode.Forbidden);
                reply.Headers.Add("x-ratelimit-remaining", "0");
                reply.Headers.Add("x-ratelimit-reset", reset);
                return reply;
            }

            return FakeHttp.Json(GitHubParseTests.Normal);
        });
        var api = New(http);
        var ct = TestContext.Current.CancellationToken;

        await api.RefreshAsync(ct);
        var kept = api.Page;
        limited = true;
        Assert.Equal(GitHubResult.RateLimited, (await api.RefreshAsync(ct)).Result);
        Assert.Equal(kept, api.Page);
        Assert.Equal(2, http.Seen.Count);

        clock.Advance(TimeSpan.FromMinutes(9));
        Assert.Equal(GitHubResult.RateLimited, (await api.RefreshAsync(ct)).Result);
        Assert.Equal(2, http.Seen.Count);

        limited = false;
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(GitHubResult.Ok, (await api.RefreshAsync(ct)).Result);
        Assert.Equal(3, http.Seen.Count);
    }

    [Fact]
    public async Task ARetryAfterIsHonouredButNeverPastAnHour()
    {
        var http = new FakeHttp(_ =>
        {
            var reply = FakeHttp.Status(HttpStatusCode.TooManyRequests);
            reply.Headers.Add("Retry-After", "999999");
            return reply;
        });
        var api = New(http);
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal(GitHubResult.RateLimited, (await api.RefreshAsync(ct)).Result);
        clock.Advance(TimeSpan.FromMinutes(59));
        Assert.Equal(GitHubResult.RateLimited, (await api.RefreshAsync(ct)).Result);
        Assert.Single(http.Seen);
        clock.Advance(TimeSpan.FromMinutes(2));
        await api.RefreshAsync(ct);
        Assert.Equal(2, http.Seen.Count);
    }

    [Fact]
    public async Task AForbiddenWithoutRateLimitHeadersIsAFailureAndNoBackOff()
    {
        var http = new FakeHttp(_ => FakeHttp.Status(HttpStatusCode.Forbidden));
        var api = New(http);
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal(GitHubResult.Failed, (await api.RefreshAsync(ct)).Result);
        Assert.Equal(GitHubResult.Failed, (await api.RefreshAsync(ct)).Result);
        Assert.Equal(2, http.Seen.Count);
        Assert.NotNull(secrets.Get(GitHubApp.TokenName));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task AnyOtherErrorKeepsTheLastGoodResult(HttpStatusCode status)
    {
        var fail = false;
        var http = new FakeHttp(_ => fail
            ? (status == HttpStatusCode.Found ? FakeHttp.Redirect("https://evil.example/graphql") : FakeHttp.Status(status))
            : FakeHttp.Json(GitHubParseTests.Normal));
        var api = New(http);
        var ct = TestContext.Current.CancellationToken;

        await api.RefreshAsync(ct);
        var kept = api.Page;
        fail = true;
        Assert.Equal(new GitHubRefreshResult(GitHubResult.Failed, false), await api.RefreshAsync(ct));
        Assert.Equal(kept, api.Page);
        Assert.All(http.Seen, s => Assert.Equal("api.github.com", s.Uri.Host));
    }

    [Fact]
    public async Task AReplyOverOneMibIsRefused()
    {
        var huge = """{"data":{"mine":{"nodes":[]}},"pad":"PAD"}""".Replace("PAD", new string('x', 2 * 1024 * 1024), StringComparison.Ordinal);
        var http = new FakeHttp(_ => FakeHttp.Json(huge));
        var api = New(http);

        Assert.Equal(GitHubResult.Failed, (await api.RefreshAsync(TestContext.Current.CancellationToken)).Result);
        Assert.Null(api.Page);
    }

    [Fact]
    public async Task RefreshesNeverOverlap()
    {
        var gate = new Gate();
        var api = new GitHubApi(secrets, gate, () => clock.Now);
        var ct = TestContext.Current.CancellationToken;

        var first = api.RefreshAsync(ct);
        Assert.Equal(GitHubResult.Busy, (await api.RefreshAsync(ct)).Result);
        gate.Open.SetResult();
        Assert.NotEqual(GitHubResult.Busy, (await first).Result);
        Assert.Equal(1, gate.Calls);
    }

    [Fact]
    public async Task TheLoginIsReadOnceForDisplay()
    {
        var http = new FakeHttp(_ => FakeHttp.Json("""{"login":"octo-cat","id":1}"""));
        Assert.Equal("octo-cat", await New(http).LoginAsync(TestContext.Current.CancellationToken));
        var request = Assert.Single(http.Seen);
        Assert.Equal("https://api.github.com/user", request.Uri.AbsoluteUri);
        Assert.Equal(HttpMethod.Get, request.Method);
    }

    [Fact]
    public async Task ALoginRefusedDeletesTheToken()
    {
        var http = new FakeHttp(_ => FakeHttp.Status(HttpStatusCode.Unauthorized));
        Assert.Null(await New(http).LoginAsync(TestContext.Current.CancellationToken));
        Assert.Null(secrets.Get(GitHubApp.TokenName));
    }

    [Fact]
    public async Task ARefusalOfTheOldTokenKeepsANewerOneSavedWhileTheRequestWasOut()
    {
        const string newer = "ghu_NewerTokenValue1234567890abcdefghij";
        var http = new FakeHttp(_ =>
        {
            secrets.Set(GitHubApp.TokenName, newer);
            return FakeHttp.Status(HttpStatusCode.Unauthorized);
        });

        Assert.Null(await New(http).LoginAsync(TestContext.Current.CancellationToken));
        Assert.Equal(newer, secrets.Get(GitHubApp.TokenName));
    }

    [Fact]
    public async Task TheTokenNeverReachesTheLog()
    {
        var http = new FakeHttp(_ => throw new HttpRequestException("No route to host"));
        await New(http).RefreshAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(Token, LogText.Read(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, 0, true, true)]
    [InlineData(null, 0, false, false)]
    [InlineData(0, 59, true, false)]
    [InlineData(0, 60, true, true)]
    [InlineData(0, 600, false, false)]
    [InlineData(0, -5, true, false)]
    public void RefreshRunsOnlyWhileWatchedAndAMinuteApart(int? lastSecond, int nowSecond, bool watched, bool expected)
    {
        var start = clock.Now;
        DateTimeOffset? last = lastSecond is int s ? start.AddSeconds(s) : null;
        Assert.Equal(expected, GitHubRefresh.ShouldRun(last, start.AddSeconds(nowSecond), watched));
    }

    private GitHubApi New(FakeHttp http) => new(secrets, http, () => clock.Now);

    private static FakeHttp Serve(string graphql) =>
        new(s => s.Uri.AbsolutePath switch
        {
            "/graphql" => FakeHttp.Json(graphql),
            _ => throw new InvalidOperationException($"Asked {s.Uri}"),
        });

    /// <summary>The refresh answers with <paramref name="renewal"/>, the query with the normal reply.</summary>
    private static FakeHttp Routes(string renewal) =>
        new(s => s.Uri.AbsolutePath switch
        {
            "/graphql" => FakeHttp.Json(GitHubParseTests.Normal),
            "/login/oauth/access_token" => FakeHttp.Json(renewal),
            _ => throw new InvalidOperationException($"Asked {s.Uri}"),
        });

    private void Expiring(DateTimeOffset at)
    {
        secrets.Set(GitHubApp.RefreshName, Refresh);
        secrets.Set(GitHubApp.ExpiresName, at.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
    }

    private sealed class Secrets : ISecretStore
    {
        private readonly Dictionary<string, string> values = [];

        public string? Get(string name) => values.GetValueOrDefault(name);

        public bool Set(string name, string value)
        {
            values[name] = value;
            return true;
        }

        public void Delete(string name) => values.Remove(name);
    }

    /// <summary>Holds every request until <see cref="Open"/> is set, so a second refresh can try to start meanwhile.</summary>
    private sealed class Gate : HttpMessageHandler
    {
        private int calls;

        public TaskCompletionSource Open { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls => Volatile.Read(ref calls);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            await Open.Task.WaitAsync(cancellationToken);
            return FakeHttp.Json(GitHubParseTests.Normal);
        }
    }
}
