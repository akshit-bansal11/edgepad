using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Edgepad.Dashboard;
using Edgepad.Hooks;
using Edgepad.Protocol;
using Xunit;

namespace Edgepad.Tests;

public sealed class HookServerTests : IDisposable
{
    private const string Token = "tok_ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789abcdefg";

    private readonly DirectoryInfo dir = Directory.CreateTempSubdirectory("edgepad-hooks");
    private readonly FakeClock clock = new();
    private readonly LoopbackListener listener = new();
    private readonly IntegrationSettings settings;
    private readonly UsageStore usage;
    private readonly SessionBoard sessions;
    private readonly PermissionBroker broker;
    private readonly HookServer server;
    private readonly List<PermPrompt> asked = [];
    private readonly List<string> gone = [];
    private bool linked = true;

    public HookServerTests()
    {
        settings = new IntegrationSettings(Path.Combine(dir.FullName, IntegrationSettings.FileName));
        settings.SetFlag(IntegrationSettings.Claude, true);
        settings.SetFlag(IntegrationSettings.Codex, true);
        usage = new UsageStore(Path.Combine(dir.FullName, UsageStore.FileName), clock.Now);
        sessions = new SessionBoard(() => clock.Now);
        broker = new PermissionBroker(sessions, () => linked, () => clock.Now, TimeSpan.FromSeconds(10));
        broker.Asked += p =>
        {
            lock (asked)
            {
                asked.Add(p);
            }
        };
        broker.Gone += id =>
        {
            lock (gone)
            {
                gone.Add(id);
            }
        };
        server = new HookServer(listener, () => Token, broker, sessions, new LimitsBoard(usage), new SpendBoard(usage), usage, settings, () => clock.Now, TimeSpan.FromMilliseconds(300));
        server.Start();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        server.Dispose();
        dir.Delete(recursive: true);
    }

    private static string Request(string agent, string kind, string d, string token = Token) =>
        $$"""{"v":1,"token":"{{token}}","agent":"{{agent}}","kind":"{{kind}}","d":{{d}}}""";

    private static string Perm(string command) => Request(AgentToken.Claude, "perm", JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["session_id"] = "s1",
        ["cwd"] = "/work/app",
        ["tool_name"] = "Bash",
        ["tool_input"] = new Dictionary<string, string> { ["command"] = command },
    }));

    private static string Start(string session = "s1", string agent = AgentToken.Claude) =>
        Request(agent, "session", $$"""{"hook_event_name":"SessionStart","session_id":"{{session}}","cwd":"/work/app"}""");

    private async Task<TcpClient> Connect()
    {
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, listener.Port, Ct);
        client.ReceiveTimeout = 10_000;
        return client;
    }

    // Sends the bytes as they are and reads the one reply line; null when the server closed without one.
    private async Task<string?> Exchange(string text)
    {
        using var client = await Connect();
        var stream = client.GetStream();
        try
        {
            stream.Write(Encoding.UTF8.GetBytes(text));
        }
        catch (IOException)
        {
            // The server closed while the bytes were still going: refused.
            return null;
        }

        return ReadReply(stream);
    }

    private static string? ReadReply(Stream stream)
    {
        var reply = new List<byte>();
        var one = new byte[1];
        try
        {
            while (stream.Read(one) == 1)
            {
                if (one[0] == '\n')
                {
                    return Encoding.UTF8.GetString(reply.ToArray());
                }

                reply.Add(one[0]);
            }
        }
        catch (IOException)
        {
            // Reset by the server: the same as closed.
        }

        return null;
    }

    private PermPrompt WaitAsked(int count = 1)
    {
        for (var i = 0; i < 1000; i++)
        {
            lock (asked)
            {
                if (asked.Count >= count)
                {
                    return asked[count - 1];
                }
            }

            Thread.Sleep(10);
        }

        throw new TimeoutException("no ask arrived");
    }

    private static void Until(Func<bool> condition)
    {
        for (var i = 0; i < 1000 && !condition(); i++)
        {
            Thread.Sleep(10);
        }

        Assert.True(condition());
    }

    [Fact]
    public async Task ASessionEventIsAppliedAndAnsweredOk()
    {
        Assert.Equal("{\"v\":1,\"ok\":true}", await Exchange(Start() + "\n"));
        Assert.Single(sessions.Snapshot());
        Assert.NotEqual(-1, usage.LastHeard(AgentToken.Claude));
    }

    [Fact]
    public async Task UsageAndSpendReachTheirBoards()
    {
        Assert.NotNull(await Exchange(Request(AgentToken.Claude, "usage", """{"limits":[{"kind":"five_hour","percentUsed":12,"resetsAt":null}],"context":{"percent":40}}""") + "\n"));
        Assert.NotNull(await Exchange(Request(AgentToken.Claude, "spend", """{"today":{"tok":1,"cents":2},"week":{"tok":3,"cents":4},"month":{"tok":5,"cents":6}}""") + "\n"));

        lock (usage.Gate)
        {
            Assert.Equal(12, usage.Limits[(AgentToken.Claude, "300")].Pct);
            Assert.Equal(40, usage.Limits[(AgentToken.Claude, LimitWindow.Context)].Pct);
            Assert.Equal(new SpendTotals(1, 2, 3, 4, 5, 6), usage.Spend[AgentToken.Claude].Totals);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("tok_ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789abcdeg")]
    [InlineData("tok_ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789abcdef")]
    [InlineData("tok_ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789abcdefgh")]
    public async Task AWrongTokenIsClosedWithoutAReply(string token)
    {
        Assert.Null(await Exchange(Request(AgentToken.Claude, "session", """{"hook_event_name":"Stop","session_id":"s"}""", token) + "\n"));
        Assert.Null(await Exchange("""{"v":1,"agent":"claude","kind":"session","d":{"hook_event_name":"Stop"}}""" + "\n"));
        Assert.Empty(sessions.Snapshot());
        Assert.Equal(-1, usage.LastHeard(AgentToken.Claude));
    }

    [Fact]
    public async Task OneBitOffIsAWrongToken()
    {
        var flipped = (char)(Token[^1] ^ 1);
        Assert.Null(await Exchange(Request(AgentToken.Claude, "session", """{"hook_event_name":"Stop","session_id":"s"}""", Token[..^1] + flipped) + "\n"));
    }

    [Theory]
    [InlineData("not json\n")]
    [InlineData("{\"v\":1}\n")]
    [InlineData("[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[1]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]\n")]
    public async Task MalformedLinesAreClosedWithoutAReply(string line) => Assert.Null(await Exchange(line));

    [Fact]
    public async Task UnknownKindsAgentsAndWrongTypesAreClosedWithoutAReply()
    {
        Assert.Null(await Exchange(Request(AgentToken.Claude, "shell", "{}") + "\n"));
        Assert.Null(await Exchange(Request("gemini", "session", """{"hook_event_name":"Stop"}""") + "\n"));
        Assert.Null(await Exchange(Request(AgentToken.Claude, "session", """{"hook_event_name":7}""") + "\n"));
        Assert.Null(await Exchange(Request(AgentToken.Claude, "perm", """{"tool_name":"rm -rf"}""") + "\n"));
        Assert.Null(await Exchange(Request(AgentToken.Claude, "session", "{\"hook_event_name\":\"Stop\",\"x\":" + new string('[', 20) + new string(']', 20) + "}") + "\n"));
    }

    [Fact]
    public async Task ALineOverTheCapIsClosed()
    {
        Assert.Null(await Exchange(new string('x', HookProtocol.MaxLineBytes + 1)));
        Assert.Null(await Exchange(Request(AgentToken.Claude, "session", "{\"hook_event_name\":\"Stop\",\"p\":\"" + new string('x', HookProtocol.MaxLineBytes) + "\"}") + "\n"));
    }

    [Fact]
    public async Task ASilentConnectionIsClosedAtTheDeadline()
    {
        using var client = await Connect();
        var started = Environment.TickCount64;
        Assert.Null(ReadReply(client.GetStream()));
        Assert.InRange(Environment.TickCount64 - started, 0, 5_000);
    }

    [Fact]
    public async Task ALineWithoutItsLineFeedIsNoRequest()
    {
        using var client = await Connect();
        var stream = client.GetStream();
        stream.Write(Encoding.UTF8.GetBytes(Start()));
        client.Client.Shutdown(SocketShutdown.Send);
        Assert.Null(ReadReply(stream));
        Assert.Empty(sessions.Snapshot());
    }

    [Fact]
    public async Task OnlyTheFirstOfTwoRequestsOnOneConnectionCounts()
    {
        using var client = await Connect();
        var stream = client.GetStream();
        stream.Write(Encoding.UTF8.GetBytes(Start("first") + "\n" + Start("second") + "\n"));
        Assert.NotNull(ReadReply(stream));
        Assert.Null(ReadReply(stream));
        Assert.Single(sessions.Snapshot());
    }

    [Fact]
    public async Task AllowAndDenyFromThePhoneReachTheHook()
    {
        using var client = await Connect();
        var stream = client.GetStream();
        stream.Write(Encoding.UTF8.GetBytes(Perm("ls -la") + "\n"));
        var ask = WaitAsked();
        Assert.Equal("claude\nBash\nls -la", ask.Body);
        Assert.True(broker.Answer(ask.Id + "/a"));
        Assert.Equal("{\"v\":1,\"decision\":\"allow\"}", ReadReply(stream));

        using var second = await Connect();
        var other = second.GetStream();
        other.Write(Encoding.UTF8.GetBytes(Perm("rm x") + "\n"));
        Assert.True(broker.Answer(WaitAsked(2).Id, false));
        Assert.Equal("{\"v\":1,\"decision\":\"deny\"}", ReadReply(other));
    }

    [Fact]
    public async Task ADisabledIntegrationDefersWithoutAsking()
    {
        settings.SetFlag(IntegrationSettings.Claude, false);
        Assert.Equal("{\"v\":1,\"decision\":\"defer\"}", await Exchange(Perm("ls") + "\n"));
        Assert.Equal("{\"v\":1,\"ok\":true}", await Exchange(Start() + "\n"));
        Assert.Empty(asked);
        Assert.Empty(sessions.Snapshot());
        Assert.Equal(-1, usage.LastHeard(AgentToken.Claude));
    }

    [Fact]
    public async Task NoPhoneDefersWithoutAsking()
    {
        linked = false;
        Assert.Equal("{\"v\":1,\"decision\":\"defer\"}", await Exchange(Perm("ls") + "\n"));
        Assert.Empty(asked);
    }

    [Fact]
    public async Task TheHookLeavingIsGoneAndALaterAnswerIsRefused()
    {
        var client = await Connect();
        client.GetStream().Write(Encoding.UTF8.GetBytes(Perm("ls") + "\n"));
        var id = WaitAsked().Id;
        client.Dispose();

        Until(() =>
        {
            lock (gone)
            {
                return gone.Contains(id);
            }
        });
        Assert.False(broker.Answer(id, true));
        Until(() => server.Active == 0);
    }

    [Fact]
    public async Task TheNinthConnectionIsClosedUnread()
    {
        var waiting = new List<TcpClient>();
        try
        {
            for (var i = 0; i < HookServer.MaxConnections; i++)
            {
                var client = await Connect();
                client.GetStream().Write(Encoding.UTF8.GetBytes(Perm("ls") + "\n"));
                waiting.Add(client);
            }

            WaitAsked(HookServer.MaxConnections);
            Until(() => server.Active == HookServer.MaxConnections);

            Assert.Null(await Exchange(Start() + "\n"));
            Assert.Empty(sessions.Snapshot());
        }
        finally
        {
            foreach (var client in waiting)
            {
                client.Dispose();
            }
        }
    }

    [Fact]
    public async Task AClientThatLeavesBeforeTheReplyHurtsNothing()
    {
        for (var i = 0; i < 5; i++)
        {
            using var client = await Connect();
            client.GetStream().Write(Encoding.UTF8.GetBytes(Start($"s{i}") + "\n"));
        }

        Assert.Equal("{\"v\":1,\"ok\":true}", await Exchange(Start("last") + "\n"));
        Until(() => server.Active == 0);
    }

    [Fact]
    public async Task TheFirstCodexRequestRecordsTrust()
    {
        var raised = 0;
        server.CodexTrusted += () => raised++;
        Assert.False(settings.Flag(IntegrationSettings.CodexTrusted));

        Assert.NotNull(await Exchange(Start("c1", AgentToken.Codex) + "\n"));
        Assert.NotNull(await Exchange(Start("c2", AgentToken.Codex) + "\n"));

        Assert.True(settings.Flag(IntegrationSettings.CodexTrusted));
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task NoCommandTextReachesTheLog()
    {
        var marker = "secret-" + Guid.NewGuid().ToString("N");
        using var client = await Connect();
        var stream = client.GetStream();
        stream.Write(Encoding.UTF8.GetBytes(Perm($"echo {marker}") + "\n"));
        broker.Answer(WaitAsked().Id, true);
        Assert.NotNull(ReadReply(stream));
        Assert.Null(await Exchange(Request(AgentToken.Claude, "perm", $$"""{"tool_name":"{{marker}}"}""") + "\n"));

        Assert.DoesNotContain(marker, LogText.Read(), StringComparison.Ordinal);
        Assert.DoesNotContain(Token, LogText.Read(), StringComparison.Ordinal);
    }

    // The hook channel over loopback TCP, so the server meets a real socket stream; the platforms supply pipes and Unix sockets.
    private sealed class LoopbackListener : IHookListener
    {
        private readonly TcpListener tcp = new(IPAddress.Loopback, 0);
        private volatile bool disposed;

        public LoopbackListener() => tcp.Start();

        public int Port => ((IPEndPoint)tcp.LocalEndpoint).Port;

        public string Endpoint => "loopback";

        public async Task<Stream?> AcceptAsync(CancellationToken cancellationToken)
        {
            try
            {
                var socket = await tcp.AcceptSocketAsync(cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception e) when (disposed && e is SocketException or ObjectDisposedException)
            {
                return null;
            }
        }

        public void Dispose()
        {
            disposed = true;
            tcp.Stop();
            tcp.Dispose();
        }
    }
}

public sealed class HookEndpointTests : IDisposable
{
    private readonly DirectoryInfo dir = Directory.CreateTempSubdirectory("edgepad-hookfile");

    public void Dispose() => dir.Delete(recursive: true);

    [Fact]
    public void TheFileNamesTheEndpointIsPrivateAndGoesOnDispose()
    {
        var file = Path.Combine(dir.FullName, "sub", HookEndpoint.FileName);
        string first;
        using (var endpoint = new HookEndpoint(file, @"\\.\pipe\edgepad-hook-0123456789abcdef"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;
            Assert.Equal(1, root.GetProperty("v").GetInt32());
            Assert.Equal(@"\\.\pipe\edgepad-hook-0123456789abcdef", root.GetProperty("endpoint").GetString());
            Assert.Equal(endpoint.Token, root.GetProperty("token").GetString());
            Assert.Equal(Environment.ProcessId, root.GetProperty("pid").GetInt32());
            Assert.Matches("^[A-Za-z0-9_-]{43}$", endpoint.Token);
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
            }

            first = endpoint.Token;
            Assert.True(endpoint.Rotate());
            Assert.NotEqual(first, endpoint.Token);
            using var again = JsonDocument.Parse(File.ReadAllText(file));
            Assert.Equal(endpoint.Token, again.RootElement.GetProperty("token").GetString());
        }

        Assert.False(File.Exists(file));
        using var next = new HookEndpoint(file, "/run/user/1000/edgepad/hook.sock");
        Assert.NotEqual(first, next.Token);
    }

    [Fact]
    public void ThePipeNameIsTheSidHash()
    {
        var name = HookEndpoint.PipeName("S-1-5-21-1-2-3-1001");
        Assert.Matches("^edgepad-hook-[0-9a-f]{16}$", name);
        Assert.Equal(name, HookEndpoint.PipeName("S-1-5-21-1-2-3-1001"));
        Assert.NotEqual(name, HookEndpoint.PipeName("S-1-5-21-1-2-3-1002"));
        Assert.Equal(@"\\.\pipe\" + name, HookEndpoint.PipePath("S-1-5-21-1-2-3-1001"));
    }
}

public sealed class UnixSocketListenerTests : IDisposable
{
    private readonly DirectoryInfo dir = Directory.CreateTempSubdirectory("edgepad-sock");

    public void Dispose() => dir.Delete(recursive: true);

    private string Folder => Path.Combine(dir.FullName, "edgepad");

    [Fact]
    public async Task ThisUsersPeerIsAcceptedAndTheSocketIsPrivate()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "SO_PEERCRED is Linux's");
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var listener = new UnixSocketListener(Folder);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Folder));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(listener.Endpoint));

        using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        var accept = listener.AcceptAsync(TestContext.Current.CancellationToken);
        await client.ConnectAsync(new UnixDomainSocketEndPoint(listener.Endpoint), TestContext.Current.CancellationToken);
        await using var stream = await accept;
        Assert.NotNull(stream);
    }

    [Fact]
    public async Task AnotherUsersPeerIsClosedBeforeAnythingIsRead()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "SO_PEERCRED is Linux's");
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var listener = new UnixSocketListener(Folder, _ => 4242, myUid: 1000);
        using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var accept = listener.AcceptAsync(stop.Token);
        await client.ConnectAsync(new UnixDomainSocketEndPoint(listener.Endpoint), TestContext.Current.CancellationToken);
        client.ReceiveTimeout = 5_000;
        Assert.Equal(0, client.Receive(new byte[1]));
        Assert.False(accept.IsCompleted);
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => accept);
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public void AFolderOthersMayEnterIsRefused()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Unix modes");
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        Directory.CreateDirectory(Folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.SetUnixFileMode(Folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);
        Assert.Throws<IOException>(() => new UnixSocketListener(Folder));
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public void ALinkedFolderIsRefused()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Unix links");
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var real = Directory.CreateDirectory(Path.Combine(dir.FullName, "real"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Directory.CreateSymbolicLink(Folder, real.FullName);
        Assert.Throws<IOException>(() => new UnixSocketListener(Folder));
    }
}
