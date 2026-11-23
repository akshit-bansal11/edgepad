using System.Globalization;
using System.Net;
using Edgepad.Dashboard;
using Xunit;

namespace Edgepad.Tests;

public sealed class GitLabTests : IDisposable
{
    private const string Client = "90603e45237215e265b1cc5a873ec796353c5e3185c14be0341a371a0dc5d000";
    private const string Token = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string Refresh = "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";
    private const string NewToken = "aaaaaaaaaaaaaaaabbbbbbbbbbbbbbbbccccccccccccccccdddddddddddddddd";
    private const string NewRefresh = "eeeeeeeeeeeeeeeeffffffffffffffff0000000000000000ffffffffffffffff";

    private const string Code = """{"device_code":"1234567890abcdef","user_code":"0A44L90H","verification_uri":"https://gitlab.com/oauth/device","verification_uri_complete":"https://gitlab.com/oauth/device?user_code=0A44L90H","expires_in":300,"interval":5}""";
    private const string Pending = """{"error":"authorization_pending","error_description":"..."}""";
    private const string SlowDown = """{"error":"slow_down"}""";
    private const string Granted = """{"access_token":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef","token_type":"Bearer","expires_in":7200,"refresh_token":"fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210","scope":"read_api read_user","created_at":1763553600}""";
    private const string Renewed = """{"access_token":"aaaaaaaaaaaaaaaabbbbbbbbbbbbbbbbccccccccccccccccdddddddddddddddd","token_type":"Bearer","expires_in":7200,"refresh_token":"eeeeeeeeeeeeeeeeffffffffffffffff0000000000000000ffffffffffffffff","created_at":1763553600}""";

    private const string User = """{"id":1,"username":"ana","name":"Ana B","avatar_url":"https://gitlab.com/uploads/a.png","web_url":"https://gitlab.com/ana","followers":3,"following":0}""";

    private const string Projects = """
        [{"id":1,"path_with_namespace":"grp/new","visibility":"private","open_issues_count":2,"star_count":5,"web_url":"https://gitlab.com/grp/new","last_activity_at":"2026-11-01T00:00:00Z","default_branch":"main"},
         {"id":2,"path_with_namespace":"grp/old","visibility":"public","open_issues_count":0,"star_count":1,"web_url":"https://gitlab.com/grp/old","last_activity_at":"2026-01-01T00:00:00Z","default_branch":"main"}]
        """;

    private const string MergesMine = """
        [{"iid":12,"project_id":1,"title":"Fix","state":"opened","draft":true,"web_url":"https://gitlab.com/grp/new/-/merge_requests/12","updated_at":"2026-11-02T00:00:00Z","references":{"full":"grp/new!12"},"head_pipeline":{"status":"failed"}}]
        """;

    private const string IssuesMine = """
        [{"iid":6,"project_id":1,"title":"Bug","state":"opened","web_url":"https://gitlab.com/grp/new/-/issues/6","updated_at":"2026-11-03T00:00:00Z","references":{"full":"grp/new#6"}}]
        """;

    private const string MergesReview = """
        [{"iid":3,"project_id":9,"title":"Review me","state":"opened","draft":false,"web_url":"https://gitlab.com/other/lib/-/merge_requests/3","updated_at":"2026-11-04T00:00:00Z","references":{"full":"other/lib!3"},"head_pipeline":{"status":"running"}}]
        """;

    private readonly DirectoryInfo dir = Directory.CreateTempSubdirectory("edgepad-gitlab");
    private readonly FakeClock clock = new();
    private readonly MemorySecrets secrets = new();
    private readonly List<TimeSpan> waits = [];
    private readonly IntegrationSettings settings;

    public GitLabTests() => settings = new IntegrationSettings(Path.Combine(dir.FullName, IntegrationSettings.FileName));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => dir.Delete(recursive: true);

    // ---- device sign-in

    [Fact]
    public async Task TheDeviceRequestUsesThisClientIdAndTheTwoReadScopesAndNoSecret()
    {
        var http = new FakeHttp(_ => FakeHttp.Json(Code));
        var start = await SignIn(http).StartAsync(Ct);

        Assert.Equal(new DeviceCode(SignInState.Started, "0A44L90H", new Uri("https://gitlab.com/oauth/device"), TimeSpan.FromMinutes(5)), start);
        var request = Assert.Single(http.Seen);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://gitlab.com/oauth/authorize_device", request.Uri.AbsoluteUri);
        Assert.Equal($"client_id={Client}&scope=read_api+read_user", request.Body);
        Assert.Equal(Client, GitLabApp.ClientId);
        Assert.DoesNotContain("secret", request.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PendingThenSlowDownThenGrantedSavesTheTokensInOrderAndPollsGitLabOnly()
    {
        var http = Script(Code, Pending, SlowDown, Granted);
        var signIn = SignIn(http);

        await signIn.StartAsync(Ct);
        Assert.Equal(SignInState.SignedIn, await signIn.WaitAsync(Ct));

        Assert.Equal("5,5,10", string.Join(",", waits.Select(w => (int)w.TotalSeconds)));
        Assert.All(http.Seen.Skip(1), s =>
        {
            Assert.Equal("https://gitlab.com/oauth/token", s.Uri.AbsoluteUri);
            Assert.Equal($"client_id={Client}&device_code=1234567890abcdef&grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Adevice_code", s.Body);
        });
        Assert.Equal(Token, secrets.Get(GitLabApp.TokenName));
        Assert.Equal(Refresh, secrets.Get(GitLabApp.RefreshName));
        Assert.Equal(clock.Now.AddSeconds(7200).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), secrets.Get(GitLabApp.ExpiresName));
        Assert.Equal(Client, secrets.Get(GitLabApp.ClientName));
        Assert.True(signIn.IsSignedIn);
    }

    [Fact]
    public void TheSaveOrderIsRefreshAccessExpiryThenClient()
    {
        var saved = new List<string>();
        var store = new MemorySecrets { OnSet = saved.Add };

        Assert.True(GitLabApp.Save(store, Client, Token, Refresh, clock.Now.AddHours(2)));

        Assert.Equal([GitLabApp.RefreshName, GitLabApp.TokenName, GitLabApp.ExpiresName, GitLabApp.ClientName], saved);
    }

    [Fact]
    public async Task ASignInAfterASelfHostedOneMovesTheAddressBackToGitLabCom()
    {
        settings.Set(GitTokens.UrlKey("gitlab"), "https://git.example.org");
        var signIn = SignIn(Script(Code, Granted));

        await signIn.StartAsync(Ct);
        await signIn.WaitAsync(Ct);

        Assert.Equal(string.Empty, settings.Text(GitTokens.UrlKey("gitlab")));
    }

    [Theory]
    [InlineData("""{"error":"access_denied"}""", SignInState.Denied)]
    [InlineData("""{"error":"expired_token"}""", SignInState.Expired)]
    [InlineData("""{"error":"invalid_client"}""", SignInState.Failed)]
    public async Task ADenialAnExpiredCodeAndAnUnknownErrorEndTheFlowWithNothingSaved(string answer, SignInState expected)
    {
        var signIn = SignIn(Script(Code, answer));

        await signIn.StartAsync(Ct);

        Assert.Equal(expected, await signIn.WaitAsync(Ct));
        Assert.Null(secrets.Get(GitLabApp.TokenName));
    }

    [Fact]
    public async Task AnAnswerWithoutARefreshTokenIsKeptWithoutAnExpiry()
    {
        var signIn = SignIn(Script(Code, """{"access_token":"0123456789abcdef0123456789abcdef","token_type":"Bearer","expires_in":7200}"""));

        await signIn.StartAsync(Ct);
        Assert.Equal(SignInState.SignedIn, await signIn.WaitAsync(Ct));

        Assert.Null(secrets.Get(GitLabApp.RefreshName));
        Assert.Null(secrets.Get(GitLabApp.ExpiresName));
    }

    // ---- renewal, never with a secret

    [Fact]
    public async Task AnExpiringTokenIsRenewedBeforeItRunsOutWithNoSecretAndTheNewOneIsUsed()
    {
        Expiring(clock.Now.AddMinutes(2));
        var http = new FakeHttp(s => s.Uri.AbsolutePath == "/oauth/token" ? FakeHttp.Json(Renewed) : Serve(s));

        var result = await Host(http).RefreshAsync(Ct);

        Assert.Equal(GitHubResult.Ok, result.Result);
        var renew = http.Seen[0];
        Assert.Equal(HttpMethod.Post, renew.Method);
        Assert.Equal("https://gitlab.com/oauth/token", renew.Uri.AbsoluteUri);
        Assert.Equal($"client_id={Client}&grant_type=refresh_token&refresh_token={Refresh}", renew.Body);
        Assert.DoesNotContain("secret", renew.Body, StringComparison.OrdinalIgnoreCase);
        Assert.All(http.Seen.Skip(1), s => Assert.Equal("Bearer " + NewToken, s.Header("Authorization")));
        Assert.Equal(NewToken, secrets.Get(GitLabApp.TokenName));
        Assert.Equal(NewRefresh, secrets.Get(GitLabApp.RefreshName));
    }

    [Fact]
    public async Task ATokenWithTimeLeftIsNotRenewed()
    {
        Expiring(clock.Now.AddHours(1));
        var http = new FakeHttp(Serve);

        await Host(http).RefreshAsync(Ct);

        Assert.DoesNotContain(http.Seen, s => s.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task OnlyInvalidGrantSignsOut()
    {
        Expiring(clock.Now.AddMinutes(1));
        var http = new FakeHttp(s => s.Uri.AbsolutePath == "/oauth/token" ? FakeHttp.Json("""{"error":"invalid_grant"}""", HttpStatusCode.BadRequest) : Serve(s));

        var result = await Host(http).RefreshAsync(Ct);

        Assert.Equal(GitHubResult.NoPermission, result.Result);
        Assert.Null(secrets.Get(GitLabApp.TokenName));
        Assert.Null(secrets.Get(GitLabApp.RefreshName));
        Assert.Null(secrets.Get(GitLabApp.ClientName));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, """{"error":"server_error"}""")]
    [InlineData(HttpStatusCode.Unauthorized, """{"error":"invalid_client"}""")]
    [InlineData(HttpStatusCode.BadGateway, "<html>bad gateway</html>")]
    public async Task AnyOtherRenewalFailureKeepsEveryToken(HttpStatusCode status, string body)
    {
        Expiring(clock.Now.AddMinutes(1));
        var http = new FakeHttp(s => s.Uri.AbsolutePath == "/oauth/token" ? FakeHttp.Json(body, status) : Serve(s));

        await Host(http).RefreshAsync(Ct);

        Assert.Equal(Token, secrets.Get(GitLabApp.TokenName));
        Assert.Equal(Refresh, secrets.Get(GitLabApp.RefreshName));
    }

    [Fact]
    public async Task ARefusalIsAnsweredByOneRenewalAndOneRetry()
    {
        Expiring(clock.Now.AddHours(1));
        var http = new FakeHttp(s => s.Uri.AbsolutePath == "/oauth/token"
            ? FakeHttp.Json(Renewed)
            : s.Header("Authorization") == "Bearer " + Token ? FakeHttp.Status(HttpStatusCode.Unauthorized) : Serve(s));

        var result = await Host(http).RefreshAsync(Ct);

        Assert.Equal(GitHubResult.Ok, result.Result);
        Assert.Single(http.Seen, s => s.Method == HttpMethod.Post);
        Assert.Equal(NewToken, secrets.Get(GitLabApp.TokenName));
    }

    [Fact]
    public async Task TwoHostsOnOneStoreRenewOnlyOnce()
    {
        Expiring(clock.Now.AddMinutes(1));
        var http = new FakeHttp(s => s.Uri.AbsolutePath == "/oauth/token" ? FakeHttp.Json(Renewed) : Serve(s));

        await Task.WhenAll(Host(http).RefreshAsync(Ct), Host(http).RefreshAsync(Ct));

        Assert.Single(http.Seen, s => s.Uri.AbsolutePath == "/oauth/token");
    }

    // ---- JSON to GitSnapshot

    [Fact]
    public async Task TheDocumentedShapesMapToTheSnapshotAndEveryRequestIsAGet()
    {
        secrets.Set(GitLabApp.TokenName, Token);
        var http = new FakeHttp(Serve);
        var host = Host(http);

        var result = await host.RefreshAsync(Ct);

        Assert.Equal(new GitHubRefreshResult(GitHubResult.Ok, true), result);
        Assert.Equal("gitlab.com", host.Domain);
        Assert.Equal(
            string.Join(
                '\n',
                "H\tgitlab\tana\tAna B\t2\t3\t1",
                "P\tgrp/new\tp\t\t5\t2\t0",
                "P\tgrp/old\t\t\t1\t0\t0",
                $"M\tgrp/new\t6\tBug\ti\tnone\topen\t{Min("2026-11-03T00:00:00Z")}",
                $"M\tgrp/new\t12\tFix\td\tfail\topen\t{Min("2026-11-02T00:00:00Z")}",
                $"R\tother/lib\t3\tReview me\t\tpending\topen\t{Min("2026-11-04T00:00:00Z")}"),
            host.Page!.Body);
        Assert.Equal(
            new List<string>
            {
                "https://gitlab.com/grp/new",
                "https://gitlab.com/grp/old",
                "https://gitlab.com/grp/new/-/issues/6",
                "https://gitlab.com/grp/new/-/merge_requests/12",
                "https://gitlab.com/other/lib/-/merge_requests/3",
            },
            host.Page.Urls);
        Assert.All(http.Seen, s =>
        {
            Assert.Equal(HttpMethod.Get, s.Method);
            Assert.Equal("https", s.Uri.Scheme);
            Assert.Equal("gitlab.com", s.Uri.Host);
            Assert.StartsWith("/api/v4/", s.Uri.AbsolutePath, StringComparison.Ordinal);
            Assert.Equal("Bearer " + Token, s.Header("Authorization"));
            Assert.DoesNotContain(Token, s.Uri.AbsoluteUri, StringComparison.Ordinal);
        });
        Assert.Contains(http.Seen, s => s.Uri.AbsolutePath == "/api/v4/merge_requests" && s.Uri.Query.Contains("scope=reviews_for_me", StringComparison.Ordinal) && s.Uri.Query.Contains("state=opened", StringComparison.Ordinal));
        Assert.Contains(http.Seen, s => s.Uri.AbsolutePath == "/api/v4/projects" && s.Uri.Query.Contains("membership=true", StringComparison.Ordinal) && s.Uri.Query.Contains("order_by=last_activity_at", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("success", "ok")]
    [InlineData("failed", "fail")]
    [InlineData("running", "pending")]
    [InlineData("pending", "pending")]
    [InlineData("created", "pending")]
    [InlineData("canceled", "none")]
    [InlineData("skipped", "none")]
    [InlineData(null, "none")]
    public void APipelineStatusMapsToAChecksToken(string? status, string expected) => Assert.Equal(expected, GitLabHost.Ci(status));

    [Fact]
    public async Task ReviewStaysEmptyWhenTheServerDoesNotKnowReviewsForMe()
    {
        secrets.Set(GitLabApp.TokenName, Token);
        var http = new FakeHttp(s => s.Uri.Query.Contains("reviews_for_me", StringComparison.Ordinal) ? FakeHttp.Status(HttpStatusCode.BadRequest) : Serve(s));
        var host = Host(http);

        Assert.Equal(GitHubResult.Ok, (await host.RefreshAsync(Ct)).Result);
        Assert.DoesNotContain("\nR\t", "\n" + host.Page!.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARevokedTokenSignsOutLikeGitHub()
    {
        secrets.Set(GitLabApp.TokenName, Token);
        var host = Host(new FakeHttp(_ => FakeHttp.Status(HttpStatusCode.Unauthorized)));

        var result = await host.RefreshAsync(Ct);

        Assert.Equal(GitHubResult.NoPermission, result.Result);
        Assert.Null(secrets.Get(GitLabApp.TokenName));
        Assert.Null(host.Page);
    }

    // ---- self-hosted

    [Theory]
    [InlineData("")]
    [InlineData("http://git.example.org")]
    [InlineData("https://user:pw@git.example.org")]
    [InlineData("https://git.example.org/?a=1")]
    [InlineData("https://git.example.org/#x")]
    [InlineData("https://evil.example%23.gitlab.com")]
    [InlineData("ftp://git.example.org")]
    [InlineData("javascript:alert(1)")]
    public async Task ABadServerAddressIsRejectedAndNothingIsAskedOrSaved(string url)
    {
        Assert.Null(GitTokens.GitLabApi(url));
        var http = new FakeHttp(Serve);
        var host = Host(http);

        var saved = await host.SaveServerAsync(Token, url, Ct);

        Assert.Equal(GitSaveStatus.BadUrl, saved.Status);
        Assert.Empty(http.Seen);
        Assert.Null(secrets.Get(GitLabApp.TokenName));
    }

    [Fact]
    public void AGoodServerAddressBecomesTheApiRoot()
    {
        Assert.Equal("https://git.example.org/api/v4/", GitTokens.GitLabApi(" https://git.example.org/ ")!.AbsoluteUri);
        Assert.Equal("https://example.org/gitlab/api/v4/", GitTokens.GitLabApi("https://example.org/gitlab/")!.AbsoluteUri);
        Assert.Equal("https://git.example.org:8443/api/v4/", GitTokens.GitLabApi("https://git.example.org:8443")!.AbsoluteUri);
    }

    [Fact]
    public async Task ASelfHostedServerIsReadWithAPastedTokenAndNeverSeesAPost()
    {
        Expiring(clock.Now.AddMinutes(1));
        var http = new FakeHttp(Serve);
        var host = Host(http);

        var saved = await host.SaveServerAsync(" " + Token + " ", "https://git.example.org", Ct);

        Assert.Equal(new GitSaveResult(GitSaveStatus.Saved, "ana"), saved);
        Assert.Equal("https://git.example.org", settings.Text(GitTokens.UrlKey("gitlab")));
        Assert.Null(secrets.Get(GitLabApp.RefreshName));
        Assert.Null(secrets.Get(GitLabApp.ExpiresName));
        Assert.Equal(Token, secrets.Get(GitLabApp.TokenName));

        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(GitHubResult.Ok, (await host.RefreshAsync(Ct)).Result);
        Assert.Equal("git.example.org", host.Domain);
        Assert.All(http.Seen, s =>
        {
            Assert.Equal(HttpMethod.Get, s.Method);
            Assert.Equal("git.example.org", s.Uri.Host);
            Assert.StartsWith("/api/v4/", s.Uri.AbsolutePath, StringComparison.Ordinal);
            Assert.Equal("Bearer " + Token, s.Header("Authorization"));
        });
    }

    [Fact]
    public async Task ASelfHostedRefusalIsReportedAndNothingIsSaved()
    {
        var host = Host(new FakeHttp(_ => FakeHttp.Status(HttpStatusCode.Unauthorized)));

        var saved = await host.SaveServerAsync(Token, "https://git.example.org", Ct);

        Assert.Equal(GitSaveStatus.Unauthorized, saved.Status);
        Assert.Null(secrets.Get(GitLabApp.TokenName));
        Assert.Equal(string.Empty, settings.Text(GitTokens.UrlKey("gitlab")));
    }

    [Fact]
    public void SignOutForgetsEveryTokenAndKeepsTheServerAddress()
    {
        Expiring(clock.Now.AddHours(1));
        settings.Set(GitTokens.UrlKey("gitlab"), "https://git.example.org");

        Host(new FakeHttp(Serve)).SignOut();

        Assert.Null(secrets.Get(GitLabApp.TokenName));
        Assert.Null(secrets.Get(GitLabApp.RefreshName));
        Assert.Null(secrets.Get(GitLabApp.ExpiresName));
        Assert.Null(secrets.Get(GitLabApp.ClientName));
        Assert.Equal("https://git.example.org", settings.Text(GitTokens.UrlKey("gitlab")));
    }

    // ---- the read-only gate

    [Theory]
    [InlineData("POST", "https://gitlab.com/oauth/authorize_device", true)]
    [InlineData("POST", "https://gitlab.com/oauth/token", true)]
    [InlineData("GET", "https://gitlab.com/api/v4/user", true)]
    [InlineData("GET", "https://git.example.org/api/v4/user", true)]
    [InlineData("POST", "https://git.example.org/oauth/token", false)]
    [InlineData("POST", "https://git.example.org/oauth/authorize_device", false)]
    [InlineData("POST", "https://gitlab.com/api/v4/projects", false)]
    [InlineData("POST", "https://gitlab.com/oauth/token/revoke", false)]
    [InlineData("PUT", "https://gitlab.com/api/v4/projects/1", false)]
    [InlineData("PATCH", "https://gitlab.com/api/v4/merge_requests/1", false)]
    [InlineData("DELETE", "https://gitlab.com/api/v4/projects/1", false)]
    [InlineData("GET", "http://gitlab.com/api/v4/user", false)]
    public async Task TheGateAllowsOnlyGitLabsDeviceCallsAndReads(string method, string url, bool allowed)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), url);

        Assert.Equal(allowed, await ReadOnlyGate.AllowsAsync(request, Ct));
    }

    [Fact]
    public async Task ARefusedWriteNamesNoTokenAndTheLogNeverHoldsOne()
    {
        secrets.Set(GitLabApp.TokenName, Token);
        using var request = new HttpRequestMessage(HttpMethod.Delete, "https://gitlab.com/api/v4/projects/1?private_token=" + Token);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token);
        var http = new FakeHttp(Serve);

        var error = await Assert.ThrowsAsync<ReadOnlyViolation>(() => CappedHttp.SendAsync(request, 1024, TimeSpan.FromSeconds(5), http, "test", Ct));
        Assert.DoesNotContain(Token, error.Message, StringComparison.Ordinal);
        Assert.Empty(http.Seen);

        await Host(new FakeHttp(Serve)).RefreshAsync(Ct);
        Assert.DoesNotContain(Token, LogText.Read(), StringComparison.Ordinal);
    }

    // ---- helpers

    private static long Min(string iso) => DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture).ToUnixTimeSeconds() / 60;

    private GitLabHost Host(FakeHttp http) => new(secrets, settings, http, () => clock.Now);

    private GitLabSignIn SignIn(FakeHttp http) => new(secrets, settings, http, () => clock.Now, Wait);

    private void Expiring(DateTimeOffset at)
    {
        secrets.Set(GitLabApp.TokenName, Token);
        secrets.Set(GitLabApp.RefreshName, Refresh);
        secrets.Set(GitLabApp.ExpiresName, at.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        secrets.Set(GitLabApp.ClientName, Client);
    }

    /// <summary>A wait that takes no time: it moves the clock and records how long it was asked to be.</summary>
    private Task Wait(TimeSpan span, CancellationToken ct)
    {
        waits.Add(span);
        clock.Advance(span);
        return Task.CompletedTask;
    }

    /// <summary>Answers in order.</summary>
    private static FakeHttp Script(params string[] replies)
    {
        var queue = new Queue<string>(replies);
        return new FakeHttp(_ => FakeHttp.Json(queue.Dequeue()));
    }

    /// <summary>A GitLab that answers the documented list shapes; the path and the <c>scope</c> pick the reply.</summary>
    private static HttpResponseMessage Serve(SeenRequest s)
    {
        var query = s.Uri.Query;
        return s.Uri.AbsolutePath switch
        {
            "/api/v4/user" => FakeHttp.Json(User),
            "/api/v4/projects" => WithTotal(FakeHttp.Json(Projects), 2),
            "/api/v4/merge_requests" when query.Contains("created_by_me", StringComparison.Ordinal) => FakeHttp.Json(MergesMine),
            "/api/v4/merge_requests" when query.Contains("reviews_for_me", StringComparison.Ordinal) => FakeHttp.Json(MergesReview),
            "/api/v4/merge_requests" => FakeHttp.Json("[]"),
            "/api/v4/issues" when query.Contains("created_by_me", StringComparison.Ordinal) => FakeHttp.Json(IssuesMine),
            "/api/v4/issues" => FakeHttp.Json("[]"),
            _ => throw new InvalidOperationException($"Asked {s.Uri}"),
        };
    }

    private static HttpResponseMessage WithTotal(HttpResponseMessage reply, int total)
    {
        reply.Headers.TryAddWithoutValidation("x-total", total.ToString(CultureInfo.InvariantCulture));
        return reply;
    }

    private sealed class MemorySecrets : ISecretStore
    {
        private readonly Dictionary<string, string> values = [];

        public Action<string>? OnSet { get; init; }

        public string? Get(string name) => values.GetValueOrDefault(name);

        public bool Set(string name, string value)
        {
            OnSet?.Invoke(name);
            values[name] = value;
            return true;
        }

        public void Delete(string name) => values.Remove(name);
    }
}
