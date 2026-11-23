using System.Globalization;
using System.Text.Json;
using Edgepad.Protocol;
using Edgepad.Trust;

namespace Edgepad.Dashboard;

/// <summary>
/// Reads Codex's own session files for the dashboard: which sessions are working, the usage limits and the spend. It exists
/// (has a timer) only while the Codex integration is on AND a phone is linked; <see cref="Evaluate"/> decides that and the app
/// calls it when either changes. The only files ever opened are <c>sessions/YYYY/MM/DD/rollout-*.jsonl</c> under the Codex folder,
/// regular files only, never <c>auth.json</c>, the databases or the config, and nothing from a rollout leaves this class except
/// a state, percentages, token counts and the last folder name of a working directory.
/// <para>
/// Every 5 seconds it looks at the files of today and yesterday that changed in the last 6 hours (at most 8) and tells the
/// <see cref="SessionBoard"/> busy (written in the last 10 minutes, task open) or idle. While the phone draws the AI widget or screen (DASH_WATCH bit 3) it also refreshes the
/// limits and the spend once a minute; spend is cached per file by size and modification time, so an unchanged month costs stats only.
/// </para>
/// </summary>
internal sealed class CodexWatcher : IDisposable
{
    public const int AiBit = 8;
    public static readonly TimeSpan Every = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan Fresh = TimeSpan.FromMinutes(10);

    /// <summary>A rollout written within this is a session on the board (idle once it was <see cref="Fresh"/> ago): the board's own expiry.</summary>
    public static readonly TimeSpan Window = SessionBoard.Expiry;

    /// <summary>A rollout written this recently with no task marker in its tail is taken as working.</summary>
    public static readonly TimeSpan JustWritten = TimeSpan.FromSeconds(30);
    public const string CacheName = "dashboard-codex-cache.json";

    private const int UsageEvery = 12;
    private const int MaxFiles = SessionBoard.Capacity;
    private const int LimitFiles = 5;
    private const int MaxTails = 32;
    private const int CacheVersion = 1;

    private readonly Func<bool> settingsOn;
    private readonly Func<bool> phoneLinked;
    private readonly SessionBoard sessions;
    private readonly LimitsBoard limits;
    private readonly SpendBoard spend;
    private readonly Func<DateTimeOffset> clock;
    private readonly string root;
    private readonly string sessionsDir;
    private readonly string cachePath;
    private readonly Func<string, Stream> open;
    private readonly Func<Action, TimeSpan, IDisposable> startTimer;
    private readonly Lock gate = new();
    private readonly Lock pollGate = new();
    private readonly Dictionary<string, Tracked> tracked = [];
    private readonly Dictionary<string, (long Size, long Ticks, RolloutTail Tail)> tails = [];
    private readonly Dictionary<string, CacheEntry> cache = [];
    private IDisposable? timer;
    private int watch;
    private int usageIn;
    private bool usageDue;
    private bool cacheDirty;
    private bool logged;
    private volatile bool unpriced;

    /// <param name="root">The Codex folder, <see cref="DefaultRoot"/> in the app.</param>
    /// <param name="cachePath">Where the per-file spend cache lives; numbers and model ids only.</param>
    /// <param name="open">Opens a file for reading. Only ever called with a rollout path. Tests spy on it.</param>
    /// <param name="startTimer">Runs the action every interval on another thread until disposed.</param>
    public CodexWatcher(
        Func<bool> settingsOn,
        Func<bool> phoneLinked,
        SessionBoard sessions,
        LimitsBoard limits,
        SpendBoard spend,
        Func<DateTimeOffset> clock,
        string root,
        string cachePath,
        Func<string, Stream>? open = null,
        Func<Action, TimeSpan, IDisposable>? startTimer = null)
    {
        this.settingsOn = settingsOn;
        this.phoneLinked = phoneLinked;
        this.sessions = sessions;
        this.limits = limits;
        this.spend = spend;
        this.clock = clock;
        this.root = root;
        sessionsDir = Path.Combine(root, "sessions");
        this.cachePath = cachePath;
        this.open = open ?? (path => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan));
        this.startTimer = startTimer ?? ((action, every) => new Timer(_ => action(), null, TimeSpan.Zero, every));
        LoadCache();
    }

    /// <summary>True while the timer exists.</summary>
    internal bool Running
    {
        get
        {
            lock (gate)
            {
                return timer is not null;
            }
        }
    }

    /// <summary>True when the last spend refresh met a model with no price, so its cents are a lower bound.</summary>
    public bool HasUnpriced => unpriced;

    /// <summary><c>CODEX_HOME</c>, else <c>.codex</c> in the user's home.</summary>
    public static string DefaultRoot() =>
        Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } home
            ? home
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");

    /// <summary>Starts or stops the timer to match "integration on and a phone linked". Call it whenever either changes.</summary>
    public void Evaluate()
    {
        lock (gate)
        {
            var wanted = settingsOn() && phoneLinked();
            if (wanted && timer is null)
            {
                timer = startTimer(Poll, Every);
            }
            else if (!wanted && timer is not null)
            {
                timer.Dispose();
                timer = null;
            }
        }
    }

    /// <summary>The phone's DASH_WATCH. Gaining bit 3 refreshes limits and spend on the next poll.</summary>
    public void SetWatch(int mask)
    {
        lock (gate)
        {
            if ((watch & AiBit) == 0 && (mask & AiBit) != 0)
            {
                usageDue = true;
            }

            watch = mask;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            timer?.Dispose();
            timer = null;
        }
    }

    /// <summary>One look at the files. Public to the timer and to tests; never throws, never overlaps itself.</summary>
    internal void Poll()
    {
        if (!pollGate.TryEnter())
        {
            return;
        }

        try
        {
            if (!settingsOn() || !phoneLinked())
            {
                return;
            }

            ScanSessions();
            var due = false;
            lock (gate)
            {
                if ((watch & AiBit) != 0 && (usageDue || --usageIn <= 0))
                {
                    usageIn = UsageEvery;
                    usageDue = false;
                    due = true;
                }
            }

            if (due)
            {
                RefreshLimits();
                RefreshSpend();
            }
        }
        catch (Exception e)
        {
            // A timer thread must not end the process: whatever a file or the disk does, the next poll starts afresh.
            if (!logged)
            {
                logged = true;
                Log.Write($"codex watcher: {e.GetType().Name}");
            }
        }
        finally
        {
            pollGate.Exit();
        }
    }

    // ---- sessions

    private void ScanSessions()
    {
        var nowUtc = clock().UtcDateTime;
        var today = Today();
        var recent = Rollouts(DayDir(today))
            .Concat(Rollouts(DayDir(today.AddDays(-1))))
            .Where(f => nowUtc - f.LastWriteTimeUtc <= Window)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Take(MaxFiles)
            .ToList();
        var seen = new HashSet<string>();
        foreach (var file in recent)
        {
            seen.Add(file.FullName);
            var size = file.Length;
            var ticks = file.LastWriteTimeUtc.Ticks;
            var stale = nowUtc - file.LastWriteTimeUtc > Fresh;
            if (tracked.TryGetValue(file.FullName, out var before) && before.Size == size && before.Ticks == ticks)
            {
                // Untouched for ten minutes: whatever it said last, it is not working now.
                if (stale && before.State == SessionStateToken.Busy)
                {
                    sessions.Apply(new HookEvent(AgentToken.Codex, before.Id, SessionEvent.Stop, string.Empty));
                    tracked[file.FullName] = before with { State = SessionStateToken.Idle };
                }

                continue;
            }

            if (TailOf(file) is not { } tail)
            {
                continue;
            }

            // A long turn can push every task marker out of the tail: keep what was known, and a file still being written is
            // working. Never idle by default.
            var state = tail.State
                ?? before?.State
                ?? (nowUtc - file.LastWriteTimeUtc <= JustWritten ? SessionStateToken.Busy : null);
            if (state is null)
            {
                continue;
            }

            if (stale && state == SessionStateToken.Busy)
            {
                state = SessionStateToken.Idle;
            }

            var id = Path.GetFileNameWithoutExtension(file.Name);
            if (tail.State is not null || before is null || state != before.State)
            {
                sessions.Apply(new HookEvent(AgentToken.Codex, id, state == SessionStateToken.Busy ? SessionEvent.Prompt : SessionEvent.Stop, tail.Cwd));
            }

            if (tail.ContextPct >= 0)
            {
                sessions.SetContextById(AgentToken.Codex, id, tail.ContextPct);
            }

            tracked[file.FullName] = new Tracked(size, ticks, state, id);
        }

        foreach (var (path, old) in tracked.ToList())
        {
            if (seen.Contains(path))
            {
                continue;
            }

            // Untouched for ten minutes: whatever it said last, it is not working now.
            if (old.State == SessionStateToken.Busy)
            {
                sessions.Apply(new HookEvent(AgentToken.Codex, old.Id, SessionEvent.Stop, string.Empty));
            }

            tracked.Remove(path);
        }
    }

    // ---- limits

    private void RefreshLimits()
    {
        foreach (var file in NewestFirst().Take(LimitFiles))
        {
            if (TailOf(file) is { Limits.Count: > 0 } tail)
            {
                limits.Update(
                    AgentToken.Codex,
                    tail.Limits.Select(w => new RawLimit(
                        w.Minutes.ToString(CultureInfo.InvariantCulture),
                        w.Pct,
                        w.ResetsAtSec >= 0 ? w.ResetsAtSec.ToString(CultureInfo.InvariantCulture) : null)),
                    null,
                    MeasuredAt(file));
                return;
            }
        }
    }

    // ---- spend

    private void RefreshSpend()
    {
        var today = Today();
        var start7 = today.AddDays(-6);
        var start30 = today.AddDays(-29);
        long tokDay = 0, centsDay = 0, tokWeek = 0, centsWeek = 0, tokMonth = 0, centsMonth = 0;
        var anyUnpriced = false;
        var live = new HashSet<string>();
        for (var day = start30; day <= today; day = day.AddDays(1))
        {
            foreach (var file in Rollouts(DayDir(day)))
            {
                var totals = TotalsOf(file, live);
                if (totals.Input == 0 && totals.Output == 0)
                {
                    continue;
                }

                var cents = 0L;
                if (OpenAiPrices.TryGet(totals.Model, out var price))
                {
                    cents = OpenAiPrices.Cents(price, totals);
                }
                else
                {
                    anyUnpriced = true;
                }

                tokMonth += totals.Tokens;
                centsMonth += cents;
                if (day >= start7)
                {
                    tokWeek += totals.Tokens;
                    centsWeek += cents;
                }

                if (day == today)
                {
                    tokDay += totals.Tokens;
                    centsDay += cents;
                }
            }
        }

        foreach (var gone in cache.Keys.Where(k => !live.Contains(k)).ToList())
        {
            cache.Remove(gone);
            cacheDirty = true;
        }

        if (cacheDirty)
        {
            SaveCache();
        }

        unpriced = anyUnpriced;
        if (live.Count == 0 && spend.Snapshot().All(e => e.Agent != AgentToken.Codex))
        {
            return; // no rollout was ever seen: nothing to show, rather than a row of zeros
        }

        spend.Update(AgentToken.Codex, new SpendTotals(tokDay, centsDay, tokWeek, centsWeek, tokMonth, centsMonth, anyUnpriced), clock());
    }

    private RolloutTotals TotalsOf(FileInfo file, HashSet<string> live)
    {
        var key = Path.GetRelativePath(root, file.FullName).Replace('\\', '/');
        live.Add(key);
        var size = file.Length;
        var ticks = file.LastWriteTimeUtc.Ticks;
        if (cache.TryGetValue(key, out var hit) && hit.Size == size && hit.Ticks == ticks)
        {
            return new RolloutTotals(hit.Model, hit.Input, hit.CachedInput, hit.Output);
        }

        RolloutTotals totals;
        try
        {
            using var stream = open(file.FullName);
            // The model this file ran last time it was read, so a grown file needs only its end read.
            totals = CodexRollout.Totals(stream, hit?.Model);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return default;
        }

        cache[key] = new CacheEntry(size, ticks, totals.Model, totals.Input, totals.CachedInput, totals.Output);
        cacheDirty = true;
        return totals;
    }

    // ---- files

    private RolloutTail? TailOf(FileInfo file)
    {
        var size = file.Length;
        var ticks = file.LastWriteTimeUtc.Ticks;
        if (tails.TryGetValue(file.FullName, out var hit) && hit.Size == size && hit.Ticks == ticks)
        {
            return hit.Tail;
        }

        try
        {
            using var stream = open(file.FullName);
            var tail = CodexRollout.ParseTail(stream);
            if (tails.Count >= MaxTails)
            {
                tails.Clear();
            }

            tails[file.FullName] = (size, ticks, tail);
            return tail;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private DateOnly Today() => DateOnly.FromDateTime(clock().ToLocalTime().DateTime);

    /// <summary>When Codex wrote the limits: the file's last write, never later than now (a clock ahead of ours).</summary>
    private DateTimeOffset MeasuredAt(FileInfo file)
    {
        var written = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
        var now = clock();
        return written < now ? written : now;
    }

    private string DayDir(DateOnly day) =>
        Path.Combine(
            sessionsDir,
            day.Year.ToString("D4", CultureInfo.InvariantCulture),
            day.Month.ToString("D2", CultureInfo.InvariantCulture),
            day.Day.ToString("D2", CultureInfo.InvariantCulture));

    /// <summary>The newest rollouts first, walking the date folders from the newest down and stopping only when the caller does.</summary>
    private IEnumerable<FileInfo> NewestFirst()
    {
        foreach (var year in Numbered(sessionsDir, 4))
        {
            foreach (var month in Numbered(year, 2))
            {
                foreach (var day in Numbered(month, 2))
                {
                    foreach (var file in Rollouts(day).OrderByDescending(f => f.Name, StringComparer.Ordinal))
                    {
                        yield return file;
                    }
                }
            }
        }
    }

    /// <summary>The all-digit sub-folders of <paramref name="parent"/> with a name of <paramref name="length"/> characters, newest first, links skipped.</summary>
    private static List<string> Numbered(string parent, int length)
    {
        try
        {
            return
            [
                .. new DirectoryInfo(parent).EnumerateDirectories()
                    .Where(d => d.Name.Length == length && d.Name.All(char.IsAsciiDigit) && !Linked(d))
                    .Select(d => d.FullName)
                    .OrderByDescending(p => p, StringComparer.Ordinal),
            ];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>The regular <c>rollout-*.jsonl</c> files of a day folder; imports and links are left out.</summary>
    private static List<FileInfo> Rollouts(string dayDirectory)
    {
        try
        {
            var dir = new DirectoryInfo(dayDirectory);
            return !dir.Exists || Linked(dir)
                ? []
                : [.. dir.EnumerateFiles("rollout-*.jsonl").Where(f => f.Name.EndsWith(".jsonl", StringComparison.Ordinal) && !Linked(f) && !f.Name.Contains("external-import", StringComparison.OrdinalIgnoreCase))];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool Linked(FileSystemInfo info) => (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null;

    // ---- the spend cache

    private void LoadCache()
    {
        try
        {
            if (!File.Exists(cachePath))
            {
                return;
            }

            using var doc = JsonDocument.Parse(File.ReadAllBytes(cachePath), new JsonDocumentOptions { MaxDepth = 8 });
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("v", out var v) || !v.TryGetInt32(out var version) || version != CacheVersion
                || !doc.RootElement.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var f in files.EnumerateArray().Where(f => f.ValueKind == JsonValueKind.Object))
            {
                if (f.TryGetProperty("p", out var p) && p.ValueKind == JsonValueKind.String && p.GetString() is { Length: > 0 and <= 300 } key
                    && Long(f, "s") is { } size && Long(f, "t") is { } ticks && Long(f, "i") is { } input && Long(f, "c") is { } cached && Long(f, "o") is { } output)
                {
                    var model = f.TryGetProperty("m", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
                    cache[key] = new CacheEntry(size, ticks, model, input, cached, output);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            cache.Clear();
        }
    }

    private void SaveCache()
    {
        try
        {
            PrivateStorage.Replace(cachePath, temp =>
            {
                using var stream = File.Create(temp);
                using var json = new Utf8JsonWriter(stream);
                json.WriteStartObject();
                json.WriteNumber("v", CacheVersion);
                json.WriteStartArray("files");
                foreach (var (key, e) in cache)
                {
                    json.WriteStartObject();
                    json.WriteString("p", key);
                    json.WriteNumber("s", e.Size);
                    json.WriteNumber("t", e.Ticks);
                    if (e.Model is not null)
                    {
                        json.WriteString("m", e.Model);
                    }

                    json.WriteNumber("i", e.Input);
                    json.WriteNumber("c", e.CachedInput);
                    json.WriteNumber("o", e.Output);
                    json.WriteEndObject();
                }

                json.WriteEndArray();
                json.WriteEndObject();
            });
            cacheDirty = false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The cache only saves work; the next refresh rebuilds what is missing.
        }
    }

    private static long? Long(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) && n >= 0 ? n : null;

    private sealed record Tracked(long Size, long Ticks, string State, string Id);

    private sealed record CacheEntry(long Size, long Ticks, string? Model, long Input, long CachedInput, long Output);
}
