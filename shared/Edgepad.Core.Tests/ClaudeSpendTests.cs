using System.Globalization;
using System.Text;
using System.Text.Json;
using Edgepad.Dashboard;
using Edgepad.Protocol;
using Xunit;

namespace Edgepad.Tests;

public sealed class ClaudeSpendTests : IDisposable
{
    private const string Sonnet = "claude-sonnet-4-5-20250929";
    private static readonly string[] ExpectedKeys = ["after", "m1", "only-uuid"];

    // Thursday 19 November 2026, noon local: today 19th, the last 7 days from the 13th, the last 30 from 21 October.
    private readonly DateTimeOffset now = new(new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Local));
    private readonly DirectoryInfo temp = Directory.CreateTempSubdirectory("edgepad-claude");
    private readonly List<string> opened = [];
    private readonly List<Action> timers = [];
    private bool on = true;
    private bool linked = true;

    public ClaudeSpendTests()
    {
        Store = new UsageStore(Path.Combine(temp.FullName, UsageStore.FileName), now);
        Spend = new SpendBoard(Store);
    }

    private string Root => Path.Combine(temp.FullName, "claude");

    private string CachePath => Path.Combine(temp.FullName, ClaudeSpendWatcher.CacheName);

    private UsageStore Store { get; }

    private SpendBoard Spend { get; }

    private DateTimeOffset Clock { get; set; }

    public void Dispose() => temp.Delete(recursive: true);

    private static string Line(string id, DateTime local, string model, int input, int output, string type = "assistant") =>
        "{\"type\":\"" + type + "\",\"uuid\":\"u-" + id + "\",\"timestamp\":\"" + new DateTimeOffset(local).ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)
        + "\",\"message\":{\"id\":\"" + id + "\",\"model\":\"" + model + "\",\"usage\":{\"input_tokens\":" + input.ToString(CultureInfo.InvariantCulture)
        + ",\"output_tokens\":" + output.ToString(CultureInfo.InvariantCulture) + "}}}";

    private static DateTime Day(int day) => new(2026, 11, day, 12, 0, 0, DateTimeKind.Local);

    private string Transcript(string relative, params string[] lines)
    {
        var path = Path.Combine(Root, "projects", relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
        File.SetLastWriteTimeUtc(path, now.UtcDateTime.AddMinutes(-1));
        return path;
    }

    private ClaudeSpendWatcher NewWatcher() =>
        new(
            () => on,
            () => linked,
            Spend,
            () => Clock,
            Root,
            CachePath,
            path =>
            {
                opened.Add(path);
                return File.OpenRead(path);
            },
            (action, _) =>
            {
                timers.Add(action);
                return new Stopper();
            });

    [Fact]
    public void PricesFollowPlimsollsTable()
    {
        using var usage = JsonDocument.Parse("""{"input_tokens":100000,"output_tokens":10000}""");
        Assert.Equal(0.45, ClaudePrices.Usd(Sonnet, usage.RootElement), 9);
        Assert.Equal(0.45, ClaudePrices.Usd("us.anthropic.claude-sonnet-4-5-20250929-v1:0", usage.RootElement), 9);
        Assert.Equal(0, ClaudePrices.Usd("claude-unknown-9", usage.RootElement));
        Assert.False(ClaudePrices.IsPriced("claude-unknown-9"));
        Assert.False(ClaudePrices.IsPriced(null));
        Assert.True(ClaudePrices.IsPriced("claude-opus-5-5"));

        // opus-5-5 before opus-5: 4 $/MTok in, not 5.
        using var million = JsonDocument.Parse("""{"input_tokens":1000000}""");
        Assert.Equal(4, ClaudePrices.Usd("claude-opus-5-5", million.RootElement), 9);

        // Cache: reads at 0.1 of input, 5-minute writes at 1.25, 1-hour writes at 2.
        using var cached = JsonDocument.Parse("""{"cache_read_input_tokens":1000000,"cache_creation_input_tokens":2000000,"cache_creation":{"ephemeral_1h_input_tokens":1000000}}""");
        Assert.Equal((3 * 0.1) + (3 * 1.25) + (3 * 2), ClaudePrices.Usd(Sonnet, cached.RootElement), 9);

        // Haiku 5.5: 5x for a prompt over 100,000 tokens.
        using var small = JsonDocument.Parse("""{"input_tokens":100000}""");
        using var big = JsonDocument.Parse("""{"input_tokens":100001}""");
        Assert.Equal(0.01, ClaudePrices.Usd("claude-haiku-5-5", small.RootElement), 9);
        Assert.Equal(0.0500005, ClaudePrices.Usd("claude-haiku-5-5", big.RootElement), 9);

        using var odd = JsonDocument.Parse("""{"input_tokens":"100","output_tokens":-5}""");
        Assert.Equal(0, ClaudePrices.Usd(Sonnet, odd.RootElement));
    }

    [Fact]
    public void PeriodsAreTheLast7And30DaysCountingToday()
    {
        Assert.Equal(new ClaudePeriods("2026-11-19", "2026-11-13", "2026-10-21", "2026-10-21"), ClaudeSpend.Periods(Day(19)));
        Assert.Equal(new ClaudePeriods("2026-11-16", "2026-11-10", "2026-10-18", "2026-10-18"), ClaudeSpend.Periods(Day(16)));
        Assert.Equal(new ClaudePeriods("2026-12-02", "2026-11-26", "2026-11-03", "2026-11-03"), ClaudeSpend.Periods(new DateTime(2026, 12, 2, 9, 0, 0, DateTimeKind.Local)));
    }

    [Fact]
    public void AllTimeIsReadOnceAndAVanishedTranscriptKeepsItsTotals()
    {
        var old = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Local);
        var oldPath = Transcript("p/old.jsonl", Line("m-old", old, Sonnet, 100000, 10000), Line("m-old2", old, Sonnet, 100000, 10000));
        File.SetLastWriteTimeUtc(oldPath, now.UtcDateTime.AddDays(-100));
        Transcript("p/new.jsonl", Line("m-new", Day(19), Sonnet, 100000, 10000));
        Clock = now;
        using (var first = NewWatcher())
        {
            first.SetWatch(ClaudeSpendWatcher.AiBit);
            first.Poll();
            var entry = Assert.Single(Spend.Snapshot());
            Assert.Equal(110000, entry.TokToday);
            Assert.Equal(330000, entry.TokAll);
            Assert.Equal(135, entry.CentsAll);
        }

        // The old transcript was folded to day totals: its ids are not in the cache any more.
        Assert.DoesNotContain("m-old", File.ReadAllText(CachePath), StringComparison.Ordinal);

        // A restart reads nothing again, and neither does a transcript Claude Code deleted: its totals stay.
        File.Delete(oldPath);
        opened.Clear();
        using var second = NewWatcher();
        second.SetWatch(ClaudeSpendWatcher.AiBit);
        second.Poll();
        Assert.Empty(opened);
        Assert.Equal(330000, Assert.Single(Spend.Snapshot()).TokAll);

        // Only a new or changed file is read, and all time grows by it.
        Transcript("p/later.jsonl", Line("m-later", Day(19), Sonnet, 100000, 10000));
        Clock = now.AddMinutes(2);
        second.Poll();
        Assert.Single(opened);
        Assert.Equal(440000, Assert.Single(Spend.Snapshot()).TokAll);
    }

    [Fact]
    public void AllTimeIsNotAnnouncedAsAPartialSumWhileOldHistoryIsStillBeingRead()
    {
        var old = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Local);
        var first = Transcript("p/a.jsonl", Line("m-big", old, Sonnet, 1, 1));
        using (var stream = File.OpenWrite(first))
        {
            stream.SetLength(ClaudeSpendWatcher.BytesPerTick + 1); // one tick's whole budget
        }

        File.SetLastWriteTimeUtc(first, now.UtcDateTime.AddDays(-100));
        var second = Transcript("p/b.jsonl", Line("m-b", old, Sonnet, 5, 5));
        File.SetLastWriteTimeUtc(second, now.UtcDateTime.AddDays(-101));
        Transcript("p/new.jsonl", Line("m-new", Day(19), Sonnet, 100000, 10000));
        Clock = now;
        using var watcher = NewWatcher();
        watcher.SetWatch(ClaudeSpendWatcher.AiBit);

        watcher.Poll();
        var partial = Assert.Single(Spend.Snapshot());
        Assert.Equal(110000, partial.TokToday);
        Assert.Equal(-1, partial.TokAll);

        Clock = now.AddSeconds(10);
        watcher.Poll();
        Assert.Equal(110012, Assert.Single(Spend.Snapshot()).TokAll);
    }

    [Fact]
    public void AllTimeKeepsItsLastKnownValueWhenALaterScanRunsOutOfBudget()
    {
        Transcript("p/new.jsonl", Line("m-new", Day(19), Sonnet, 100000, 10000));
        Clock = now;
        using var watcher = NewWatcher();
        watcher.SetWatch(ClaudeSpendWatcher.AiBit);
        watcher.Poll();
        var known = Assert.Single(Spend.Snapshot()).TokAll;
        Assert.Equal(110000, known);

        var big = Transcript("p/big.jsonl", Line("m-big", Day(19), Sonnet, 1, 1));
        using (var stream = File.OpenWrite(big))
        {
            stream.SetLength(ClaudeSpendWatcher.BytesPerTick + 1); // the whole budget, read first as the newest
        }

        File.SetLastWriteTimeUtc(big, now.UtcDateTime.AddMinutes(-1));
        var old = Transcript("p/old.jsonl", Line("m-old", new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Local), Sonnet, 5, 5));
        File.SetLastWriteTimeUtc(old, now.UtcDateTime.AddDays(-100));
        Clock = now.AddMinutes(2);
        watcher.Poll();

        Assert.Equal(known, Assert.Single(Spend.Snapshot()).TokAll);
    }

    [Fact]
    public void AFileKeepsTheLastRowOfEachIdAndSkipsWhatDoesNotCount()
    {
        var text = string.Join(
            "\n",
            Line("m1", Day(19), Sonnet, 1, 1),
            Line("m1", Day(19), Sonnet, 100000, 10000),
            Line("syn", Day(19), "<synthetic>", 5, 5),
            Line("user", Day(19), Sonnet, 5, 5, type: "user"),
            Line("old", new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Local), Sonnet, 5, 5),
            """{"type":"assistant","timestamp":"never","message":{"id":"bad","usage":{"input_tokens":1}}}""",
            """{"type":"assistant","timestamp":"2026-11-19T10:00:00Z","uuid":"only-uuid","message":{"model":"claude-unknown-9","usage":{"input_tokens":3,"output_tokens":4}}}""",
            "not json \"usage\"",
            """{"type":"assistant","message":{"usage":""" + "\"" + new string('x', ClaudeSpend.MaxLine) + "\"}}",
            Line("after", Day(18), Sonnet, 10, 0));

        var rows = ClaudeSpend.SumFile(new MemoryStream(Encoding.UTF8.GetBytes(text)), "2026-11-01");

        Assert.Equal(ExpectedKeys, rows.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(new ClaudeRow("2026-11-19", 110000, 0.45, true), rows["m1"] with { Usd = Math.Round(rows["m1"].Usd, 9) });
        Assert.False(rows["only-uuid"].Priced);
        Assert.Equal(7, rows["only-uuid"].Tokens);
    }

    [Fact]
    public void TheWatcherSumsTodayWeekAndMonthCountingAForkOnce()
    {
        Transcript(
            "proj-a/s1.jsonl",
            Line("m1", Day(19), Sonnet, 1, 1),
            Line("m1", Day(19), Sonnet, 100000, 10000),
            Line("m2", Day(17), Sonnet, 100000, 10000),
            Line("m3", Day(3), "claude-unknown-9", 500, 500));
        Transcript("proj-b/s2/subagents/agent-1.jsonl", Line("m1", Day(19), Sonnet, 100000, 10000));
        Clock = now;
        using var watcher = NewWatcher();
        watcher.SetWatch(ClaudeSpendWatcher.AiBit);

        watcher.Poll();

        var entry = Assert.Single(Spend.Snapshot());
        Assert.Equal(new SpendEntry(AgentToken.Claude, entry.MeasuredAtMin, 110000, 45, 220000, 90, 221000, 90, Unpriced: true, TokAll: 221000, CentsAll: 90), entry);
        Assert.True(watcher.HasUnpriced);
    }

    [Fact]
    public void NothingIsReadUnlessTheAiScreenIsWatchedAndThenOnlyOnceAMinute()
    {
        Transcript("p/s.jsonl", Line("m1", Day(19), Sonnet, 100000, 10000));
        Clock = now;
        using var watcher = NewWatcher();

        watcher.Poll();
        Assert.Empty(opened);
        Assert.Empty(Spend.Snapshot());

        watcher.SetWatch(ClaudeSpendWatcher.AiBit);
        on = false;
        watcher.Poll();
        Assert.Empty(opened);

        on = true;
        watcher.Poll();
        Assert.Single(opened);

        Clock = now.AddSeconds(30);
        watcher.Poll();
        Assert.Single(opened);

        // A minute on, the unchanged file comes from the cache.
        Clock = now.AddMinutes(2);
        watcher.Poll();
        Assert.Single(opened);
        Assert.Single(Spend.Snapshot());
    }

    [Fact]
    public void ALineHalfWrittenAtTheLastReadIsCountedOnceComplete()
    {
        var second = Line("m4", Day(19), Sonnet, 100000, 10000);
        var path = Transcript("p/s.jsonl", Line("m1", Day(19), Sonnet, 100000, 10000));
        File.AppendAllText(path, second[..20]);
        File.SetLastWriteTimeUtc(path, now.UtcDateTime.AddMinutes(-1));
        Clock = now;
        using var watcher = NewWatcher();
        watcher.SetWatch(ClaudeSpendWatcher.AiBit);
        watcher.Poll();
        Assert.Equal(45, Spend.Snapshot().Single().CentsToday);

        File.AppendAllText(path, second[20..] + "\n");
        File.SetLastWriteTimeUtc(path, now.UtcDateTime);
        Clock = now.AddMinutes(2);
        watcher.Poll();
        Assert.Equal(90, Spend.Snapshot().Single().CentsToday);
    }

    [Fact]
    public void TheCacheIsWrittenAtMostEveryFiveMinutesAndOnDispose()
    {
        var path = Transcript("p/s.jsonl", Line("m1", Day(19), Sonnet, 100000, 10000));
        Clock = now;
        var watcher = NewWatcher();
        watcher.SetWatch(ClaudeSpendWatcher.AiBit);
        watcher.Poll();
        var saved = File.ReadAllText(CachePath);

        File.AppendAllText(path, Line("m4", Day(19), Sonnet, 100000, 10000) + "\n");
        File.SetLastWriteTimeUtc(path, now.UtcDateTime);
        Clock = now.AddMinutes(2);
        watcher.Poll();
        Assert.Equal(saved, File.ReadAllText(CachePath));

        watcher.Dispose();
        Assert.NotEqual(saved, File.ReadAllText(CachePath));
    }

    [Fact]
    public void TheCacheSurvivesARestartAndAChangedFileIsReadAgain()
    {
        var path = Transcript("p/s.jsonl", Line("m1", Day(19), Sonnet, 100000, 10000));
        Clock = now;
        using (var first = NewWatcher())
        {
            first.SetWatch(ClaudeSpendWatcher.AiBit);
            first.Poll();
        }

        opened.Clear();
        using var second = NewWatcher();
        second.SetWatch(ClaudeSpendWatcher.AiBit);
        second.Poll();
        Assert.Empty(opened);
        Assert.Equal(45, Spend.Snapshot().Single().CentsToday);

        File.AppendAllText(path, Line("m4", Day(19), Sonnet, 100000, 10000) + "\n");
        File.SetLastWriteTimeUtc(path, now.UtcDateTime);
        Clock = now.AddMinutes(2);
        second.Poll();
        Assert.Single(opened);
        Assert.Equal(90, Spend.Snapshot().Single().CentsToday);
    }

    [Fact]
    public void NoTranscriptsMeansNoRowAndAGarbageCacheIsIgnored()
    {
        File.WriteAllText(CachePath, "{not json");
        Clock = now;
        using var watcher = NewWatcher();
        watcher.SetWatch(ClaudeSpendWatcher.AiBit);
        watcher.Poll();
        Assert.Empty(Spend.Snapshot());
    }

    [Fact]
    public void TheTimerExistsOnlyWhileOnAndLinked()
    {
        using var watcher = NewWatcher();
        linked = false;
        watcher.Evaluate();
        Assert.False(watcher.Running);
        linked = true;
        watcher.Evaluate();
        Assert.True(watcher.Running);
        Assert.Single(timers);
        on = false;
        watcher.Evaluate();
        Assert.False(watcher.Running);
    }

    private sealed class Stopper : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
