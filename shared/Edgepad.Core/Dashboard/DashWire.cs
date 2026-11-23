using System.Globalization;
using System.Text;
using Edgepad.Protocol;

namespace Edgepad.Dashboard;

/// <summary>TEXT STATS. Percents are 0..100, down and up are KiB/s, ping is ms, temperatures are C, power is W; -1 is unknown.</summary>
internal sealed record StatsSample(int Cpu, int Ram, int Gpu, int Vram, int Down, int Up, int Ping, int Loss, int CpuTemp, int GpuTemp, int GpuPower);

/// <summary>TEXT STATS_INFO: the names the numbers belong to and the memory sizes, -1 unknown.</summary>
internal sealed record StatsInfo(string CpuName, int RamMiB, string GpuName, int VramMiB, string NetName);

/// <summary>One disk of TEXT DISKS.</summary>
internal sealed record DiskEntry(string Label, int UsedPct, long TotalGiB, long FreeGiB);

/// <summary>One session of TEXT SESSIONS. <paramref name="Agent"/> is an <see cref="AgentToken"/> and <paramref name="State"/> a <see cref="SessionStateToken"/>. <paramref name="ContextPct"/> is how full the session's context window is, 0..100, -1 unknown.</summary>
internal sealed record SessionEntry(string Agent, string State, string Title, int ContextPct = -1);

/// <summary>One limit of TEXT LIMITS. <paramref name="Window"/> is minutes, <see cref="LimitWindow.Context"/>, or <see cref="LimitWindow.NamedPrefix"/> and a name.</summary>
internal sealed record LimitEntry(string Agent, string Window, double Pct, long ResetAtMin, long MeasuredAtMin);

/// <summary>One agent of TEXT SPEND. Cents are estimates at API list prices; <paramref name="Unpriced"/> marks them a lower bound (some tokens had no price).</summary>
internal sealed record SpendEntry(string Agent, long MeasuredAtMin, long TokToday, long CentsToday, long TokWeek, long CentsWeek, long TokMonth, long CentsMonth, bool Unpriced = false, long TokAll = -1, long CentsAll = -1);

/// <summary>TEXT WEATHER.</summary>
internal sealed record WeatherInfo(int TempTenthsC, int WmoCode, string City);

/// <summary>One line of BLOB GITHUB. <paramref name="Section"/> is R, M or N; <paramref name="Ci"/> is a <see cref="CiToken"/>.</summary>
internal sealed record GitHubLine(string Section, string Repo, long Number, string Title, bool Draft, string Ci);

/// <summary>The body of a BLOB PERM_ASK once read: who asks, with which tool, and the cleaned text.</summary>
internal sealed record PermAsk(string Agent, string Tool, string Text);

/// <summary>A TEXT PERM_ANSWER once read.</summary>
internal readonly record struct PermAnswer(string Id, bool Allow)
{
    /// <summary>Exactly 12 lowercase hex digits, "/", then "a" or "d". Anything else, a trailing space or upper case included, is refused.</summary>
    public static bool TryParse(string? wire, out PermAnswer answer)
    {
        answer = default;
        if (wire is not { Length: 14 } || wire[12] != '/' || !DashWire.IsId(wire.AsSpan(0, 12)) || wire[13] is not ('a' or 'd'))
        {
            return false;
        }

        answer = new PermAnswer(wire[..12], wire[13] == 'a');
        return true;
    }
}

/// <summary>
/// The text and blob codecs of the dashboard, byte for byte what protocol/dashboard-vectors.txt holds and what the Kotlin and
/// Swift codecs produce. Encoders clean and cap what they are given, so a caller cannot send a payload that breaks its own
/// format; readers drop an entry they cannot read instead of failing the list.
/// </summary>
internal static class DashWire
{
    private const int MaxText = 255;

    /// <summary>Control characters (Cc), space, "|", "/", ":" and the bidi and zero-width characters become a space, runs collapse, then trim and cut to <paramref name="max"/> code points, never inside a surrogate pair.</summary>
    public static string Clean(string text, int max)
    {
        var collapsed = new StringBuilder(text.Length);
        var gap = false;
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value is ' ' or '|' or '/' or ':' || Rune.IsControl(rune) || IsBidi(rune.Value))
            {
                gap = collapsed.Length > 0;
                continue;
            }

            if (gap)
            {
                collapsed.Append(' ');
                gap = false;
            }

            collapsed.Append(rune.ToString());
        }

        var kept = new StringBuilder(collapsed.Length);
        var count = 0;
        foreach (var rune in collapsed.ToString().EnumerateRunes())
        {
            if (count++ >= max)
            {
                break;
            }

            kept.Append(rune.ToString());
        }

        return kept.ToString().TrimEnd(' ');
    }

    /// <summary>
    /// For an approval's text: keeps line feeds and tabs, and shows every other control character (Cc: U+0000-001F, U+007F-009F,
    /// so a carriage return or U+0085 too) and every bidi or zero-width character as <c>&lt;U+XXXX&gt;</c>, so it can be read but
    /// never takes effect (Trojan Source). Nothing is deleted, so the shown text is never shorter than what runs. No trim and no
    /// cut. Idempotent: a marker is plain text.
    /// </summary>
    public static string CleanText(string text)
    {
        var clean = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value is '\n' or '\t')
            {
                clean.Append(rune.ToString());
            }
            else if (IsBidi(rune.Value) || Rune.IsControl(rune))
            {
                clean.Append("<U+").Append(rune.Value.ToString("X4", CultureInfo.InvariantCulture)).Append('>');
            }
            else
            {
                clean.Append(rune.ToString());
            }
        }

        return clean.ToString();
    }

    /// <summary>The entries joined by <paramref name="separator"/>, the last ones dropped until the result is at most 255 bytes.</summary>
    public static string Fit255(IReadOnlyList<string> parts, char separator) => Fit(parts, separator, MaxText);

    /// <summary>True for exactly 12 lowercase hex digits.</summary>
    public static bool IsId(ReadOnlySpan<char> id)
    {
        if (id.Length != 12)
        {
            return false;
        }

        foreach (var c in id)
        {
            if (c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
            {
                return false;
            }
        }

        return true;
    }

    // ---- STATS

    /// <summary>"cpu/ram/gpu/vram/down/up/ping/loss/ctemp/gtemp/gpower": percents clamp to 0..100 and any negative is -1.</summary>
    public static string EncodeStats(StatsSample s) =>
        string.Join(
            '/',
            Pct(s.Cpu), Pct(s.Ram), Pct(s.Gpu), Pct(s.Vram), Num(s.Down), Num(s.Up), Num(s.Ping), Pct(s.Loss), Num(s.CpuTemp), Num(s.GpuTemp), Num(s.GpuPower));

    public static bool TryParseStats(string? wire, out StatsSample sample)
    {
        sample = new StatsSample(-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1);
        var parts = (wire ?? string.Empty).Split('/');
        if (parts.Length != 11)
        {
            return false;
        }

        var n = new int[11];
        for (var i = 0; i < n.Length; i++)
        {
            if (!TryInt(parts[i], out n[i]))
            {
                return false;
            }
        }

        sample = new StatsSample(n[0], n[1], n[2], n[3], n[4], n[5], n[6], n[7], n[8], n[9], n[10]);
        return true;
    }

    // ---- STATS_INFO

    /// <summary>"cpuName|ramMiB|gpuName|vramMiB|netName", the names cleaned to 30 code points and, when the result passes 255 bytes, shortened together a code point at a time.</summary>
    public static string EncodeStatsInfo(StatsInfo info)
    {
        var text = string.Empty;
        for (var max = 30; max >= 0; max--)
        {
            text = string.Join('|', Clean(info.CpuName, max), Num(info.RamMiB), Clean(info.GpuName, max), Num(info.VramMiB), Clean(info.NetName, max));
            if (Encoding.UTF8.GetByteCount(text) <= MaxText)
            {
                break;
            }
        }

        return text;
    }

    public static bool TryParseStatsInfo(string? wire, out StatsInfo info)
    {
        info = new StatsInfo(string.Empty, -1, string.Empty, -1, string.Empty);
        var parts = (wire ?? string.Empty).Split('|');
        if (parts.Length != 5 || !TryInt(parts[1], out var ram) || !TryInt(parts[3], out var vram))
        {
            return false;
        }

        info = new StatsInfo(parts[0], ram, parts[2], vram, parts[4]);
        return true;
    }

    // ---- DISKS

    private const int MaxDisks = 6;

    /// <summary>"label:usedPct:totalGiB:freeGiB" entries joined by "|", at most 6, the last dropped until it fits 255 bytes.</summary>
    public static string EncodeDisks(IReadOnlyList<DiskEntry> disks)
    {
        var parts = new List<string>();
        foreach (var d in disks.Take(MaxDisks))
        {
            var label = Clean(d.Label, 16);
            parts.Add($"{(label.Length == 0 ? "-" : label)}:{Pct(d.UsedPct)}:{Big(d.TotalGiB, -1)}:{Big(d.FreeGiB, -1)}");
        }

        return Fit255(parts, '|');
    }

    /// <summary>An entry that does not read is skipped.</summary>
    public static IReadOnlyList<DiskEntry> ParseDisks(string? wire) =>
        Entries<DiskEntry>(wire, entry =>
        {
            var p = entry.Split(':');
            return p.Length == 4 && TryInt(p[1], out var used) && TryLong(p[2], out var total) && TryLong(p[3], out var free)
                ? new DiskEntry(p[0], used, total, free)
                : null;
        });

    // ---- SESSIONS

    private const int MaxSessions = 8;

    /// <summary>"agent:state:ctx:title" entries joined by "|", newest first as given, at most 8; ctx is 0..100 or -1; an unknown agent or state drops the entry.</summary>
    public static string EncodeSessions(IReadOnlyList<SessionEntry> sessions)
    {
        var parts = new List<string>();
        foreach (var s in sessions)
        {
            if (parts.Count == MaxSessions)
            {
                break;
            }

            if (IsAgent(s.Agent) && (s.State is SessionStateToken.Busy or SessionStateToken.Wait or SessionStateToken.Idle))
            {
                parts.Add($"{s.Agent}:{s.State}:{(s.ContextPct is >= 0 and <= 100 ? s.ContextPct : -1).ToString(CultureInfo.InvariantCulture)}:{Clean(s.Title, 24)}");
            }
        }

        return Fit255(parts, '|');
    }

    public static IReadOnlyList<SessionEntry> ParseSessions(string? wire) =>
        Entries<SessionEntry>(wire, entry =>
        {
            var p = entry.Split(':', 4);
            return p.Length == 4 && IsAgent(p[0]) && (p[1] is SessionStateToken.Busy or SessionStateToken.Wait or SessionStateToken.Idle)
                && TryLong(p[2], out var ctx)
                ? new SessionEntry(p[0], p[1], p[3], ctx is >= 0 and <= 100 ? (int)ctx : -1)
                : null;
        });

    // ---- PERM_ANSWER, PERM_ASK

    /// <summary>The label of a BLOB PERM_ASK: "id/f", f 1 when the text is complete.</summary>
    public static string PermLabel(string id, bool complete) => $"{id}/{(complete ? 1 : 0)}";

    public static bool TryParsePermLabel(string? label, out string id, out bool complete)
    {
        id = string.Empty;
        complete = false;
        if (label is not { Length: 14 } || label[12] != '/' || !IsId(label.AsSpan(0, 12)) || label[13] is not ('0' or '1'))
        {
            return false;
        }

        id = label[..12];
        complete = label[13] == '1';
        return true;
    }

    // ---- LIMITS

    /// <summary>"agent:window:pct:resetAtMin:measuredAtMin" entries joined by "|". An entry with an unknown agent or a window outside the three forms is dropped.</summary>
    public static string EncodeLimits(IReadOnlyList<LimitEntry> limits)
    {
        var parts = new List<(int Rank, string Text)>();
        foreach (var l in limits)
        {
            if (IsAgent(l.Agent) && TryWindow(l.Window, out var window))
            {
                var pct = double.IsNaN(l.Pct) ? 0 : (long)Math.Floor(Math.Clamp(l.Pct, 0, 100) + 0.5);
                parts.Add((LimitRank(window), $"{l.Agent}:{window}:{Num(pct)}:{Big(l.ResetAtMin, -1)}:{Big(l.MeasuredAtMin, -1)}"));
            }
        }

        // Only an overflow reorders: then every agent's 5 h and weekly window come first, so the cut loses the least important.
        if (Encoding.UTF8.GetByteCount(string.Join('|', parts.Select(p => p.Text))) > MaxText)
        {
            parts = [.. parts.OrderBy(p => p.Rank)];
        }

        return Fit255([.. parts.Select(p => p.Text)], '|');
    }

    // 0: the 5 h and weekly windows; 1: the context and any other duration; 2: a named window (a model's week); 3: credits.
    private static int LimitRank(string window) => window switch
    {
        "300" or "10080" => 0,
        "~spend_limit" => 3,
        _ when window[0] == LimitWindow.NamedPrefix => 2,
        _ => 1,
    };

    public static IReadOnlyList<LimitEntry> ParseLimits(string? wire) =>
        Entries<LimitEntry>(wire, entry =>
        {
            var p = entry.Split(':');
            return p.Length == 5 && IsAgent(p[0]) && TryWindow(p[1], out var window) && TryLong(p[2], out var pct)
                && TryLong(p[3], out var reset) && TryLong(p[4], out var measured)
                ? new LimitEntry(p[0], window, Math.Clamp(pct, 0, 100), reset, measured)
                : null;
        });

    /// <summary>The canonical form of a window: decimal minutes 1..525600 without a leading zero, "ctx", or "~" and a name cleaned to 16 code points that is not empty.</summary>
    public static bool TryWindow(string window, out string canonical)
    {
        canonical = string.Empty;
        if (window == LimitWindow.Context)
        {
            canonical = window;
            return true;
        }

        if (window.Length > 0 && window[0] == LimitWindow.NamedPrefix)
        {
            var name = Clean(window[1..], 16);
            canonical = LimitWindow.NamedPrefix + name;
            return name.Length > 0;
        }

        if (window.Length is < 1 or > 6 || window[0] == '0' || !window.All(char.IsAsciiDigit) || int.Parse(window, CultureInfo.InvariantCulture) > 525_600)
        {
            return false;
        }

        canonical = window;
        return true;
    }

    // ---- SPEND

    /// <summary>"agent/measuredAtMin/tokToday/centsToday/tok7d/cents7d/tok30d/cents30d/tokAll/centsAll/unpriced" entries joined by "|"; unpriced is 1 or 0; the all-time pair is -1 when unknown, any other negative is 0.</summary>
    public static string EncodeSpend(IReadOnlyList<SpendEntry> spend)
    {
        var parts = new List<string>();
        foreach (var s in spend)
        {
            if (IsAgent(s.Agent))
            {
                parts.Add(string.Join('/', s.Agent, Big(s.MeasuredAtMin, -1), Big(s.TokToday, 0), Big(s.CentsToday, 0), Big(s.TokWeek, 0), Big(s.CentsWeek, 0), Big(s.TokMonth, 0), Big(s.CentsMonth, 0), Big(s.TokAll, -1), Big(s.CentsAll, -1), s.Unpriced ? "1" : "0"));
            }
        }

        return Fit255(parts, '|');
    }

    public static IReadOnlyList<SpendEntry> ParseSpend(string? wire) =>
        Entries<SpendEntry>(wire, entry =>
        {
            var p = entry.Split('/');
            if (p.Length != 11 || !IsAgent(p[0]) || p[10] is not ("0" or "1"))
            {
                return null;
            }

            var n = new long[9];
            for (var i = 0; i < n.Length; i++)
            {
                if (!TryLong(p[i + 1], out n[i]))
                {
                    return null;
                }
            }

            return new SpendEntry(p[0], n[0], n[1], n[2], n[3], n[4], n[5], n[6], p[10] == "1", Math.Max(-1, n[7]), Math.Max(-1, n[8]));
        });

    // ---- WEATHER

    /// <summary>"tempTenthsC/wmoCode/city"; null is the empty payload, meaning no weather.</summary>
    public static string EncodeWeather(WeatherInfo? weather) =>
        weather is null
            ? string.Empty
            : $"{Math.Clamp(weather.TempTenthsC, -999, 999).ToString(CultureInfo.InvariantCulture)}/{(weather.WmoCode is >= 0 and <= 99 ? weather.WmoCode : -1).ToString(CultureInfo.InvariantCulture)}/{Clean(weather.City, 24)}";

    public static bool TryParseWeather(string? wire, out WeatherInfo weather)
    {
        weather = new WeatherInfo(0, -1, string.Empty);
        var p = (wire ?? string.Empty).Split('/', 3);
        if (p.Length != 3 || !TryInt(p[0], out var temp) || !TryInt(p[1], out var wmo))
        {
            return false;
        }

        weather = new WeatherInfo(Math.Clamp(temp, -999, 999), wmo is >= 0 and <= 99 ? wmo : -1, Clean(p[2], 24));
        return true;
    }

    // ---- private helpers

    private static string Pct(int v) => v < 0 ? "-1" : Math.Min(v, 100).ToString(CultureInfo.InvariantCulture);

    private static string Num(long v) => (v < 0 ? -1 : v).ToString(CultureInfo.InvariantCulture);

    private static string Big(long v, long whenNegative) => (v < 0 ? whenNegative : v).ToString(CultureInfo.InvariantCulture);

    internal static bool IsAgent(string agent) => agent is AgentToken.Claude or AgentToken.Codex or AgentToken.Gemini or AgentToken.OpenCode;

    // Invisible and direction-changing code points (Unicode categories Cf, Zl, Zp: bidi controls and isolates, zero-width
    // characters, U+061C, U+2028/2029, U+2060-2064, U+180E, tag characters U+E0000-E007F) could make a command on the
    // approval sheet read differently from what runs, so they are escaped as <U+XXXX>. Kotlin and Swift mirror this.
    internal static bool IsBidi(int v) =>
        Rune.IsValid(v)
        && Rune.GetUnicodeCategory(new Rune(v)) is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;

    internal static string Fit(IReadOnlyList<string> parts, char separator, int maxBytes)
    {
        for (var count = parts.Count; count > 0; count--)
        {
            var text = string.Join(separator, parts.Take(count));
            if (Encoding.UTF8.GetByteCount(text) <= maxBytes)
            {
                return text;
            }
        }

        return string.Empty;
    }

    /// <summary>The "|" entries of a list payload, each read by <paramref name="read"/>, the ones it returns null for skipped.</summary>
    internal static List<T> Entries<T>(string? wire, Func<string, T?> read)
        where T : class
    {
        var list = new List<T>();
        if (string.IsNullOrEmpty(wire))
        {
            return list;
        }

        foreach (var entry in wire.Split('|'))
        {
            if (read(entry) is { } item)
            {
                list.Add(item);
            }
        }

        return list;
    }

    /// <summary>An optional minus sign and digits, nothing else (no plus sign, no spaces), that fits a long.</summary>
    internal static bool TryLong(string s, out long value)
    {
        value = 0;
        var start = s.Length > 0 && s[0] == '-' ? 1 : 0;
        if (s.Length == start || s.Length > 19 || !s[start..].All(char.IsAsciiDigit))
        {
            return false;
        }

        return long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    internal static bool TryInt(string s, out int value)
    {
        value = 0;
        if (!TryLong(s, out var wide) || wide is < int.MinValue or > int.MaxValue)
        {
            return false;
        }

        value = (int)wide;
        return true;
    }
}

/// <summary>The body of a BLOB PERM_ASK: UTF-8 "agent", "tool" and "text" joined by line feeds, at most 4096 bytes.</summary>
internal static class PermAskBody
{
    public const int MaxBytes = 4096;
    private const int MaxToolLength = 24;

    /// <summary>
    /// The body for an approval. The text is cleaned with <see cref="DashWire.CleanText"/> and the tool name reduced to A-Za-z0-9_.-.
    /// Complete only when nothing was cut on the way (<paramref name="truncated"/> false), the head plus the RAW text fit 4096 bytes,
    /// and the cleaned body fits too: the raw text is measured, never only the cleaned one, so cleaning can never hide a cut. A
    /// client or server cut always leaves at least 4999 characters (<see cref="Edgepad.Hooks.HookClient.InputCap"/>), so it fails the raw
    /// size test by itself. Otherwise the text is left out and <paramref name="complete"/> is false, so the phone offers Deny only.
    /// </summary>
    public static string Encode(string agent, string tool, string text, out bool complete, bool truncated = false)
    {
        var head = $"{agent}\n{Tool(tool)}\n";
        var body = head + DashWire.CleanText(text);
        complete = !truncated
            && Encoding.UTF8.GetByteCount(head) + Encoding.UTF8.GetByteCount(text) <= MaxBytes
            && Encoding.UTF8.GetByteCount(body) <= MaxBytes;
        return complete ? body : head;
    }

    /// <summary>The agent must be known, the tool 1..24 of A-Za-z0-9_.-, and the body at most 4096 bytes. The text may hold more line feeds.</summary>
    public static bool TryParse(string? body, out PermAsk ask)
    {
        ask = new PermAsk(string.Empty, string.Empty, string.Empty);
        if (body is null || Encoding.UTF8.GetByteCount(body) > MaxBytes)
        {
            return false;
        }

        var first = body.IndexOf('\n');
        var second = first < 0 ? -1 : body.IndexOf('\n', first + 1);
        if (second < 0)
        {
            return false;
        }

        var agent = body[..first];
        var tool = body[(first + 1)..second];
        if (!DashWire.IsAgent(agent) || tool.Length is < 1 or > MaxToolLength || !tool.All(IsToolChar))
        {
            return false;
        }

        ask = new PermAsk(agent, tool, body[(second + 1)..]);
        return true;
    }

    private static bool IsToolChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-';

    private static string Tool(string tool)
    {
        var clean = new StringBuilder();
        foreach (var rune in tool.EnumerateRunes())
        {
            if (clean.Length == MaxToolLength)
            {
                break;
            }

            clean.Append(rune.IsAscii && IsToolChar((char)rune.Value) ? (char)rune.Value : '_');
        }

        return clean.Length == 0 ? "unknown" : clean.ToString();
    }
}

/// <summary>
/// The body of the older BLOB 7 (GITHUB): lines of <c>section TAB repo TAB number TAB title TAB flags TAB ci</c> joined by line feeds, at most 8 KiB.
/// Nothing sends it any more (<see cref="GitBlob"/> replaced it); it stays only for protocol/dashboard-vectors.txt, which the Swift and
/// Kotlin ports still read until their git steps (C24, C25) retire the vectors with it.
/// </summary>
internal static class GitHubBlob
{
    public const int MaxBytes = 8192;
    public const int MaxPerSection = 10;
    private const int MaxRepoPart = 100;

    /// <summary>
    /// Lines in the order given, an invalid one dropped, at most 10 per section, the last lines dropped until it fits 8 KiB.
    /// An N line always reads <c>N TAB TAB count TAB TAB TAB none</c>.
    /// </summary>
    public static string Encode(IReadOnlyList<GitHubLine> lines)
    {
        var perSection = new Dictionary<string, int>();
        var rows = new List<string>();
        foreach (var raw in lines)
        {
            if (Normalise(raw) is not { } line)
            {
                continue;
            }

            perSection[line.Section] = perSection.GetValueOrDefault(line.Section) + 1;
            if (perSection[line.Section] > MaxPerSection)
            {
                continue;
            }

            rows.Add(string.Join('\t', line.Section, line.Repo, line.Number.ToString(CultureInfo.InvariantCulture), line.Title, line.Draft ? "d" : string.Empty, line.Ci));
        }

        return DashWire.Fit(rows, '\n', MaxBytes);
    }

    /// <summary>Lines that do not read, or are blank, are skipped.</summary>
    public static IReadOnlyList<GitHubLine> Parse(string? body)
    {
        var list = new List<GitHubLine>();
        foreach (var row in (body ?? string.Empty).Split('\n'))
        {
            var f = row.Split('\t');
            if (f.Length == 6 && DashWire.TryLong(f[2], out var number) && Normalise(new GitHubLine(f[0], f[1], number, f[3], f[4] == "d", f[5])) is { } line)
            {
                list.Add(line);
            }
        }

        return list;
    }

    private static GitHubLine? Normalise(GitHubLine line)
    {
        if (line.Number < 0)
        {
            return null;
        }

        if (line.Section == "N")
        {
            return new GitHubLine("N", string.Empty, line.Number, string.Empty, false, CiToken.None);
        }

        if (line.Section is not ("R" or "M") || !IsRepo(line.Repo))
        {
            return null;
        }

        var ci = line.Ci is CiToken.Ok or CiToken.Fail or CiToken.Pending ? line.Ci : CiToken.None;
        return new GitHubLine(line.Section, line.Repo, line.Number, DashWire.Clean(line.Title, 80), line.Draft, ci);
    }

    private static bool IsRepo(string repo)
    {
        var parts = repo.Split('/');
        return parts.Length == 2 && parts.All(part => part.Length is >= 1 and <= MaxRepoPart && part.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-'));
    }
}
