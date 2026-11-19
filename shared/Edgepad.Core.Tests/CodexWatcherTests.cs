using System.Globalization;
using Edgepad.Dashboard;
using Edgepad.Protocol;
using Xunit;

namespace Edgepad.Tests;

public sealed class CodexWatcherTests : IDisposable
{
    private static readonly string Fixture = RolloutText.Fixture();

    private readonly DirectoryInfo temp = Directory.CreateTempSubdirectory("edgepad-codex");
    private readonly List<string> opened = [];
    private readonly List<Action> timers = [];
    private DateTimeOffset now = new(new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Local));
    private bool on = true;
    private bool linked = true;
    private int timersStopped;

    public CodexWatcherTests()
    {
        Directory.CreateDirectory(Root);
        Store = new UsageStore(Path.Combine(temp.FullName, UsageStore.FileName), now);
        Sessions = new SessionBoard(() => now);
        Limits = new LimitsBoard(Store);
        Spend = new SpendBoard(Store);
        Watcher = NewWatcher();
    }

    private string Root => Path.Combine(temp.FullName, "codex");

    private string CachePath => Path.Combine(temp.FullName, CodexWatcher.CacheName);

    private UsageStore Store { get; }

    private SessionBoard Sessions { get; }

    private LimitsBoard Limits { get; }

    private SpendBoard Spend { get; }

    private CodexWatcher Watcher { get; }

    public void Dispose()
    {
        Watcher.Dispose();
        temp.Delete(recursive: true);
    }

    private CodexWatcher NewWatcher() =>
        new(
            () => on,
            () => linked,
            Sessions,
            Limits,
            Spend,
            () => now,
            Root,
            CachePath,
            path =>
            {
                lock (opened)
                {
                    opened.Add(path);
                }

                return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            },
            (action, _) =>
            {
                timers.Add(action);
                return new Stopper(() => timersStopped++);
            });

    private string Rollout(DateTimeOffset day, string name, string content, TimeSpan? age = null)
    {
        var local = day.ToLocalTime();
        var dir = Path.Combine(
            Root,
            "sessions",
            local.Year.ToString("D4", CultureInfo.InvariantCulture),
            local.Month.ToString("D2", CultureInfo.InvariantCulture),
            local.Day.ToString("D2", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, now.UtcDateTime - (age ?? TimeSpan.FromMinutes(1)));
        return path;
    }

    private string Today(string name, string content, TimeSpan? age = null) => Rollout(now, name, content, age);

    [Fact]
    public void NoTimerExistsUntilTheIntegrationIsOnAndAPhoneIsLinked()
    {
        on = false;
        linked = false;
        Watcher.Evaluate();
        on = true;
        Watcher.Evaluate();
        Assert.False(Watcher.Running);
        Assert.Empty(timers);

        linked = true;
        Watcher.Evaluate();
        Assert.True(Watcher.Running);
        Assert.Single(timers);

        Watcher.Evaluate();
        Assert.Single(timers);

        linked = false;
        Watcher.Evaluate();
        Assert.False(Watcher.Running);
        Assert.Equal(1, timersStopped);
    }

    [Fact]
    public void PollingDoesNothingWhenOffOrUnlinked()
    {
        Today("rollout-a.jsonl", RolloutText.Join(RolloutText.Meta, RolloutText.Started));
        Watcher.SetWatch(CodexWatcher.AiBit);

        on = false;
        Watcher.Poll();
        on = true;
        linked = false;
        Watcher.Poll();

        Assert.Empty(opened);
        Assert.Empty(Sessions.Snapshot());
        Assert.Empty(Limits.Snapshot());
    }

    [Fact]
    public void AWorkingRolloutShowsAsABusySessionThenIdle()
    {
        var path = Today("rollout-2026-11-19T11-00-00-aaa.jsonl", RolloutText.Join(RolloutText.Meta, RolloutText.Started));

        Watcher.Poll();

        var session = Assert.Single(Sessions.Snapshot());
        Assert.Equal((AgentToken.Codex, SessionStateToken.Busy, "demo"), (session.Agent, session.State, session.Title));

        File.AppendAllText(path, RolloutText.Complete + "\n");
        File.SetLastWriteTimeUtc(path, now.UtcDateTime);
        Watcher.Poll();

        Assert.Equal(SessionStateToken.Idle, Assert.Single(Sessions.Snapshot()).State);
    }

    [Fact]
    public void AnUnchangedFileIsNotReadAgain()
    {
        Today("rollout-a.jsonl", RolloutText.Join(RolloutText.Meta, RolloutText.Started));
        Watcher.Poll();
        var reads = opened.Count;

        Watcher.Poll();
        Watcher.Poll();

        Assert.Equal(reads, opened.Count);
    }

    [Fact]
    public void ABusyFileUntouchedForTenMinutesIsIdle()
    {
        Today("rollout-a.jsonl", RolloutText.Join(RolloutText.Meta, RolloutText.Started));
        Watcher.Poll();
        Assert.Equal(SessionStateToken.Busy, Assert.Single(Sessions.Snapshot()).State);

        now += CodexWatcher.Fresh + TimeSpan.FromMinutes(1);
        Watcher.Poll();

        Assert.Equal(SessionStateToken.Idle, Assert.Single(Sessions.Snapshot()).State);
    }

    [Fact]
    public void AFileOlderThanTenMinutesNeverStartsASession()
    {
        Today("rollout-a.jsonl", RolloutText.Join(RolloutText.Meta, RolloutText.Started), TimeSpan.FromMinutes(30));

        Watcher.Poll();

        Assert.Empty(Sessions.Snapshot());
        Assert.Empty(opened);
    }

    [Fact]
    public void AtMostFourFilesAreLookedAt()
    {
        for (var i = 0; i < 7; i++)
        {
            Today($"rollout-{i}.jsonl", RolloutText.Join(RolloutText.Meta, RolloutText.Started), TimeSpan.FromMinutes(1 + i));
        }

        Watcher.Poll();

        Assert.Equal(4, Sessions.Snapshot().Count);
        Assert.Equal(4, opened.Count);
    }

    [Fact]
    public void YesterdaysFolderIsLookedAtToo()
    {
        Rollout(now - TimeSpan.FromDays(1), "rollout-y.jsonl", RolloutText.Join(RolloutText.Meta, RolloutText.Started));

        Watcher.Poll();

        Assert.Single(Sessions.Snapshot());
    }

    [Fact]
    public void ImportFilesAreIgnoredForStateLimitsAndSpend()
    {
        Today("rollout-2026-08-10T00-00-00-external-import-turn-3.jsonl", Fixture + RolloutText.Started + "\n");
        Watcher.SetWatch(CodexWatcher.AiBit);

        Watcher.Poll();

        Assert.Empty(Sessions.Snapshot());
        Assert.Empty(Limits.Snapshot());
        Assert.Empty(Spend.Snapshot());
        Assert.Empty(opened);
    }

    [Fact]
    public void LimitsAreReadOnlyWhileThePhoneDrawsThem()
    {
        Today("rollout-a.jsonl", Fixture);

        Watcher.Poll();
        Assert.Empty(Limits.Snapshot());
        Assert.Empty(Spend.Snapshot());

        Watcher.SetWatch(CodexWatcher.AiBit);
        Watcher.Poll();

        var limit = Assert.Single(Limits.Snapshot());
        Assert.Equal((AgentToken.Codex, "43200", 12d, 1787897318L / 60), (limit.Agent, limit.Window, limit.Pct, limit.ResetAtMin));
    }

    [Fact]
    public void TheFirstRecentFileWithALimitProvidesIt()
    {
        Today("rollout-2026-11-19T11-30-00-new.jsonl", RolloutText.Join(RolloutText.Meta, RolloutText.Count("null")));
        Today("rollout-2026-11-19T10-00-00-old.jsonl", RolloutText.Join(RolloutText.Meta, RolloutText.Count(RolloutText.Limits("codex", "33", 10080, 1787897318))), TimeSpan.FromHours(2));
        Watcher.SetWatch(CodexWatcher.AiBit);

        Watcher.Poll();

        var limit = Assert.Single(Limits.Snapshot());
        Assert.Equal(("10080", 33d), (limit.Window, limit.Pct));
    }

    [Fact]
    public void UsageIsRefreshedAgainOnlyEveryTwelfthPollOrWhenTheBitIsGained()
    {
        Today("rollout-a.jsonl", Fixture);
        Watcher.SetWatch(CodexWatcher.AiBit);
        Watcher.Poll();
        var first = Assert.Single(Spend.Snapshot()).MeasuredAtMin;

        now += TimeSpan.FromMinutes(5);
        Watcher.Poll();
        Assert.Equal(first, Assert.Single(Spend.Snapshot()).MeasuredAtMin);

        for (var i = 0; i < 12; i++)
        {
            Watcher.Poll();
        }

        Assert.NotEqual(first, Assert.Single(Spend.Snapshot()).MeasuredAtMin);
    }

    [Fact]
    public void SpendBucketsFilesByTheirFolderDate()
    {
        Today("rollout-today.jsonl", Fixture, TimeSpan.FromHours(3));
        Rollout(now - TimeSpan.FromDays(1), "rollout-yesterday.jsonl", Fixture, TimeSpan.FromHours(3));
        Rollout(new DateTimeOffset(new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Local)), "rollout-first.jsonl", Fixture, TimeSpan.FromHours(3));
        Rollout(new DateTimeOffset(new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Local)), "rollout-lastmonth.jsonl", Fixture, TimeSpan.FromHours(3));
        Watcher.SetWatch(CodexWatcher.AiBit);

        Watcher.Poll();

        var spend = Assert.Single(Spend.Snapshot());
        Assert.Equal(AgentToken.Codex, spend.Agent);
        Assert.Equal((47898L, 3L), (spend.TokToday, spend.CentsToday));
        Assert.Equal((2 * 47898L, 6L), (spend.TokWeek, spend.CentsWeek));
        Assert.Equal((3 * 47898L, 9L), (spend.TokMonth, spend.CentsMonth));
        Assert.False(Watcher.HasUnpriced);
    }

    [Fact]
    public void AModelWithNoPriceCountsTokensNotCentsAndIsReported()
    {
        Today("rollout-a.jsonl", RolloutText.Join(RolloutText.Context("gpt-5.2-codex"), RolloutText.Count("null", 1000, 200, 100)), TimeSpan.FromHours(3));
        Watcher.SetWatch(CodexWatcher.AiBit);

        Watcher.Poll();

        var spend = Assert.Single(Spend.Snapshot());
        Assert.Equal((900L, 0L), (spend.TokToday, spend.CentsToday));
        Assert.True(Watcher.HasUnpriced);
    }

    [Fact]
    public void SpendIsCachedPerFileBySizeAndModificationTime()
    {
        var path = Today("rollout-a.jsonl", Fixture, TimeSpan.FromHours(3));
        Watcher.SetWatch(CodexWatcher.AiBit);
        Watcher.Poll();
        Assert.NotEmpty(opened);

        opened.Clear();
        Watcher.SetWatch(0);
        Watcher.SetWatch(CodexWatcher.AiBit);
        Watcher.Poll();
        Assert.Empty(opened);

        File.AppendAllText(path, RolloutText.Count("null", 600000, 461056, 8000) + "\n");
        File.SetLastWriteTimeUtc(path, now.UtcDateTime - TimeSpan.FromHours(2));
        Watcher.SetWatch(0);
        Watcher.SetWatch(CodexWatcher.AiBit);
        Watcher.Poll();

        Assert.NotEmpty(opened);
        Assert.All(opened, p => Assert.Equal(path, p));
        Assert.Equal(600000 - 461056 + 8000, Assert.Single(Spend.Snapshot()).TokToday);
    }

    [Fact]
    public void TheCacheSurvivesARestart()
    {
        var path = Today("rollout-a.jsonl", Fixture, TimeSpan.FromHours(3));
        Watcher.SetWatch(CodexWatcher.AiBit);
        Watcher.Poll();
        Assert.True(File.Exists(CachePath));

        // Same size and time, different content: only the cache can still say 47898.
        var stamp = File.GetLastWriteTimeUtc(path);
        File.WriteAllBytes(path, new byte[new FileInfo(path).Length]);
        File.SetLastWriteTimeUtc(path, stamp);
        var again = NewWatcher();
        again.SetWatch(CodexWatcher.AiBit);
        again.Poll();

        Assert.Equal(47898, Assert.Single(Spend.Snapshot()).TokToday);
    }

    [Fact]
    public void ADamagedCacheIsRebuilt()
    {
        File.WriteAllText(CachePath, "{{{ not json");
        Today("rollout-a.jsonl", Fixture, TimeSpan.FromHours(3));

        var again = NewWatcher();
        again.SetWatch(CodexWatcher.AiBit);
        again.Poll();

        Assert.Equal(47898, Assert.Single(Spend.Snapshot()).TokToday);
    }

    [Fact]
    public void OnlyRolloutFilesUnderSessionsAreEverOpened()
    {
        File.WriteAllText(Path.Combine(Root, "auth.json"), "{\"token\":\"secret\"}");
        File.WriteAllText(Path.Combine(Root, "config.toml"), "x = 1");
        File.WriteAllText(Path.Combine(Root, "state_5.sqlite"), "x");
        Directory.CreateDirectory(Path.Combine(Root, "sessions"));
        File.WriteAllText(Path.Combine(Root, "sessions", "auth.json"), "x");
        Today("rollout-a.jsonl", Fixture);
        Today("notes.jsonl", Fixture);
        Today("rollout-b.txt", Fixture);
        Watcher.SetWatch(CodexWatcher.AiBit);

        Watcher.Poll();
        Watcher.SetWatch(0);
        Watcher.SetWatch(CodexWatcher.AiBit);
        Watcher.Poll();

        Assert.NotEmpty(opened);
        var sessions = Path.Combine(Root, "sessions") + Path.DirectorySeparatorChar;
        Assert.All(opened, path =>
        {
            Assert.StartsWith(sessions, path, StringComparison.Ordinal);
            Assert.StartsWith("rollout-", Path.GetFileName(path), StringComparison.Ordinal);
            Assert.EndsWith(".jsonl", path, StringComparison.Ordinal);
        });
        Assert.DoesNotContain(opened, p => p.Contains("auth.json", StringComparison.Ordinal));
    }

    [Fact]
    public void AMissingCodexFolderIsQuietlyNothing()
    {
        Directory.Delete(Root, recursive: true);
        Watcher.SetWatch(CodexWatcher.AiBit);

        Watcher.Poll();

        Assert.Empty(Sessions.Snapshot());
        Assert.Empty(Limits.Snapshot());
        Assert.Empty(Spend.Snapshot());
    }

    [Fact]
    public void TheDefaultRootIsCodexHomeOrDotCodex()
    {
        var root = CodexWatcher.DefaultRoot();

        Assert.False(string.IsNullOrEmpty(root));
        Assert.True(Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } home ? root == home : root.EndsWith(".codex", StringComparison.Ordinal));
    }

    private sealed class Stopper(Action stopped) : IDisposable
    {
        public void Dispose() => stopped();
    }
}
