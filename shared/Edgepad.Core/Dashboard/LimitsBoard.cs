using System.Globalization;
using Edgepad.Protocol;

namespace Edgepad.Dashboard;

/// <summary>
/// One window as an agent reported it. <paramref name="Window"/> is Claude's <c>kind</c> or, for Codex, decimal minutes.
/// <paramref name="ResetsAt"/> is an ISO date or epoch seconds, or null.
/// </summary>
internal readonly record struct RawLimit(string Window, double Pct, string? ResetsAt);

/// <summary>
/// The last-known usage limits of each agent, for TEXT LIMITS. Claude's <c>five_hour</c> is 300 minutes and <c>seven_day</c> 10080;
/// every other kind is kept under <c>~kind</c> (never dropped, only an empty one). The context window is <c>ctx</c>. A window an
/// agent stops reporting stays for 24 hours as last measured, then goes. <see cref="Changed"/> fires outside the lock when a
/// percentage, a reset time or the set of windows changed; a fresher measurement of the same numbers is stored but not announced.
/// </summary>
internal sealed class LimitsBoard(UsageStore store)
{
    public static readonly TimeSpan KeepAbsent = TimeSpan.FromHours(24);

    public event Action? Changed;

    /// <summary>The wire window for what an agent called it, or null when it cannot be one.</summary>
    public static string? WindowFor(string agent, string window)
    {
        var name = agent == AgentToken.Claude
            ? window switch
            {
                "five_hour" => "300",
                "seven_day" => "10080",
                _ => LimitWindow.NamedPrefix + window,
            }
            : window;
        return DashWire.IsAgent(agent) && DashWire.TryWindow(name, out var canonical) ? canonical : null;
    }

    public void Update(string agent, IEnumerable<RawLimit> windows, double? contextPct, DateTimeOffset now)
    {
        if (!DashWire.IsAgent(agent))
        {
            return;
        }

        var nowMin = UsageStore.Minutes(now);
        var incoming = new Dictionary<string, LimitRow>();
        foreach (var raw in windows)
        {
            if (WindowFor(agent, raw.Window) is { } window && Round(raw.Pct) is { } pct)
            {
                incoming[window] = new LimitRow(pct, ResetMinutes(raw.ResetsAt), nowMin);
            }
        }

        if (contextPct is { } context && Round(context) is { } contextRounded)
        {
            incoming[LimitWindow.Context] = new LimitRow(contextRounded, -1, nowMin);
        }

        var changed = false;
        lock (store.Gate)
        {
            foreach (var (window, row) in incoming)
            {
                var key = (agent, window);
                changed |= !store.Limits.TryGetValue(key, out var old) || old.Pct != row.Pct || old.ResetAtMin != row.ResetAtMin;
                store.Limits[key] = row;
            }

            var cutoff = nowMin - (long)KeepAbsent.TotalMinutes;
            foreach (var key in store.Limits.Where(l => l.Key.Agent == agent && !incoming.ContainsKey(l.Key.Window) && l.Value.MeasuredAtMin < cutoff).Select(l => l.Key).ToList())
            {
                store.Limits.Remove(key);
                changed = true;
            }

            if (changed)
            {
                store.Save();
            }
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Claude first, then Codex; within an agent the shortest window first, then named limits, then the context.</summary>
    public IReadOnlyList<LimitEntry> Snapshot()
    {
        lock (store.Gate)
        {
            return
            [
                .. store.Limits
                    .OrderBy(l => l.Key.Agent == AgentToken.Claude ? 0 : 1)
                    .ThenBy(l => Rank(l.Key.Window))
                    .ThenBy(l => l.Key.Window, StringComparer.Ordinal)
                    .Select(l => new LimitEntry(l.Key.Agent, l.Key.Window, l.Value.Pct, l.Value.ResetAtMin, l.Value.MeasuredAtMin)),
            ];
        }
    }

    // Minutes in numeric order (the string is padded in the sort), then named limits, then the context.
    private static string Rank(string window) =>
        window == LimitWindow.Context ? "3" : window[0] == LimitWindow.NamedPrefix ? "2" : "1" + window.PadLeft(6, '0');

    private static long? Round(double pct) =>
        double.IsNaN(pct) ? null : (long)Math.Floor(Math.Clamp(pct, 0, 100) + 0.5);

    private static long ResetMinutes(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return -1;
        }

        var value = text.Trim();
        if (value.Length is >= 1 and <= 15 && value.All(char.IsAsciiDigit))
        {
            var number = long.Parse(value, CultureInfo.InvariantCulture);
            return value.Length >= 13 ? number / 60_000 : number / 60;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? Math.Max(-1, UsageStore.Minutes(parsed))
            : -1;
    }
}
