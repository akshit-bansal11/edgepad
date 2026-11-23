using Edgepad.Dashboard;
using Edgepad.Protocol;
using Xunit;

namespace Edgepad.Tests;

public sealed class SessionBoardTests
{
    private DateTimeOffset now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    private SessionBoard NewBoard() => new(() => now);

    private static HookEvent Hook(string id, SessionEvent e, string agent = AgentToken.Claude, string cwd = "/work/app") => new(agent, id, e, cwd);

    [Fact]
    public void EveryTransitionLandsOnTheStateItNames()
    {
        var board = NewBoard();

        board.Apply(Hook("a", SessionEvent.Start));
        Assert.Equal(SessionStateToken.Idle, Assert.Single(board.Snapshot()).State);
        board.Apply(Hook("a", SessionEvent.Prompt));
        Assert.Equal(SessionStateToken.Busy, Assert.Single(board.Snapshot()).State);
        board.Apply(Hook("a", SessionEvent.NeedsYou));
        Assert.Equal(SessionStateToken.Wait, Assert.Single(board.Snapshot()).State);
        board.Apply(Hook("a", SessionEvent.Stop));
        Assert.Equal(SessionStateToken.Idle, Assert.Single(board.Snapshot()).State);
        board.Apply(Hook("a", SessionEvent.End));
        Assert.Empty(board.Snapshot());
    }

    [Fact]
    public void AContextFillGoesToTheNewestSessionOfThatFolderAndIsRoundedAndClamped()
    {
        var board = NewBoard();
        board.Apply(Hook("a", SessionEvent.Start, cwd: "/work/app/"));
        board.Apply(Hook("b", SessionEvent.Start, cwd: "/work/app"));
        board.Apply(Hook("c", SessionEvent.Start, cwd: "/work/other"));
        Assert.All(board.Snapshot(), s => Assert.Equal(-1, s.ContextPct));

        board.SetContext(AgentToken.Claude, "/work/app", 41.6);
        board.SetContext(AgentToken.Claude, "/work/other", 41.6);
        board.SetContext(AgentToken.Claude, "/nowhere", 90);
        board.SetContext(AgentToken.Codex, "/work/other", 90);

        // a and b share a folder, so neither takes the number; c is alone there.
        Assert.Equal([-1, -1, 42], board.Snapshot().Select(s => s.ContextPct).Reverse().ToArray());

        board.SetContext(AgentToken.Claude, "/work/other", 250);
        board.SetContextById(AgentToken.Claude, "a", double.NaN);
        board.SetContextById(AgentToken.Claude, "a", 12);
        Assert.Equal([12, -1, 100], board.Snapshot().Select(s => s.ContextPct).Reverse().ToArray());
    }

    [Fact]
    public void AChangedContextRaisesChangedAndTheSameOneDoesNot()
    {
        var board = NewBoard();
        board.Apply(Hook("a", SessionEvent.Start));
        var raised = 0;
        board.Changed += () => raised++;

        board.SetContext(AgentToken.Claude, "/work/app", 30);
        board.SetContext(AgentToken.Claude, "/work/app", 30);

        Assert.Equal(1, raised);
    }

    [Theory]
    [InlineData("SessionStart", null, SessionEvent.Start)]
    [InlineData("UserPromptSubmit", null, SessionEvent.Prompt)]
    [InlineData("Notification", "permission_prompt", SessionEvent.NeedsYou)]
    [InlineData("Notification", "idle_prompt", SessionEvent.NeedsYou)]
    [InlineData("Notification", "agent_needs_input", SessionEvent.NeedsYou)]
    [InlineData("Stop", null, SessionEvent.Stop)]
    [InlineData("SessionEnd", null, SessionEvent.End)]
    public void ClaudeHooksMapToEvents(string name, string? type, SessionEvent expected)
    {
        var hook = HookEvent.FromClaude(name, type, "s1", "/p");

        Assert.NotNull(hook);
        Assert.Equal(expected, hook.Event);
        Assert.Equal(AgentToken.Claude, hook.Agent);
    }

    [Theory]
    [InlineData("Notification", "agent_completed")]
    [InlineData("Notification", "auth_success")]
    [InlineData("Notification", null)]
    [InlineData("PermissionRequest", null)]
    [InlineData("PreToolUse", null)]
    [InlineData(null, null)]
    public void OtherClaudeHooksAreIgnored(string? name, string? type) =>
        Assert.Null(HookEvent.FromClaude(name, type, "s1", "/p"));

    [Fact]
    public void CodexMapsSessionStartOnly()
    {
        Assert.Equal(SessionEvent.Start, HookEvent.FromCodex("SessionStart", "c1", "/p")?.Event);
        Assert.Null(HookEvent.FromCodex("Stop", "c1", "/p"));
        Assert.Equal(AgentToken.Codex, HookEvent.FromCodex("SessionStart", "c1", "/p")?.Agent);
    }

    [Fact]
    public void ASessionWithAnAskPendingWaitsUntilItIsAnswered()
    {
        var board = NewBoard();
        board.Apply(Hook("a", SessionEvent.Prompt));

        board.SetWaiting("a", true);
        Assert.Equal(SessionStateToken.Wait, Assert.Single(board.Snapshot()).State);

        board.SetWaiting("a", false);
        Assert.Equal(SessionStateToken.Busy, Assert.Single(board.Snapshot()).State);
    }

    [Fact]
    public void AnAskForAnUnknownSessionDoesNothing()
    {
        var board = NewBoard();
        var raised = 0;
        board.Changed += () => raised++;

        board.SetWaiting("nobody", true);

        Assert.Empty(board.Snapshot());
        Assert.Equal(0, raised);
    }

    [Theory]
    [InlineData("/home/me/projects/edgepad", "edgepad")]
    [InlineData("/home/me/projects/edgepad/", "edgepad")]
    [InlineData(@"C:\work\my-proj\", "my-proj")]
    [InlineData(@"C:\work\a|b:c", "a b c")]
    [InlineData("", "")]
    public void TheTitleIsTheLastFolderCleaned(string cwd, string title) =>
        Assert.Equal(title, SingleTitle(cwd));

    private string SingleTitle(string cwd)
    {
        var board = NewBoard();
        board.Apply(Hook("a", SessionEvent.Start, cwd: cwd));
        return Assert.Single(board.Snapshot()).Title;
    }

    [Fact]
    public void ANewestFirstListIsKeptAndNeverPastEight()
    {
        var board = NewBoard();
        board.Apply(Hook("s0", SessionEvent.Prompt, cwd: "/w/0"));
        for (var i = 1; i <= 7; i++)
        {
            board.Apply(Hook($"s{i}", SessionEvent.Start, cwd: $"/w/{i}"));
        }

        board.Apply(Hook("s8", SessionEvent.Start, cwd: "/w/8"));

        var list = board.Snapshot();
        Assert.Equal(SessionBoard.Capacity, list.Count);
        Assert.Equal("8", list[0].Title);
        Assert.Contains(list, e => e.Title == "0");
        Assert.DoesNotContain(list, e => e.Title == "1");
    }

    [Fact]
    public void WithNoIdleSessionTheOldestMakesRoom()
    {
        var board = NewBoard();
        for (var i = 0; i < SessionBoard.Capacity; i++)
        {
            board.Apply(Hook($"s{i}", SessionEvent.Prompt, cwd: $"/w/{i}"));
        }

        board.Apply(Hook("new", SessionEvent.Prompt, cwd: "/w/new"));

        var list = board.Snapshot();
        Assert.Equal(SessionBoard.Capacity, list.Count);
        Assert.DoesNotContain(list, e => e.Title == "0");
        Assert.Equal("new", list[0].Title);
    }

    [Fact]
    public void ASessionNobodyTouchedForSixHoursIsForgotten()
    {
        var board = NewBoard();
        board.Apply(Hook("a", SessionEvent.Prompt));

        now += SessionBoard.Expiry - TimeSpan.FromMinutes(1);
        Assert.Single(board.Snapshot());

        now += TimeSpan.FromMinutes(1);
        Assert.Empty(board.Snapshot());
    }

    [Fact]
    public void ChangedFiresOnlyWhenWhatAPhoneSeesChanges()
    {
        var board = NewBoard();
        var raised = 0;
        board.Changed += () => raised++;

        board.Apply(Hook("a", SessionEvent.Start));
        Assert.Equal(1, raised);
        board.Apply(Hook("a", SessionEvent.Start));
        board.Apply(Hook("a", SessionEvent.Stop));
        Assert.Equal(1, raised);
        board.Apply(Hook("a", SessionEvent.Prompt));
        Assert.Equal(2, raised);
        board.Apply(Hook("a", SessionEvent.End));
        Assert.Equal(3, raised);
        board.Apply(Hook("a", SessionEvent.End));
        Assert.Equal(3, raised);
    }

    [Fact]
    public void ExpiryRaisesChanged()
    {
        var board = NewBoard();
        board.Apply(Hook("a", SessionEvent.Start));
        var raised = 0;
        board.Changed += () => raised++;

        now += SessionBoard.Expiry;
        board.Sweep();

        Assert.Equal(1, raised);
    }

    [Theory]
    [InlineData("", "claude")]
    [InlineData("id", "grok")]
    public void ABadIdOrAgentIsIgnored(string id, string agent)
    {
        var board = NewBoard();

        board.Apply(new HookEvent(agent, id, SessionEvent.Start, "/p"));

        Assert.Empty(board.Snapshot());
    }

    [Fact]
    public void AnOverlongIdIsIgnored()
    {
        var board = NewBoard();

        board.Apply(Hook(new string('x', SessionBoard.MaxIdLength + 1), SessionEvent.Start));

        Assert.Empty(board.Snapshot());
    }

    [Fact]
    public void TheSameIdUnderTwoAgentsIsTwoSessions()
    {
        var board = NewBoard();

        board.Apply(Hook("same", SessionEvent.Start));
        board.Apply(Hook("same", SessionEvent.Prompt, AgentToken.Codex));

        Assert.Equal(2, board.Snapshot().Count);
    }

    [Fact]
    public async Task FourThreadsNeverCorruptTheBoard()
    {
        var board = NewBoard();
        var errors = 0;
        board.Changed += () => board.Snapshot();

        await Task.WhenAll(Enumerable.Range(0, 4).Select(t => Task.Run(() =>
        {
            try
            {
                for (var i = 0; i < 500; i++)
                {
                    board.Apply(Hook($"t{t}-{i % 12}", (SessionEvent)(i % 5), cwd: $"/w/{t}"));
                    board.SetWaiting($"t{t}-{i % 12}", i % 2 == 0);
                    board.Snapshot();
                }
            }
            catch (Exception)
            {
                Interlocked.Increment(ref errors);
            }
        }, TestContext.Current.CancellationToken)));

        Assert.Equal(0, errors);
        Assert.InRange(board.Snapshot().Count, 0, SessionBoard.Capacity);
    }
}

public sealed class LimitsBoardTests : IDisposable
{
    private readonly DirectoryInfo dir = Directory.CreateTempSubdirectory("edgepad-limits");
    private readonly DateTimeOffset start = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    public void Dispose() => dir.Delete(recursive: true);

    private string UsagePath => Path.Combine(dir.FullName, UsageStore.FileName);

    private LimitsBoard NewBoard(out UsageStore store)
    {
        store = new UsageStore(UsagePath, start);
        return new LimitsBoard(store);
    }

    [Theory]
    [InlineData("claude", "five_hour", "300")]
    [InlineData("claude", "seven_day", "10080")]
    [InlineData("claude", "spend_limit", "~spend_limit")]
    [InlineData("claude", "weird_kind", "~weird_kind")]
    [InlineData("claude", "300", "~300")]
    [InlineData("claude", "", null)]
    [InlineData("claude", "   ", null)]
    [InlineData("codex", "10080", "10080")]
    [InlineData("codex", "43200", "43200")]
    [InlineData("codex", "0", null)]
    [InlineData("codex", "600000", null)]
    [InlineData("codex", "5h", null)]
    [InlineData("grok", "300", null)]
    public void WindowsMapToTheWireForm(string agent, string window, string? expected) =>
        Assert.Equal(expected, LimitsBoard.WindowFor(agent, window));

    [Fact]
    public void ClaudeKindsAreKeptNeverDropped()
    {
        var board = NewBoard(out _);

        board.Update(
            AgentToken.Claude,
            [new("five_hour", 40, null), new("seven_day", 10, null), new("spend_limit", 5, null), new("weird_kind", 7, null), new(string.Empty, 99, null)],
            null,
            start);

        Assert.Equal(["300", "10080", "~spend_limit", "~weird_kind"], board.Snapshot().Select(l => l.Window).ToArray());
    }

    [Fact]
    public void PercentagesRoundAndClamp()
    {
        var board = NewBoard(out _);

        board.Update(
            AgentToken.Codex,
            [new("300", 150, null), new("10080", -5, null), new("43200", 12.5, null), new("60", double.NaN, null), new("120", double.PositiveInfinity, null)],
            null,
            start);

        var byWindow = board.Snapshot().ToDictionary(l => l.Window, l => l.Pct);
        Assert.Equal(100, byWindow["300"]);
        Assert.Equal(0, byWindow["10080"]);
        Assert.Equal(13, byWindow["43200"]);
        Assert.Equal(100, byWindow["120"]);
        Assert.False(byWindow.ContainsKey("60"));
    }

    [Fact]
    public void ResetTimesReadAsIsoDateOrEpochSeconds()
    {
        var board = NewBoard(out _);

        board.Update(
            AgentToken.Claude,
            [new("five_hour", 1, "2026-11-19T15:00:00Z"), new("seven_day", 1, "1787897318"), new("spend_limit", 1, "garbage"), new("x", 1, null)],
            null,
            start);

        var byWindow = board.Snapshot().ToDictionary(l => l.Window, l => l.ResetAtMin);
        Assert.Equal(DateTimeOffset.Parse("2026-11-19T15:00:00Z", System.Globalization.CultureInfo.InvariantCulture).ToUnixTimeSeconds() / 60, byWindow["300"]);
        Assert.Equal(29_798_288, byWindow["10080"]);
        Assert.Equal(-1, byWindow["~spend_limit"]);
        Assert.Equal(-1, byWindow["~x"]);
    }

    [Fact]
    public void TheContextIsItsOwnWindowAndSortsLast()
    {
        var board = NewBoard(out _);

        board.Update(AgentToken.Claude, [new("seven_day", 10, null), new("five_hour", 20, null), new("zzz", 1, null)], 63.4, start);

        Assert.Equal(["300", "10080", "~zzz", "ctx"], board.Snapshot().Select(l => l.Window).ToArray());
        Assert.Equal(63, board.Snapshot()[^1].Pct);
        Assert.Equal(-1, board.Snapshot()[^1].ResetAtMin);
    }

    [Fact]
    public void ClaudeSortsBeforeCodex()
    {
        var board = NewBoard(out _);

        board.Update(AgentToken.Codex, [new("60", 1, null)], null, start);
        board.Update(AgentToken.Claude, [new("five_hour", 1, null)], null, start);

        Assert.Equal([AgentToken.Claude, AgentToken.Codex], board.Snapshot().Select(l => l.Agent).ToArray());
    }

    [Fact]
    public void AWindowAbsentFromTheReportStaysForTwentyFourHoursThenGoes()
    {
        var board = NewBoard(out _);
        board.Update(AgentToken.Claude, [new("five_hour", 10, null), new("seven_day", 20, null)], null, start);

        board.Update(AgentToken.Claude, [new("seven_day", 21, null)], null, start + TimeSpan.FromHours(23));
        Assert.Equal(2, board.Snapshot().Count);

        board.Update(AgentToken.Claude, [new("seven_day", 22, null)], null, start + TimeSpan.FromHours(25));
        Assert.Equal(["10080"], board.Snapshot().Select(l => l.Window).ToArray());
    }

    [Fact]
    public void AnotherAgentsReportDoesNotExpireThisOnesWindows()
    {
        var board = NewBoard(out _);
        board.Update(AgentToken.Claude, [new("five_hour", 10, null)], null, start);

        board.Update(AgentToken.Codex, [new("60", 1, null)], null, start + TimeSpan.FromDays(3));

        Assert.Equal(2, board.Snapshot().Count);
    }

    [Fact]
    public void ChangedFiresOnARealChangeNotOnAFresherMeasurement()
    {
        var board = NewBoard(out _);
        var raised = 0;
        board.Changed += () => raised++;

        board.Update(AgentToken.Claude, [new("five_hour", 10, null)], null, start);
        board.Update(AgentToken.Claude, [new("five_hour", 10, null)], null, start + TimeSpan.FromMinutes(5));
        Assert.Equal(1, raised);
        Assert.Equal((start + TimeSpan.FromMinutes(5)).ToUnixTimeSeconds() / 60, board.Snapshot().Single().MeasuredAtMin);

        board.Update(AgentToken.Claude, [new("five_hour", 11, null)], null, start + TimeSpan.FromMinutes(6));
        Assert.Equal(2, raised);
    }

    [Fact]
    public void AnUnknownAgentIsIgnored()
    {
        var board = NewBoard(out _);

        board.Update("grok", [new("300", 1, null)], null, start);

        Assert.Empty(board.Snapshot());
    }

    [Fact]
    public void LimitsSpendAndLastHeardSurviveARestart()
    {
        var limits = NewBoard(out var store);
        var spend = new SpendBoard(store);
        limits.Update(AgentToken.Claude, [new("five_hour", 42, "1787897318")], 7, start);
        spend.Update(AgentToken.Codex, new SpendTotals(1, 2, 3, 4, 5, 6, Unpriced: true, TokAll: 7, CentsAll: 8), start);
        store.Touch(AgentToken.Codex, start);

        var again = new UsageStore(UsagePath, start);

        Assert.Equal(limits.Snapshot(), new LimitsBoard(again).Snapshot());
        Assert.Equal(spend.Snapshot(), new SpendBoard(again).Snapshot());
        Assert.Equal(8, Assert.Single(new SpendBoard(again).Snapshot()).CentsAll);
        Assert.True(Assert.Single(new SpendBoard(again).Snapshot()).Unpriced);
        Assert.Equal(start.ToUnixTimeSeconds() / 60, again.LastHeard(AgentToken.Codex));
        Assert.Equal(-1, again.LastHeard(AgentToken.Claude));
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{\"v\":2,\"limits\":[{\"a\":\"claude\",\"w\":\"300\",\"p\":1,\"r\":1,\"m\":99999999}]}")]
    [InlineData("{\"v\":1,\"limits\":[{\"a\":\"claude\",\"w\":\"5h\",\"p\":1,\"r\":1,\"m\":99999999}],\"spend\":[{\"a\":\"x\"}],\"heard\":{\"claude\":\"soon\"}}")]
    public void ADamagedFileIsAnEmptyStore(string content)
    {
        File.WriteAllText(UsagePath, content);

        var board = NewBoard(out var store);

        Assert.Empty(board.Snapshot());
        Assert.Empty(new SpendBoard(store).Snapshot());
        Assert.Equal(-1, store.LastHeard(AgentToken.Claude));
    }

    [Fact]
    public void EntriesOlderThanThirtyDaysAreDroppedOnLoad()
    {
        var board = NewBoard(out var store);
        board.Update(AgentToken.Claude, [new("five_hour", 10, null)], null, start);
        new SpendBoard(store).Update(AgentToken.Claude, new SpendTotals(1, 1, 1, 1, 1, 1), start);
        store.Touch(AgentToken.Claude, start);

        var young = new UsageStore(UsagePath, start + TimeSpan.FromDays(29));
        Assert.Single(new LimitsBoard(young).Snapshot());
        Assert.Single(new SpendBoard(young).Snapshot());
        Assert.NotEqual(-1, young.LastHeard(AgentToken.Claude));

        var old = new UsageStore(UsagePath, start + TimeSpan.FromDays(31));
        Assert.Empty(new LimitsBoard(old).Snapshot());
        Assert.Empty(new SpendBoard(old).Snapshot());
        Assert.Equal(-1, old.LastHeard(AgentToken.Claude));
    }

    [Fact]
    public void TheFileHoldsNumbersAndWindowNamesOnly()
    {
        var board = NewBoard(out var store);
        board.Update(AgentToken.Claude, [new("five_hour", 10, null)], null, start);
        store.Touch(AgentToken.Claude, start);

        var text = File.ReadAllText(UsagePath);

        Assert.Contains("\"w\":\"300\"", text);
        Assert.DoesNotContain("cwd", text);
        Assert.DoesNotContain("token", text);
    }

    [Fact]
    public void AFailedWriteKeepsMemoryAndReportsFalse()
    {
        Directory.CreateDirectory(UsagePath + ".tmp");
        var board = NewBoard(out var store);

        board.Update(AgentToken.Claude, [new("five_hour", 10, null)], null, start);

        Assert.False(store.Save());
        Assert.Single(board.Snapshot());
    }

    [Fact]
    public async Task FourThreadsNeverCorruptTheStore()
    {
        var board = NewBoard(out var store);
        var spend = new SpendBoard(store);

        await Task.WhenAll(Enumerable.Range(0, 4).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                board.Update(AgentToken.Claude, [new("five_hour", i, null)], null, start);
                spend.Update(AgentToken.Codex, new SpendTotals(i, i, i, i, i, i), start);
                store.Touch(AgentToken.Codex, start + TimeSpan.FromMinutes(i));
                _ = board.Snapshot();
            }
        }, TestContext.Current.CancellationToken)));

        var again = new UsageStore(UsagePath, start + TimeSpan.FromMinutes(100));
        Assert.Single(new LimitsBoard(again).Snapshot());
        Assert.Single(new SpendBoard(again).Snapshot());
    }
}

public sealed class SpendBoardTests : IDisposable
{
    private readonly DirectoryInfo dir = Directory.CreateTempSubdirectory("edgepad-spend");
    private readonly DateTimeOffset start = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    public void Dispose() => dir.Delete(recursive: true);

    private SpendBoard NewBoard() => new(new UsageStore(Path.Combine(dir.FullName, UsageStore.FileName), start));

    [Fact]
    public void TotalsAreStoredAndReadBack()
    {
        var board = NewBoard();

        board.Update(AgentToken.Codex, new SpendTotals(10, 20, 30, 40, 50, 60), start);
        board.Update(AgentToken.Claude, new SpendTotals(1, 2, 3, 4, 5, 6), start);

        Assert.Equal(
            [
                new SpendEntry(AgentToken.Claude, start.ToUnixTimeSeconds() / 60, 1, 2, 3, 4, 5, 6),
                new SpendEntry(AgentToken.Codex, start.ToUnixTimeSeconds() / 60, 10, 20, 30, 40, 50, 60),
            ],
            board.Snapshot().ToArray());
    }

    [Fact]
    public void NegativesAndHugeNumbersAreClamped()
    {
        var board = NewBoard();

        board.Update(AgentToken.Claude, new SpendTotals(-1, -2, long.MaxValue, long.MaxValue, 5, 6), start);

        var entry = Assert.Single(board.Snapshot());
        Assert.Equal(0, entry.TokToday);
        Assert.Equal(0, entry.CentsToday);
        Assert.Equal(SpendBoard.MaxTokens, entry.TokWeek);
        Assert.Equal(SpendBoard.MaxCents, entry.CentsWeek);
    }

    [Fact]
    public void ChangedFiresOnlyWhenATotalChanges()
    {
        var board = NewBoard();
        var raised = 0;
        board.Changed += () => raised++;

        board.Update(AgentToken.Claude, new SpendTotals(1, 1, 1, 1, 1, 1), start);
        board.Update(AgentToken.Claude, new SpendTotals(1, 1, 1, 1, 1, 1), start + TimeSpan.FromMinutes(3));
        Assert.Equal(1, raised);

        board.Update(AgentToken.Claude, new SpendTotals(2, 1, 1, 1, 1, 1), start + TimeSpan.FromMinutes(4));
        Assert.Equal(2, raised);
    }

    [Fact]
    public void AnUnknownAgentIsIgnored()
    {
        var board = NewBoard();

        board.Update("grok", new SpendTotals(1, 1, 1, 1, 1, 1), start);

        Assert.Empty(board.Snapshot());
    }
}
