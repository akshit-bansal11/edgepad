using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using Edgepad.Hooks;
using Xunit;

namespace Edgepad.Tests;

/// <summary>Holds HookClient to the CLIENT lines of protocol/hook-vectors.txt (the Swift twin reads them too) and its failure paths.</summary>
public sealed class HookClientTests : IDisposable
{
    private const string Token = "abcdefghijklmnop_-0123456789";

    private static readonly string Endpoint = OperatingSystem.IsWindows() ? @"\\.\pipe\edgepad-hook-0123456789abcdef" : "/run/user/1000/edgepad/hook.sock";

    private readonly DirectoryInfo dir = Directory.CreateTempSubdirectory("edgepad-hookclient");

    public void Dispose() => dir.Delete(recursive: true);

    private string HookFile(string? json = null)
    {
        var file = Path.Combine(dir.FullName, "hook.json");
        File.WriteAllText(file, json ?? $$"""{"v":1,"endpoint":"{{Endpoint.Replace(@"\", @"\\", StringComparison.Ordinal)}}","token":"{{Token}}","pid":1}""");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return file;
    }

    [Fact]
    public void EveryClientVectorSendsExactlyItsRequest()
    {
        var count = 0;
        foreach (var line in Fixtures.Lines("hook-vectors.txt").Where(l => l.StartsWith("CLIENT ", StringComparison.Ordinal)))
        {
            var at = line.IndexOf(" => ", StringComparison.Ordinal);
            var words = line[..at].Split(' ', 4);
            var (agent, kind, input) = (words[1], words[2], words[3]);
            var want = line[(at + 4)..];
            var server = new FakeServer("""{"v":1,"decision":"defer"}""");
            var stdin = kind is "perm" or "session" ? input : string.Empty;
            string[] args = kind is "perm" or "session" ? ["--hook", agent, kind] : ["--hook", agent, kind, input];
            var stdout = new StringWriter();

            Assert.Equal(0, HookClient.Run(args, new StringReader(stdin), stdout, server.Options(HookFile())));

            Assert.Equal(string.Empty, stdout.ToString());
            if (want == "NONE")
            {
                Assert.True(server.Requests.Count == 0, line);
            }
            else
            {
                var sent = Assert.Single(server.Requests);
                var request = JsonNode.Parse(sent)!.AsObject();
                Assert.Equal(Token, request["token"]!.GetValue<string>());
                request.Remove("token");
                Assert.True(JsonNode.DeepEquals(JsonNode.Parse(want), request), $"{line}\n sent {request.ToJsonString()}");
                Assert.Equal(Endpoint, server.Endpoint);
            }

            count++;
        }

        Assert.True(count >= 9, $"only {count} CLIENT vectors");
    }

    [Theory]
    [InlineData("""{"v":1,"decision":"allow"}""", """{"hookSpecificOutput":{"hookEventName":"PermissionRequest","decision":{"behavior":"allow"}}}""")]
    [InlineData("""{"v":1,"decision":"deny"}""", """{"hookSpecificOutput":{"hookEventName":"PermissionRequest","decision":{"behavior":"deny","message":"Denied from Edgepad on the phone."}}}""")]
    [InlineData("""{"v":1,"decision":"defer"}""", null)]
    [InlineData("""{"v":2,"decision":"allow"}""", null)]
    [InlineData("""{"v":1,"decision":"ALLOW"}""", null)]
    [InlineData("not json", null)]
    [InlineData("", null)]
    public void OnlyAnAllowOrDenyIsPrinted(string reply, string? printed)
    {
        var server = new FakeServer(reply);
        var stdout = new StringWriter();

        Assert.Equal(0, HookClient.Run(["--hook", "claude", "perm"], new StringReader("""{"tool_name":"Bash","tool_input":{"command":"ls"}}"""), stdout, server.Options(HookFile())));

        if (printed is null)
        {
            Assert.Equal(string.Empty, stdout.ToString());
        }
        else
        {
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(printed), JsonNode.Parse(stdout.ToString())), stdout.ToString());
            Assert.EndsWith("\n", stdout.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(Token, stdout.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ARequestWithoutTheAgentsReplyPrintsNothingOnceTheWaitEnds()
    {
        var server = new FakeServer(reply: null);
        var stdout = new StringWriter();
        var started = Environment.TickCount64;

        Assert.Equal(0, HookClient.Run(["--hook", "codex", "perm"], new StringReader("""{"tool_name":"shell"}"""), stdout, server.Options(HookFile(), TimeSpan.FromMilliseconds(300))));

        Assert.Equal(string.Empty, stdout.ToString());
        Assert.Single(server.Requests);
        Assert.InRange(Environment.TickCount64 - started, 250, 8000);
    }

    [Fact]
    public void AMissingFileOrADeadEndpointSendsAndPrintsNothing()
    {
        var stdout = new StringWriter();
        var server = new FakeServer("""{"v":1,"decision":"allow"}""");
        Assert.Equal(0, HookClient.Run(["--hook", "claude", "perm"], new StringReader("""{"tool_name":"Bash"}"""), stdout, server.Options(Path.Combine(dir.FullName, "none.json"))));
        Assert.True(server.Requests.Count == 0);

        var dead = new HookClientOptions { File = HookFile(), Connect = (_, _) => throw new TimeoutException() };
        Assert.Equal(0, HookClient.Run(["--hook", "claude", "perm"], new StringReader("""{"tool_name":"Bash"}"""), stdout, dead));
        Assert.Equal(string.Empty, stdout.ToString());
    }

    [Theory]
    [InlineData("""{"v":1,"endpoint":"\\\\.\\pipe\\other","token":"abcdefghijklmnop"}""")]
    [InlineData("""{"v":1,"endpoint":"relative.sock","token":"abcdefghijklmnop"}""")]
    [InlineData("""{"v":1,"endpoint":"/x.sock","token":"short"}""")]
    [InlineData("""{"v":2,"endpoint":"/x.sock","token":"abcdefghijklmnop"}""")]
    [InlineData("""{"v":1,"endpoint":"/x.sock","token":"abcdefghijklmnop!"}""")]
    [InlineData("""not json""")]
    [InlineData("""[]""")]
    public void ABadHookFileSendsNothing(string json)
    {
        var server = new FakeServer("""{"v":1,"decision":"allow"}""");
        var stdout = new StringWriter();
        Assert.Equal(0, HookClient.Run(["--hook", "claude", "perm"], new StringReader("""{"tool_name":"Bash"}"""), stdout, server.Options(HookFile(json))));
        Assert.True(server.Requests.Count == 0);
        Assert.Equal(string.Empty, stdout.ToString());
    }

    [Fact]
    public void AHookFileOthersCanReadIsRefused()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var file = HookFile();
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        var server = new FakeServer("""{"v":1,"decision":"allow"}""");
        HookClient.Run(["--hook", "claude", "perm"], new StringReader("""{"tool_name":"Bash"}"""), new StringWriter(), server.Options(file));
        Assert.True(server.Requests.Count == 0);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("""{"tool_name":""}""")]
    [InlineData("""{"tool_name":7}""")]
    public void GarbageOnStdinSendsNothing(string stdin)
    {
        var server = new FakeServer("""{"v":1,"decision":"allow"}""");
        var stdout = new StringWriter();
        Assert.Equal(0, HookClient.Run(["--hook", "claude", "perm"], new StringReader(stdin), stdout, server.Options(HookFile())));
        Assert.True(server.Requests.Count == 0);
        Assert.Equal(string.Empty, stdout.ToString());
    }

    [Fact]
    public void StdinOver1MiBSendsNothing()
    {
        var big = """{"tool_name":"Bash","pad":""" + "\"" + new string('x', HookClient.StdinCap) + "\"}";
        var server = new FakeServer("""{"v":1,"decision":"allow"}""");
        Assert.Equal(0, HookClient.Run(["--hook", "claude", "perm"], new StringReader(big), new StringWriter(), server.Options(HookFile())));
        Assert.True(server.Requests.Count == 0);
    }

    [Theory]
    [InlineData("--hook", "gemini", "perm")]
    [InlineData("--hook", "claude", "shell")]
    [InlineData("--hook", "claude")]
    [InlineData("--hooks", "claude", "perm")]
    public void UnknownArgumentsSendNothing(params string[] args)
    {
        var server = new FakeServer("""{"v":1,"decision":"allow"}""");
        Assert.Equal(0, HookClient.Run(args, new StringReader("""{"tool_name":"Bash"}"""), new StringWriter(), server.Options(HookFile())));
        Assert.True(server.Requests.Count == 0);
    }

    [Fact]
    public void AUsageArgumentOverTheCapOrNotAnObjectSendsNothing()
    {
        var server = new FakeServer(null);
        var big = """{"x":""" + "\"" + new string('x', HookClient.ArgCap) + "\"}";
        foreach (var arg in new[] { big, "[1]", "7", "nope" })
        {
            HookClient.Run(["--hook", "claude", "usage", arg], new StringReader(string.Empty), new StringWriter(), server.Options(HookFile()));
        }

        HookClient.Run(["--hook", "claude", "usage"], new StringReader(string.Empty), new StringWriter(), server.Options(HookFile()));
        Assert.True(server.Requests.Count == 0);
    }

    [Fact]
    public void ACodexCommandArrayTooLongJoinedIsSentCutAsAString()
    {
        var part = new string('a', 3000);
        var server = new FakeServer("""{"v":1,"decision":"defer"}""");
        HookClient.Run(["--hook", "codex", "perm"], new StringReader($$$"""{"tool_name":"shell","tool_input":{"command":["{{{part}}}","{{{part}}}"]}}"""), new StringWriter(), server.Options(HookFile()));
        var d = JsonNode.Parse(Assert.Single(server.Requests))!["d"]!;
        Assert.Equal(HookClient.InputCap, d["tool_input"]!["command"]!.GetValue<string>().Length);
        Assert.True(d["truncated"]!.GetValue<bool>());
    }

    [Fact]
    public void ACutStringIsMarkedTruncatedAndAnUncutOneIsNot()
    {
        var server = new FakeServer("""{"v":1,"decision":"defer"}""");
        foreach (var n in new[] { HookClient.InputCap, HookClient.InputCap + 1 })
        {
            HookClient.Run(["--hook", "claude", "perm"], new StringReader($$$"""{"tool_name":"Bash","tool_input":{"command":"{{{new string('a', n)}}}"}}"""), new StringWriter(), server.Options(HookFile()));
        }

        var requests = server.Requests.ToList();
        Assert.Equal(2, requests.Count);
        Assert.Null(JsonNode.Parse(requests[0])!["d"]!["truncated"]);
        Assert.True(JsonNode.Parse(requests[1])!["d"]!["truncated"]!.GetValue<bool>());
    }

    [Fact]
    public void IsHookLooksOnlyAtTheFirstArgument()
    {
        Assert.True(HookClient.IsHook(["--hook", "claude", "perm"]));
        Assert.False(HookClient.IsHook([]));
        Assert.False(HookClient.IsHook(["--minimized", "--hook"]));
    }

    /// <summary>The app's end of the channel: records each request line and, when it has one, writes the reply line.</summary>
    private sealed class FakeServer(string? reply)
    {
        private readonly List<Thread> served = [];
        private readonly ConcurrentQueue<string> requests = new();

        /// <summary>Every request line, once each served connection has been read up to its line feed.</summary>
        public ConcurrentQueue<string> Requests
        {
            get
            {
                served.ForEach(t => t.Join(TimeSpan.FromSeconds(15)));
                return requests;
            }
        }

        public string? Endpoint { get; private set; }

        public HookClientOptions Options(string file, TimeSpan? wait = null) => new()
        {
            File = file,
            ReplyWait = wait ?? TimeSpan.FromSeconds(5),
            Connect = (endpoint, _) =>
            {
                Endpoint = endpoint;
                var (client, app) = Duplex.Create();
                var thread = new Thread(() => Serve(app)) { IsBackground = true };
                thread.Start();
                served.Add(thread);
                return client;
            },
        };

        // The client writes after Connect returns, so the line is read on its own thread.
        private void Serve(Stream app)
        {
            try
            {
                var line = new List<byte>();
                var one = new byte[1];
                while (app.Read(one) == 1 && one[0] != (byte)'\n')
                {
                    line.Add(one[0]);
                }

                requests.Enqueue(Encoding.UTF8.GetString([.. line]));
                if (reply is not null)
                {
                    app.Write(Encoding.UTF8.GetBytes(reply + "\n"));
                }
            }
            catch (Exception e) when (e is IOException or TimeoutException)
            {
                // The client already gave up, or never wrote: the test sees no request.
            }
        }
    }
}
