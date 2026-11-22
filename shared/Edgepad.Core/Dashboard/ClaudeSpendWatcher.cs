using System.Text.Json;
using Edgepad.Protocol;
using Edgepad.Trust;

namespace Edgepad.Dashboard;

/// <summary>
/// Claude Code's spend for the dashboard, read by the app from the transcripts (<c>projects/**/*.jsonl</c> under the Claude folder)
/// so the plugin needs no script runtime. Like <see cref="CodexWatcher"/> it has a timer only while the Claude integration is on AND
/// a phone is linked (<see cref="Evaluate"/>), and it reads files only while a phone draws the AI widget or screen (DASH_WATCH bit 3):
/// at once when the bit is gained, then once a minute. Each file's responses are cached by size and modification time, in memory
/// and in <c>cachePath</c> (ids, days and numbers only), so an unchanged month costs stats only; files last written over 40 days ago
/// are skipped. A tick parses at most <see cref="BytesPerTick"/> of changed files (at least one file); the totals go to the
/// <see cref="SpendBoard"/> only after a tick that had every file in hand, so a first scan of a large folder spreads over ticks
/// instead of sending a partial sum. Nothing from a transcript leaves this class except those totals.
/// </summary>
internal sealed class ClaudeSpendWatcher : IDisposable
{
    public const int AiBit = CodexWatcher.AiBit;
    public const string CacheName = "dashboard-claude-cache.json";
    public const long BytesPerTick = 32L * 1024 * 1024;
    public static readonly TimeSpan Every = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(1);

    /// <summary>The cache file is written at most this often while scanning, and again on <see cref="Dispose"/>.</summary>
    public static readonly TimeSpan SaveEvery = TimeSpan.FromMinutes(5);

    private const int WindowDays = 40;
    private const int CacheVersion = 1;
    private const long MaxCacheBytes = 64L * 1024 * 1024;

    private readonly Func<bool> settingsOn;
    private readonly Func<bool> phoneLinked;
    private readonly SpendBoard spend;
    private readonly Func<DateTimeOffset> clock;
    private readonly string projects;
    private readonly string cachePath;
    private readonly Func<string, Stream> open;
    private readonly Func<Action, TimeSpan, IDisposable> startTimer;
    private readonly Lock gate = new();
    private readonly Lock pollGate = new();
    private readonly Dictionary<string, CacheEntry> cache = new(StringComparer.Ordinal);
    private IDisposable? timer;
    private int watch;
    private bool due = true;
    private DateTimeOffset lastDone = DateTimeOffset.MinValue;
    private bool cacheDirty;
    private DateTimeOffset cacheSaved = DateTimeOffset.MinValue;
    private bool logged;
    private volatile bool unpriced;

    /// <param name="root">The Claude folder, <see cref="DefaultRoot"/> in the app.</param>
    /// <param name="cachePath">Where the per-file cache lives, in the app's data folder.</param>
    /// <param name="open">Opens a transcript for reading. Tests spy on it.</param>
    /// <param name="startTimer">Runs the action every interval on another thread until disposed.</param>
    public ClaudeSpendWatcher(
        Func<bool> settingsOn,
        Func<bool> phoneLinked,
        SpendBoard spend,
        Func<DateTimeOffset> clock,
        string root,
        string cachePath,
        Func<string, Stream>? open = null,
        Func<Action, TimeSpan, IDisposable>? startTimer = null)
    {
        this.settingsOn = settingsOn;
        this.phoneLinked = phoneLinked;
        this.spend = spend;
        this.clock = clock;
        projects = Path.Combine(root, "projects");
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

    /// <summary>True when the last totals counted tokens of a model with no price, so their cents are a lower bound.</summary>
    public bool HasUnpriced => unpriced;

    /// <summary><c>CLAUDE_CONFIG_DIR</c>, else <c>.claude</c> in the user's home.</summary>
    public static string DefaultRoot() =>
        Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } dir && Path.IsPathFullyQualified(dir)
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

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

    /// <summary>The phone's DASH_WATCH. Gaining bit 3 refreshes on the next tick.</summary>
    public void SetWatch(int mask)
    {
        lock (gate)
        {
            if ((watch & AiBit) == 0 && (mask & AiBit) != 0)
            {
                due = true;
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

        // After a running tick, so the cache it changed is the one written.
        lock (pollGate)
        {
            if (cacheDirty)
            {
                SaveCache();
            }
        }
    }

    /// <summary>One tick. Public to the timer and to tests; never throws, never overlaps itself.</summary>
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

            lock (gate)
            {
                if ((watch & AiBit) == 0 || (!due && clock() - lastDone < RefreshEvery))
                {
                    return;
                }
            }

            if (Scan())
            {
                lock (gate)
                {
                    due = false;
                    lastDone = clock();
                }
            }
        }
        catch (Exception e)
        {
            // A timer thread must not end the process: whatever a file or the disk does, the next tick starts afresh.
            if (!logged)
            {
                logged = true;
                Log.Write($"claude spend watcher: {e.GetType().Name}");
            }
        }
        finally
        {
            pollGate.Exit();
        }
    }

    // True once every file was in hand and the totals were handed on.
    private bool Scan()
    {
        var now = clock();
        var periods = ClaudeSpend.Periods(now.ToLocalTime().DateTime);
        var oldest = now.UtcDateTime.AddDays(-WindowDays);
        var budget = BytesPerTick;
        var complete = true;
        var live = new HashSet<string>(StringComparer.Ordinal);
        var byId = new Dictionary<string, ClaudeRow>(StringComparer.Ordinal);
        foreach (var file in Transcripts())
        {
            if (file.LastWriteTimeUtc < oldest)
            {
                continue;
            }

            var key = Path.GetRelativePath(projects, file.FullName).Replace('\\', '/');
            live.Add(key);
            var size = file.Length;
            var ticks = file.LastWriteTimeUtc.Ticks;
            Dictionary<string, ClaudeRow> rows;
            if (cache.TryGetValue(key, out var hit) && hit.Size == size && hit.Ticks == ticks && string.CompareOrdinal(hit.From, periods.From) <= 0)
            {
                rows = hit.Rows;
            }
            else if (budget <= 0)
            {
                complete = false;
                continue;
            }
            else
            {
                var grown = hit is not null && hit.Size < size && hit.From == periods.From ? hit : null;
                budget -= size - (grown?.Size ?? 0);
                try
                {
                    using var stream = open(file.FullName);
                    rows = (grown is null ? null : Appended(stream, grown, periods.From)) ?? ClaudeSpend.SumFile(stream, periods.From);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    continue; // removed or locked mid-scan: skipped this time
                }

                cache[key] = new CacheEntry(size, ticks, periods.From, rows);
                cacheDirty = true;
            }

            foreach (var (id, row) in rows)
            {
                byId[id] = row;
            }
        }

        if (complete)
        {
            foreach (var gone in cache.Keys.Where(k => !live.Contains(k)).ToList())
            {
                cache.Remove(gone);
                cacheDirty = true;
            }
        }

        if (cacheDirty && now - cacheSaved >= SaveEvery)
        {
            SaveCache();
            cacheSaved = now;
        }

        if (!complete)
        {
            return false;
        }

        var (totals, anyUnpriced) = ClaudeSpend.Total(byId.Values, periods);
        unpriced = anyUnpriced;
        if (live.Count == 0 && spend.Snapshot().All(e => e.Agent != AgentToken.Claude))
        {
            return true; // no transcript was ever seen: nothing to show, rather than a row of zeros
        }

        spend.Update(AgentToken.Claude, totals with { Unpriced = anyUnpriced }, now);
        return true;
    }

    /// <summary>
    /// A transcript that only grew since it was cached: the cached rows plus those of the new bytes, read from the old end. Null
    /// (read it whole) when the stream cannot seek or the old end was not a line end, as when Claude was mid-write.
    /// </summary>
    private static Dictionary<string, ClaudeRow>? Appended(Stream stream, CacheEntry old, string from)
    {
        if (!stream.CanSeek || old.Size <= 0 || stream.Length < old.Size)
        {
            return null;
        }

        stream.Seek(old.Size - 1, SeekOrigin.Begin);
        if (stream.ReadByte() != '\n')
        {
            stream.Seek(0, SeekOrigin.Begin);
            return null;
        }

        var rows = new Dictionary<string, ClaudeRow>(old.Rows, StringComparer.Ordinal);
        foreach (var (id, row) in ClaudeSpend.SumFile(stream, from))
        {
            rows[id] = row;
        }

        return rows;
    }

    /// <summary>The most bytes read from the end of one transcript to find its last record.</summary>
    public const int TailBytes = 64 * 1024;

    /// <summary>
    /// The sessions behind the newest transcripts (<c>projects/&lt;folder&gt;/&lt;id&gt;.jsonl</c>) written within the
    /// <see cref="SessionBoard.Expiry"/>, at most <see cref="SessionBoard.Capacity"/>, for <see cref="SessionBoard.Seed"/>. One
    /// directory listing and at most <see cref="TailBytes"/> read per file, run at start and per connect, never on a timer. A
    /// session is working when its last record is unfinished (a prompt, a tool call or a tool result) and the file was written
    /// within <see cref="CodexWatcher.Fresh"/>, else idle; waiting is only ever known from a hook.
    /// </summary>
    public List<SeedSession> ListSessions()
    {
        var now = clock();
        var oldest = now.UtcDateTime - SessionBoard.Expiry;
        var found = new List<SeedSession>();
        foreach (var file in Transcripts()
            .Where(f => f.LastWriteTimeUtc > oldest && Path.GetRelativePath(projects, f.FullName).Split('\\', '/').Length == 2)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Take(SessionBoard.Capacity))
        {
            var id = Path.GetFileNameWithoutExtension(file.Name);
            if (id.Length is 0 or > SessionBoard.MaxIdLength)
            {
                continue;
            }

            try
            {
                using var stream = open(file.FullName);
                var (unfinished, cwd) = LastRecord(stream);
                var busy = unfinished && now.UtcDateTime - file.LastWriteTimeUtc <= CodexWatcher.Fresh;
                found.Add(new SeedSession(AgentToken.Claude, id, busy ? SessionStateToken.Busy : SessionStateToken.Idle, cwd, new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // removed or locked: skipped this time
            }
        }

        return found;
    }

    // Reads only the last TailBytes. unfinished: the last turn record is not an ended turn. cwd: the newest one seen, else empty.
    private static (bool Unfinished, string Cwd) LastRecord(Stream stream)
    {
        if (!stream.CanSeek)
        {
            return (false, string.Empty);
        }

        var length = stream.Length;
        var start = Math.Max(0, length - TailBytes);
        stream.Seek(start, SeekOrigin.Begin);
        var buffer = new byte[length - start];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = stream.Read(buffer, read, buffer.Length - read);
            if (n <= 0)
            {
                break;
            }

            read += n;
        }

        var text = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
        if (start > 0)
        {
            var cut = text.IndexOf('\n', StringComparison.Ordinal);
            text = cut < 0 ? string.Empty : text[(cut + 1)..];
        }

        bool? unfinished = null;
        var cwd = string.Empty;
        foreach (var line in text.Split('\n').Reverse())
        {
            if (line.Length == 0 || (unfinished is not null && cwd.Length > 0))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 16 });
                var r = doc.RootElement;
                if (r.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (cwd.Length == 0 && Str(r, "cwd") is { Length: > 0 } dir)
                {
                    cwd = dir;
                }

                if (unfinished is null)
                {
                    unfinished = Str(r, "type") switch
                    {
                        "user" => true,
                        "assistant" => !(r.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.Object
                            && Str(m, "stop_reason") is "end_turn" or "stop_sequence" or "max_tokens"),
                        "system" when Str(r, "subtype") == "turn_duration" => false,
                        _ => null,
                    };
                }
            }
            catch (JsonException)
            {
                // a torn or foreign line: look further back
            }
        }

        return (unfinished ?? false, cwd);
    }

    // Every regular *.jsonl under projects/, links skipped.
    private List<FileInfo> Transcripts()
    {
        try
        {
            var dir = new DirectoryInfo(projects);
            if (!dir.Exists || (dir.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return [];
            }

            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint, MaxRecursionDepth = 8 };
            return [.. dir.EnumerateFiles("*.jsonl", options).Where(f => f.Name.EndsWith(".jsonl", StringComparison.Ordinal))];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // ---- the cache: {"v":1,"files":[{"p","s","t","f","r":[[id,day,tokens,usd,priced 0|1],...]}]}

    private void LoadCache()
    {
        try
        {
            var info = new FileInfo(cachePath);
            if (!info.Exists || info.Length > MaxCacheBytes)
            {
                return;
            }

            using var doc = JsonDocument.Parse(File.ReadAllBytes(cachePath), new JsonDocumentOptions { MaxDepth = 8 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("v", out var v) || !v.TryGetInt32(out var version) || version != CacheVersion
                || !root.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var f in files.EnumerateArray().Where(f => f.ValueKind == JsonValueKind.Object))
            {
                if (Str(f, "p") is not { Length: > 0 and <= 1024 } key || Long(f, "s") is not { } size || Long(f, "t") is not { } ticks
                    || Str(f, "f") is not { Length: 10 } from || !f.TryGetProperty("r", out var list) || list.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var rows = new Dictionary<string, ClaudeRow>(StringComparer.Ordinal);
                foreach (var r in list.EnumerateArray())
                {
                    if (r.ValueKind == JsonValueKind.Array && r.GetArrayLength() == 5
                        && r[0].ValueKind == JsonValueKind.String && r[0].GetString() is { Length: > 0 and <= 256 } id
                        && r[1].ValueKind == JsonValueKind.String && r[1].GetString() is { Length: 10 } day
                        && r[2].ValueKind == JsonValueKind.Number && r[2].TryGetDouble(out var tokens) && tokens >= 0
                        && r[3].ValueKind == JsonValueKind.Number && r[3].TryGetDouble(out var usd) && usd >= 0
                        && r[4].ValueKind == JsonValueKind.Number && r[4].TryGetInt32(out var priced))
                    {
                        rows[id] = new ClaudeRow(day, tokens, usd, priced == 1);
                    }
                }

                cache[key] = new CacheEntry(size, ticks, from, rows);
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
                    json.WriteString("f", e.From);
                    json.WriteStartArray("r");
                    foreach (var (id, row) in e.Rows)
                    {
                        json.WriteStartArray();
                        json.WriteStringValue(id);
                        json.WriteStringValue(row.Day);
                        json.WriteNumberValue(row.Tokens);
                        json.WriteNumberValue(row.Usd);
                        json.WriteNumberValue(row.Priced ? 1 : 0);
                        json.WriteEndArray();
                    }

                    json.WriteEndArray();
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

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long? Long(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) && n >= 0 ? n : null;

    private sealed record CacheEntry(long Size, long Ticks, string From, Dictionary<string, ClaudeRow> Rows);
}
