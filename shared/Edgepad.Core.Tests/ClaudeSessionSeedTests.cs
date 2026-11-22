using Edgepad.Dashboard;
using Edgepad.Protocol;
using Xunit;

namespace Edgepad.Tests;

public sealed class ClaudeSessionSeedTests : IDisposable
{
    private const string Done = "{\"type\":\"assistant\",\"cwd\":\"C:\\\\work\\\\app\",\"message\":{\"stop_reason\":\"end_turn\"}}";
    private const string ToolUse = "{\"type\":\"assistant\",\"cwd\":\"C:\\\\work\\\\app\",\"message\":{\"stop_reason\":\"tool_use\"}}";
    private const string Prompt = "{\"type\":\"user\",\"cwd\":\"C:\\\\work\\\\app\",\"message\":{\"content\":\"hi\"}}";

    private readonly DateTimeOffset now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private readonly DirectoryInfo temp = Directory.CreateTempSubdirectory("edgepad-seed");
    private readonly List<long> bytesRead = [];

    public void Dispose() => temp.Delete(recursive: true);

    private ClaudeSpendWatcher Watcher() =>
        new(
            () => true,
            () => true,
            new SpendBoard(new UsageStore(Path.Combine(temp.FullName, UsageStore.FileName), now)),
            () => now,
            Path.Combine(temp.FullName, "claude"),
            Path.Combine(temp.FullName, ClaudeSpendWatcher.CacheName),
            path => new Counting(File.OpenRead(path), bytesRead),
            (_, _) => new Stopper());

    private string Transcript(string id, string content, TimeSpan age, string folder = "proj")
    {
        var path = Path.Combine(temp.FullName, "claude", "projects", folder, id + ".jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, now.UtcDateTime - age);
        return path;
    }

    [Fact]
    public void AnOlderSessionIsFoundWithItsFolderAndIdle()
    {
        Transcript("s1", Prompt + "\n" + Done + "\n", TimeSpan.FromMinutes(20));

        var seed = Assert.Single(Watcher().ListSessions());
        var board = new SessionBoard(() => now);
        board.Seed([seed]);

        var entry = Assert.Single(board.Snapshot());
        Assert.Equal(("s1", "app", SessionStateToken.Idle), (seed.SessionId, entry.Title, entry.State));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(10, false)]
    public void AnEmptyOrTinyFileIsIdleWithNoFolder(int bytes, bool busy)
    {
        var path = Transcript("s1", new string('x', bytes), TimeSpan.FromSeconds(5));

        var seed = Assert.Single(Watcher().ListSessions());

        Assert.Equal(busy, seed.State == SessionStateToken.Busy);
        Assert.Equal(string.Empty, seed.Cwd);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void AnUnfinishedTurnIsWorkingOnlyWhileTheFileIsFresh()
    {
        Transcript("a", Prompt + "\n", TimeSpan.FromSeconds(5));
        Transcript("b", Prompt + "\n" + ToolUse + "\n", TimeSpan.FromSeconds(100));
        Transcript("c", Prompt + "\n" + ToolUse + "\n", TimeSpan.FromMinutes(3));

        var states = Watcher().ListSessions().ToDictionary(s => s.SessionId, s => s.State);

        Assert.Equal(SessionStateToken.Busy, states["a"]);
        Assert.Equal(SessionStateToken.Busy, states["b"]);
        Assert.Equal(SessionStateToken.Idle, states["c"]);
    }

    [Fact]
    public void AFinishedTurnIsIdleEvenWhenJustWritten()
    {
        Transcript("a", ToolUse + "\n" + Done + "\n{\"type\":\"ai-title\"}\n", TimeSpan.FromSeconds(2));
        Transcript("b", ToolUse + "\n{\"type\":\"system\",\"subtype\":\"turn_duration\"}\n", TimeSpan.FromSeconds(2));

        Assert.All(Watcher().ListSessions(), s => Assert.Equal(SessionStateToken.Idle, s.State));
    }

    [Fact]
    public void AFileOlderThanSixHoursIsIgnored()
    {
        Transcript("old", Done + "\n", SessionBoard.Expiry + TimeSpan.FromMinutes(1));
        Transcript("new", Done + "\n", SessionBoard.Expiry - TimeSpan.FromMinutes(1));

        Assert.Equal("new", Assert.Single(Watcher().ListSessions()).SessionId);
    }

    [Fact]
    public void SubagentTranscriptsAreNotSessions()
    {
        Transcript("agent-1", Done + "\n", TimeSpan.FromMinutes(1), Path.Combine("proj", "s1", "subagents"));

        Assert.Empty(Watcher().ListSessions());
    }

    [Fact]
    public void AtMostEightAreListedAndSeeded()
    {
        for (var i = 0; i < 12; i++)
        {
            Transcript($"s{i}", Done + "\n", TimeSpan.FromMinutes(1 + i));
        }

        var seeds = Watcher().ListSessions();
        var board = new SessionBoard(() => now);
        board.Seed(seeds);

        Assert.Equal(SessionBoard.Capacity, seeds.Count);
        Assert.Equal(SessionBoard.Capacity, board.Snapshot().Count);
        Assert.DoesNotContain(seeds, s => s.SessionId == "s11");
    }

    [Fact]
    public void ALaterHookEventReplacesTheSeedWithoutADuplicate()
    {
        Transcript("s1", Prompt + "\n", TimeSpan.FromSeconds(5));
        var board = new SessionBoard(() => now);
        board.Seed(Watcher().ListSessions());
        Assert.Equal(SessionStateToken.Busy, Assert.Single(board.Snapshot()).State);

        board.Apply(new HookEvent(AgentToken.Claude, "s1", SessionEvent.Stop, "C:\\work\\app"));

        Assert.Equal(SessionStateToken.Idle, Assert.Single(board.Snapshot()).State);
    }

    [Fact]
    public void ASeedNeverOverridesWhatAHookAlreadySaid()
    {
        var board = new SessionBoard(() => now);
        board.Apply(new HookEvent(AgentToken.Claude, "s1", SessionEvent.NeedsYou, "C:\\work\\app"));

        board.Seed([new SeedSession(AgentToken.Claude, "s1", SessionStateToken.Idle, "C:\\other", now)]);

        var entry = Assert.Single(board.Snapshot());
        Assert.Equal((SessionStateToken.Wait, "app"), (entry.State, entry.Title));
    }

    [Fact]
    public void ASeedDoesNotPushOutAFullBoardOfLiveSessions()
    {
        var board = new SessionBoard(() => now);
        for (var i = 0; i < SessionBoard.Capacity; i++)
        {
            board.Apply(new HookEvent(AgentToken.Claude, $"live{i}", SessionEvent.Prompt, "/w"));
        }

        board.Seed([new SeedSession(AgentToken.Claude, "seed", SessionStateToken.Idle, "/w", now)]);

        Assert.DoesNotContain(board.Snapshot(), e => e.State == SessionStateToken.Idle);
        Assert.Equal(SessionBoard.Capacity, board.Snapshot().Count);
    }

    [Fact]
    public void ASeedPastTheExpiryIsIgnored()
    {
        var board = new SessionBoard(() => now);

        board.Seed([new SeedSession(AgentToken.Claude, "s1", SessionStateToken.Idle, "/w", now - SessionBoard.Expiry)]);

        Assert.Empty(board.Snapshot());
    }

    [Fact]
    public void AHugeFileIsReadOnlyByItsTail()
    {
        var path = Path.Combine(temp.FullName, "claude", "projects", "proj", "big.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var stream = File.Create(path))
        {
            // 10 MB of junk whose last line is cut mid-record at the 64 KB boundary, then a finished turn at the end.
            var chunk = new byte[1024 * 1024];
            Array.Fill(chunk, (byte)'x');
            for (var i = 0; i < 10; i++)
            {
                stream.Write(chunk);
            }

            stream.Write(System.Text.Encoding.UTF8.GetBytes("\n" + Prompt + "\n" + Done + "\n"));
        }

        File.SetLastWriteTimeUtc(path, now.UtcDateTime - TimeSpan.FromSeconds(1));

        var seed = Assert.Single(Watcher().ListSessions());

        Assert.Equal(SessionStateToken.Idle, seed.State);
        Assert.Equal("C:\\work\\app", seed.Cwd);
        Assert.True(bytesRead.Sum() <= ClaudeSpendWatcher.TailBytes);
    }

    private sealed class Stopper : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private sealed class Counting(Stream inner, List<long> reads) : Stream
    {
        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => inner.CanSeek;

        public override bool CanWrite => false;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = inner.Read(buffer, offset, count);
            reads.Add(n);
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
