using Edgepad.Protocol;

namespace Edgepad.Dashboard;

/// <summary>
/// The last-known token and estimated-cent totals of each agent, for TEXT SPEND. A total is clamped to 0 and the caps below, so a
/// wrong number cannot break the wire format. <see cref="Changed"/> fires outside the lock when a total changed.
/// </summary>
internal sealed class SpendBoard(UsageStore store)
{
    public const long MaxTokens = 10_000_000_000_000;
    public const long MaxCents = 10_000_000_000;

    public event Action? Changed;

    public void Update(string agent, SpendTotals totals, DateTimeOffset now)
    {
        if (!DashWire.IsAgent(agent))
        {
            return;
        }

        var clamped = new SpendTotals(
            Math.Clamp(totals.TokToday, 0, MaxTokens),
            Math.Clamp(totals.CentsToday, 0, MaxCents),
            Math.Clamp(totals.TokWeek, 0, MaxTokens),
            Math.Clamp(totals.CentsWeek, 0, MaxCents),
            Math.Clamp(totals.TokMonth, 0, MaxTokens),
            Math.Clamp(totals.CentsMonth, 0, MaxCents),
            totals.Unpriced,
            Math.Clamp(totals.TokAll, -1, MaxTokens),
            Math.Clamp(totals.CentsAll, -1, MaxCents));
        bool changed;
        lock (store.Gate)
        {
            changed = !store.Spend.TryGetValue(agent, out var old) || old.Totals != clamped;
            store.Spend[agent] = new SpendRow(UsageStore.Minutes(now), clamped);
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

    /// <summary>Claude first, then Codex.</summary>
    public IReadOnlyList<SpendEntry> Snapshot()
    {
        lock (store.Gate)
        {
            return
            [
                .. store.Spend
                    .OrderBy(s => s.Key == AgentToken.Claude ? 0 : 1)
                    .Select(s => new SpendEntry(s.Key, s.Value.MeasuredAtMin, s.Value.Totals.TokToday, s.Value.Totals.CentsToday, s.Value.Totals.TokWeek, s.Value.Totals.CentsWeek, s.Value.Totals.TokMonth, s.Value.Totals.CentsMonth, s.Value.Totals.Unpriced, s.Value.Totals.TokAll, s.Value.Totals.CentsAll)),
            ];
        }
    }
}
