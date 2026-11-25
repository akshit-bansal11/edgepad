using System.Globalization;
using System.Net;
using Edgepad.Dashboard;
using Xunit;

namespace Edgepad.Tests;

public sealed class GitHubSignInTests
{
    private const string Client = "Ov23liTestClient0001";
    private const string Token = "ghu_SecretTokenValue1234567890abcdef";
    private const string Code = """{"device_code":"3584d83530557fdd1f46af8289938c8ef79f9dc5","user_code":"WDJB-MJHT","verification_uri":"https://github.com/login/device","expires_in":900,"interval":5}""";
    private const string Pending = """{"error":"authorization_pending"}""";
    private const string SlowDown = """{"error":"slow_down","interval":10}""";
    private const string Granted = """{"access_token":"ghu_SecretTokenValue1234567890abcdef","token_type":"bearer","scope":""}""";

    private readonly FakeClock clock = new();
    private readonly MemorySecrets secrets = new();
    private readonly List<TimeSpan> waits = [];

    [Fact]
    public async Task AnEmptyClientIdReportsNotConfiguredWithoutARequest()
    {
        var http = new FakeHttp(_ => FakeHttp.Json(Code));
        var signIn = new GitHubSignIn(secrets, "", http, () => clock.Now, Wait);

        var start = await signIn.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SignInState.NotConfigured, start.State);
        Assert.Empty(http.Seen);
        Assert.False(GitHubApp.IsConfigured(""));
        Assert.True(GitHubApp.IsConfigured(GitHubApp.ClientId));
    }

    [Fact]
    public async Task StartAsksGitHubForACodeAtTheFixedAddress()
    {
        var http = new FakeHttp(_ => FakeHttp.Json(Code));
        var start = await New(http).StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new DeviceCode(SignInState.Started, "WDJB-MJHT", new Uri("https://github.com/login/device"), TimeSpan.FromMinutes(15)), start);
        var request = Assert.Single(http.Seen);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://github.com/login/device/code", request.Uri.AbsoluteUri);
        Assert.Equal("client_id=Ov23liTestClient0001", request.Body);
        Assert.DoesNotContain("scope", request.Body, StringComparison.Ordinal);
        Assert.Equal("application/json", request.Header("Accept"));
        Assert.DoesNotContain("secret", request.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheAppIsTheReadOnlyGitHubApp()
    {
        Assert.Equal("Iv23lie6SY6AFlHwufmK", GitHubApp.ClientId);
        Assert.Equal("https://github.com/apps/edgepad-dashboard/installations/new", GitHubApp.InstallUri.AbsoluteUri);
    }

    [Fact]
    public async Task AnExpiringTokenIsSavedWithItsRefreshTokenExpiryAndClientId()
    {
        const string expiring = """{"access_token":"ghu_SecretTokenValue1234567890abcdef","expires_in":28800,"refresh_token":"ghr_RefreshValue1234567890abcdef","refresh_token_expires_in":15897600,"scope":"","token_type":"bearer"}""";
        var signIn = New(Script(Code, expiring));
        var ct = TestContext.Current.CancellationToken;

        await signIn.StartAsync(ct);
        Assert.Equal(SignInState.SignedIn, await signIn.WaitAsync(ct));

        Assert.Equal("ghu_SecretTokenValue1234567890abcdef", secrets.Get(GitHubApp.TokenName));
        Assert.Equal("ghr_RefreshValue1234567890abcdef", secrets.Get(GitHubApp.RefreshName));
        Assert.Equal(clock.Now.AddSeconds(28800).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), secrets.Get(GitHubApp.ExpiresName));
        Assert.Equal(Client, secrets.Get(GitHubApp.ClientName));
    }

    [Theory]
    [InlineData("""{"access_token":"ghu_SecretTokenValue1234567890abcdef","expires_in":28800}""")]
    [InlineData("""{"access_token":"ghu_SecretTokenValue1234567890abcdef","refresh_token":"ghr_Value123"}""")]
    [InlineData("""{"access_token":"ghu_SecretTokenValue1234567890abcdef","refresh_token":"ghr_bad value","expires_in":28800}""")]
    [InlineData("""{"access_token":"ghu_SecretTokenValue1234567890abcdef","refresh_token":"ghr_Value123","expires_in":0}""")]
    public async Task HalfOfAnExpiringPairOrAnOutOfRangeLifeIsAFailure(string answer)
    {
        var signIn = New(Script(Code, answer));
        var ct = TestContext.Current.CancellationToken;

        await signIn.StartAsync(ct);
        Assert.Equal(SignInState.Failed, await signIn.WaitAsync(ct));
        Assert.Null(secrets.Get(GitHubApp.TokenName));
    }

    [Fact]
    public async Task ATokenFromTheOldOAuthAppIsDeletedOnTheFirstLookAndTheNoteStaysUntilANewSignIn()
    {
        secrets.Set(GitHubApp.TokenName, "gho_OldOAuthAppToken1234567890");
        var signIn = New(Script(Code, Granted));
        var ct = TestContext.Current.CancellationToken;

        Assert.False(signIn.IsSignedIn);
        Assert.Null(secrets.Get(GitHubApp.TokenName));
        Assert.True(signIn.NeedsReSignIn);

        await signIn.StartAsync(ct);
        await signIn.WaitAsync(ct);
        Assert.True(signIn.IsSignedIn);
        Assert.False(signIn.NeedsReSignIn);
    }

    [Fact]
    public async Task ATokenFromThisAppIsKept()
    {
        var signIn = New(Script(Code, Granted));
        var ct = TestContext.Current.CancellationToken;

        await signIn.StartAsync(ct);
        await signIn.WaitAsync(ct);
        Assert.True(New(Script()).IsSignedIn);
        Assert.False(New(Script()).NeedsReSignIn);
    }

    [Fact]
    public async Task AVerificationUriFromTheServerIsIgnored()
    {
        var http = new FakeHttp(_ => FakeHttp.Json(Code.Replace("https://github.com/login/device", "https://evil.example/login", StringComparison.Ordinal)));
        var start = await New(http).StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SignInState.Started, start.State);
        Assert.Equal(GitHubApp.DevicePage, start.Page);
    }

    [Theory]
    [InlineData("\"user_code\":\"WDJB-MJHT\"", "\"user_code\":\"wdjb-mjht\"")]
    [InlineData("\"user_code\":\"WDJB-MJHT\"", "\"user_code\":\"WDJB MJHT\"")]
    [InlineData("\"user_code\":\"WDJB-MJHT\"", "\"user_code\":\"WDJ\"")]
    [InlineData("\"user_code\":\"WDJB-MJHT\"", "\"user_code\":\"ABCDEFGHIJKLMNOPQRSTU\"")]
    [InlineData("\"user_code\":\"WDJB-MJHT\"", "\"user_code\":\"WDJB-MJHT\\n\"")]
    [InlineData("\"user_code\":\"WDJB-MJHT\"", "\"user_code\":\"WDJB\\u202E-MJHT\"")]
    [InlineData("\"interval\":5", "\"interval\":0")]
    [InlineData("\"interval\":5", "\"interval\":61")]
    [InlineData("\"interval\":5", "\"interval\":\"5\"")]
    [InlineData("\"expires_in\":900", "\"expires_in\":59")]
    [InlineData("\"expires_in\":900", "\"expires_in\":1801")]
    [InlineData("\"expires_in\":900", "\"expires_in\":null")]
    [InlineData("\"device_code\":\"3584d83530557fdd1f46af8289938c8ef79f9dc5\"", "\"device_code\":\"\"")]
    [InlineData("\"device_code\":\"3584d83530557fdd1f46af8289938c8ef79f9dc5\"", "\"device_code\":\"a b\"")]
    public async Task AReplyOutsideTheDocumentedRangesFails(string from, string to)
    {
        var http = new FakeHttp(_ => FakeHttp.Json(Code.Replace(from, to, StringComparison.Ordinal)));
        var start = await New(http).StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SignInState.Failed, start.State);
        Assert.Empty(start.UserCode);
    }

    [Fact]
    public async Task AMissingIntervalMeansFiveSeconds()
    {
        var replies = new Queue<string>([Code.Replace(",\"interval\":5", string.Empty, StringComparison.Ordinal), Granted]);
        var signIn = New(new FakeHttp(_ => FakeHttp.Json(replies.Dequeue())));
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal(SignInState.Started, (await signIn.StartAsync(ct)).State);
        Assert.Equal(SignInState.SignedIn, await signIn.WaitAsync(ct));
        Assert.Equal("5", Waits());
    }

    [Fact]
    public async Task ARedirectOrServerErrorAtStartFails()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(SignInState.Failed, (await New(new FakeHttp(_ => FakeHttp.Redirect("https://evil.example/"))).StartAsync(ct)).State);
        Assert.Equal(SignInState.Failed, (await New(new FakeHttp(_ => FakeHttp.Status(HttpStatusCode.BadGateway))).StartAsync(ct)).State);
        Assert.Equal(SignInState.Failed, (await New(new FakeHttp(_ => FakeHttp.Json(new string(' ', DeviceFlow.MaxBytes + 1)))).StartAsync(ct)).State);
    }

    [Fact]
    public async Task PendingThenGrantedSavesTheTokenAndPollsAtTheFixedAddress()
    {
        var http = Script(Code, Pending, Pending, Granted);
        var signIn = New(http);
        var ct = TestContext.Current.CancellationToken;

        await signIn.StartAsync(ct);
        Assert.Equal(SignInState.SignedIn, await signIn.WaitAsync(ct));

        Assert.Equal(Token, secrets.Get(GitHubApp.TokenName));
        Assert.True(signIn.IsSignedIn);
        Assert.Equal(4, http.Seen.Count);
        Assert.All(http.Seen.Skip(1), s =>
        {
            Assert.Equal("https://github.com/login/oauth/access_token", s.Uri.AbsoluteUri);
            Assert.Equal(
                "client_id=Ov23liTestClient0001&device_code=3584d83530557fdd1f46af8289938c8ef79f9dc5&grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Adevice_code",
                s.Body);
        });
        Assert.All(http.Seen, s => Assert.Equal("github.com", s.Uri.Host));
        Assert.Equal("5,5,5", Waits());
    }

    [Fact]
    public async Task SlowDownAddsFiveSecondsToEveryLaterWait()
    {
        var signIn = New(Script(Code, SlowDown, Pending, Granted));
        var ct = TestContext.Current.CancellationToken;

        await signIn.StartAsync(ct);
        Assert.Equal(SignInState.SignedIn, await signIn.WaitAsync(ct));
        Assert.Equal("5,10,10", Waits());
    }

    [Theory]
    [InlineData("""{"error":"expired_token"}""", SignInState.Expired)]
    [InlineData("""{"error":"access_denied"}""", SignInState.Denied)]
    [InlineData("""{"error":"incorrect_device_code"}""", SignInState.Failed)]
    [InlineData("""{"error":"device_flow_disabled"}""", SignInState.Failed)]
    [InlineData("""{"access_token":"gho_bad token"}""", SignInState.Failed)]
    [InlineData("""{"access_token":"gho_x\r\nX-Evil: 1"}""", SignInState.Failed)]
    [InlineData("""[]""", SignInState.Failed)]
    public async Task EveryEndingIsReportedAndNothingIsSaved(string answer, SignInState expected)
    {
        var signIn = New(Script(Code, answer));
        var ct = TestContext.Current.CancellationToken;

        await signIn.StartAsync(ct);
        Assert.Equal(expected, await signIn.WaitAsync(ct));
        Assert.Null(secrets.Get(GitHubApp.TokenName));
    }

    [Fact]
    public async Task AFailedPollIsTriedAgain()
    {
        var signIn = New(Script(Code, null, null, Granted));
        var ct = TestContext.Current.CancellationToken;

        await signIn.StartAsync(ct);
        Assert.Equal(SignInState.SignedIn, await signIn.WaitAsync(ct));
    }

    [Fact]
    public async Task PollingStopsWhenTheCodeRunsOut()
    {
        var http = new FakeHttp(s => FakeHttp.Json(s.Uri.AbsolutePath == "/login/device/code" ? Code : Pending));
        var signIn = New(http);
        var ct = TestContext.Current.CancellationToken;

        await signIn.StartAsync(ct);
        Assert.Equal(SignInState.Expired, await signIn.WaitAsync(ct));
        Assert.Equal(900 / 5, http.Seen.Count);
    }

    [Fact]
    public async Task CancelEndsTheWait()
    {
        var signIn = new GitHubSignIn(secrets, Client, Script(Code), () => clock.Now, (_, token) => Task.Delay(Timeout.Infinite, token));
        var ct = TestContext.Current.CancellationToken;

        await signIn.StartAsync(ct);
        var waiting = signIn.WaitAsync(ct);
        signIn.Cancel();
        Assert.Equal(SignInState.Cancelled, await waiting);
    }

    [Fact]
    public async Task CancellingTheTokenEndsTheWait()
    {
        using var stop = new CancellationTokenSource();
        var signIn = new GitHubSignIn(secrets, Client, Script(Code), () => clock.Now, (_, token) => Task.Delay(Timeout.Infinite, token));

        await signIn.StartAsync(TestContext.Current.CancellationToken);
        var waiting = signIn.WaitAsync(stop.Token);
        await stop.CancelAsync();
        Assert.Equal(SignInState.Cancelled, await waiting);
    }

    [Fact]
    public async Task ANewSignInCancelsTheOldOne()
    {
        var signIn = new GitHubSignIn(secrets, Client, new FakeHttp(_ => FakeHttp.Json(Code)), () => clock.Now, (_, token) => Task.Delay(Timeout.Infinite, token));
        var ct = TestContext.Current.CancellationToken;

        await signIn.StartAsync(ct);
        var first = signIn.WaitAsync(ct);
        Assert.Equal(SignInState.Started, (await signIn.StartAsync(ct)).State);
        Assert.Equal(SignInState.Cancelled, await first);
        signIn.Cancel();
    }

    [Fact]
    public async Task WaitingWithoutAStartFails() =>
        Assert.Equal(SignInState.Failed, await New(Script()).WaitAsync(TestContext.Current.CancellationToken));

    [Fact]
    public async Task ATokenThatCannotBeSavedIsAFailure()
    {
        secrets.Refuse = true;
        var signIn = New(Script(Code, Granted));
        var ct = TestContext.Current.CancellationToken;

        await signIn.StartAsync(ct);
        Assert.Equal(SignInState.Failed, await signIn.WaitAsync(ct));
        Assert.False(signIn.IsSignedIn);
    }

    [Fact]
    public async Task SignOutForgetsTheToken()
    {
        var signIn = New(Script(Code, Granted));
        var ct = TestContext.Current.CancellationToken;

        await signIn.StartAsync(ct);
        await signIn.WaitAsync(ct);
        signIn.SignOut();
        Assert.Null(secrets.Get(GitHubApp.TokenName));
        Assert.False(signIn.IsSignedIn);
    }

    [Fact]
    public async Task TheTokenNeverReachesTheLog()
    {
        var signIn = New(Script(Code, null, Granted));
        var ct = TestContext.Current.CancellationToken;

        await signIn.StartAsync(ct);
        Assert.Equal(SignInState.SignedIn, await signIn.WaitAsync(ct));
        Assert.DoesNotContain(Token, LogText.Read(), StringComparison.Ordinal);
        Assert.DoesNotContain("3584d83530557fdd1f46af8289938c8ef79f9dc5", LogText.Read(), StringComparison.Ordinal);
    }

    private GitHubSignIn New(FakeHttp http) => new(secrets, Client, http, () => clock.Now, Wait);

    private string Waits() => string.Join(",", waits.Select(w => (int)w.TotalSeconds));

    /// <summary>A wait that takes no time: it moves the clock and records how long it was asked to be.</summary>
    private Task Wait(TimeSpan span, CancellationToken ct)
    {
        waits.Add(span);
        clock.Advance(span);
        return Task.CompletedTask;
    }

    /// <summary>Answers in order; a null entry is a network failure.</summary>
    private static FakeHttp Script(params string?[] replies)
    {
        var queue = new Queue<string?>(replies);
        return new FakeHttp(_ => queue.Dequeue() is { } json
            ? FakeHttp.Json(json)
            : throw new HttpRequestException("No route to host"));
    }

    private sealed class MemorySecrets : ISecretStore
    {
        private readonly Dictionary<string, string> values = [];

        public bool Refuse { get; set; }

        public string? Get(string name) => values.GetValueOrDefault(name);

        public bool Set(string name, string value)
        {
            if (Refuse)
            {
                return false;
            }

            values[name] = value;
            return true;
        }

        public void Delete(string name) => values.Remove(name);
    }
}
