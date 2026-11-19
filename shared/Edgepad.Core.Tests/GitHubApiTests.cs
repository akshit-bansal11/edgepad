using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Edgepad.Dashboard;
using Edgepad.Protocol;
using Xunit;

namespace Edgepad.Tests;

public sealed class GitHubApiTests
{
    private const string Token = "gho_ApiTokenNeverLogged0987654321";
    private const string TwoUnread = """[{"id":"1"},{"id":"2"}]""";

    private readonly FakeClock clock = new();
    private readonly Secrets secrets = new();

    public GitHubApiTests() => secrets.Set(GitHubApp.TokenName, Token);

    [Fact]
    public async Task ARefreshSendsTheConstantQueryThenAsksForNotifications()
    {
        var http = Serve(GitHubParseTests.Normal, TwoUnread);
        var api = New(http);

        var result = await api.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new GitHubRefreshResult(GitHubResult.Ok, true), result);
        Assert.Equal(2, http.Seen.Count);

        var graphql = http.Seen[0];
        Assert.Equal(HttpMethod.Post, graphql.Method);
        Assert.Equal("https://api.github.com/graphql", graphql.Uri.AbsoluteUri);
        using (var body = JsonDocument.Parse(graphql.Body))
        {
            Assert.Equal(GitHubApi.Query, body.RootElement.GetProperty("query").GetString());
            Assert.Single(body.RootElement.EnumerateObject());
        }

        Assert.Equal("application/json", graphql.ContentType);

        var notifications = http.Seen[1];
        Assert.Equal(HttpMethod.Get, notifications.Method);
        Assert.Equal("https://api.github.com/notifications?per_page=50", notifications.Uri.AbsoluteUri);

        Assert.All(http.Seen, s =>
        {
            Assert.Equal("api.github.com", s.Uri.Host);
            Assert.Equal("Bearer " + Token, s.Header("Authorization"));
            Assert.Equal("application/vnd.github+json", s.Header("Accept"));
            Assert.Equal("2022-11-28", s.Header("X-GitHub-Api-Version"));
            Assert.Equal("Edgepad", s.Header("User-Agent"));
        });

        Assert.Equal(
            new List<GitHubLine>
            {
                new("N", string.Empty, 2, string.Empty, false, CiToken.None),
                new("R", "octo/edgepad", 12, "Fix the pairing timeout", false, CiToken.Ok),
                new("R", "octo/other.repo", 7, "Draft idea", true, CiToken.None),
                new("M", "me/my_repo-2", 3, "Mine", false, CiToken.Fail),
            },
            api.Lines);
    }

    [Fact]
    public async Task TheQueryNeverChanges()
    {
        Assert.Equal(GitHubApi.QueryBody(), GitHubApi.QueryBody());
        Assert.DoesNotContain("{{", GitHubApi.Query, StringComparison.Ordinal);
        var http = Serve(GitHubParseTests.Normal, TwoUnread);
        var api = New(http);
        await api.RefreshAsync(TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromMinutes(2));
        await api.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(http.Seen[0].Body, http.Seen[2].Body);
    }

    [Fact]
    public async Task WithoutATokenNobodyIsAsked()
    {
        secrets.Delete(GitHubApp.TokenName);
        var http = Serve(GitHubParseTests.Normal, TwoUnread);
        Assert.Equal(GitHubResult.NotSignedIn, (await New(http).RefreshAsync(TestContext.Current.CancellationToken)).Result);
        Assert.Empty(http.Seen);
    }

    [Fact]
    public async Task ATokenThatCouldInjectAHeaderIsNeverSent()
    {
        secrets.Set(GitHubApp.TokenName, "gho_x\r\nX-Evil: 1");
        var http = Serve(GitHubParseTests.Normal, TwoUnread);
        Assert.Equal(GitHubResult.NotSignedIn, (await New(http).RefreshAsync(TestContext.Current.CancellationToken)).Result);
        Assert.Empty(http.Seen);
    }

    [Fact]
    public async Task TheETagIsSentBackAndANotModifiedKeepsTheCount()
    {
        var first = true;
        var http = new FakeHttp(s =>
        {
            if (s.Uri.AbsolutePath == "/graphql")
            {
                return FakeHttp.Json(GitHubParseTests.Normal);
            }

            if (first)
            {
                first = false;
                var reply = FakeHttp.Json(TwoUnread);
                reply.Headers.ETag = new EntityTagHeaderValue("\"abc123\"");
                return reply;
            }

            return FakeHttp.Status(HttpStatusCode.NotModified);
        });
        var api = New(http);
        var ct = TestContext.Current.CancellationToken;

        await api.RefreshAsync(ct);
        var again = await api.RefreshAsync(ct);

        Assert.Equal(new GitHubRefreshResult(GitHubResult.Ok, false), again);
        Assert.Null(http.Seen[1].Header("If-None-Match"));
        Assert.Equal("\"abc123\"", http.Seen[3].Header("If-None-Match"));
        Assert.Equal(2, api.Lines[0].Number);
    }

    [Fact]
    public async Task ARefusedTokenIsDeletedAndTheScreenEmptied()
    {
        var refuse = false;
        var http = new FakeHttp(s => refuse
            ? FakeHttp.Status(HttpStatusCode.Unauthorized)
            : FakeHttp.Json(s.Uri.AbsolutePath == "/graphql" ? GitHubParseTests.Normal : TwoUnread));
        var api = New(http);
        var ct = TestContext.Current.CancellationToken;

        await api.RefreshAsync(ct);
        refuse = true;
        var result = await api.RefreshAsync(ct);

        Assert.Equal(new GitHubRefreshResult(GitHubResult.NoPermission, true), result);
        Assert.Null(secrets.Get(GitHubApp.TokenName));
        Assert.Empty(api.Lines);
        Assert.Equal(3, http.Seen.Count);
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

            return FakeHttp.Json(s.Uri.AbsolutePath == "/graphql" ? GitHubParseTests.Normal : TwoUnread);
        });
        var api = New(http);
        var ct = TestContext.Current.CancellationToken;

        await api.RefreshAsync(ct);
        var kept = api.Lines;
        limited = true;
        Assert.Equal(GitHubResult.RateLimited, (await api.RefreshAsync(ct)).Result);
        Assert.Equal(kept, api.Lines);
        Assert.Equal(3, http.Seen.Count);

        clock.Advance(TimeSpan.FromMinutes(9));
        Assert.Equal(GitHubResult.RateLimited, (await api.RefreshAsync(ct)).Result);
        Assert.Equal(3, http.Seen.Count);

        limited = false;
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(GitHubResult.Ok, (await api.RefreshAsync(ct)).Result);
        Assert.Equal(5, http.Seen.Count);
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
        Assert.Equal(4, http.Seen.Count);
        Assert.NotNull(secrets.Get(GitHubApp.TokenName));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task AnyOtherErrorKeepsTheLastGoodResult(HttpStatusCode status)
    {
        var fail = false;
        var http = new FakeHttp(s => fail
            ? (status == HttpStatusCode.Found ? FakeHttp.Redirect("https://evil.example/graphql") : FakeHttp.Status(status))
            : FakeHttp.Json(s.Uri.AbsolutePath == "/graphql" ? GitHubParseTests.Normal : TwoUnread));
        var api = New(http);
        var ct = TestContext.Current.CancellationToken;

        await api.RefreshAsync(ct);
        var kept = api.Lines;
        fail = true;
        Assert.Equal(new GitHubRefreshResult(GitHubResult.Failed, false), await api.RefreshAsync(ct));
        Assert.Equal(kept, api.Lines);
        Assert.All(http.Seen, s => Assert.Equal("api.github.com", s.Uri.Host));
    }

    [Fact]
    public async Task AReplyOverOneMibIsRefused()
    {
        var huge = """{"data":{"mine":{"nodes":[]}},"pad":"PAD"}""".Replace("PAD", new string('x', 2 * 1024 * 1024), StringComparison.Ordinal);
        var http = new FakeHttp(s => FakeHttp.Json(s.Uri.AbsolutePath == "/graphql" ? huge : TwoUnread));
        var api = New(http);

        Assert.Equal(GitHubResult.Failed, (await api.RefreshAsync(TestContext.Current.CancellationToken)).Result);
        Assert.Equal(CiToken.None, Assert.Single(api.Lines).Ci);
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
        Assert.Equal(2, gate.Calls);
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

    private static FakeHttp Serve(string graphql, string notifications) =>
        new(s => s.Uri.AbsolutePath switch
        {
            "/graphql" => FakeHttp.Json(graphql),
            "/notifications" => FakeHttp.Json(notifications),
            _ => throw new InvalidOperationException($"Asked {s.Uri}"),
        });

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
            return FakeHttp.Json(request.RequestUri!.AbsolutePath == "/graphql" ? GitHubParseTests.Normal : TwoUnread);
        }
    }
}
