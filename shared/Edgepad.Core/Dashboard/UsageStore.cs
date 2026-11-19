using System.Text.Json;
using Edgepad.Trust;

namespace Edgepad.Dashboard;

/// <summary>The last-known value of one limit window. Pct is rounded 0..100; the times are UTC minutes since the Unix epoch, -1 unknown.</summary>
internal sealed record LimitRow(long Pct, long ResetAtMin, long MeasuredAtMin);

/// <summary>Token and cent totals of one agent. Tokens are uncached input plus output; cents are estimates at API list prices.</summary>
internal readonly record struct SpendTotals(long TokToday, long CentsToday, long TokWeek, long CentsWeek, long TokMonth, long CentsMonth);

/// <summary>The last-known spend of one agent and when it was measured.</summary>
internal sealed record SpendRow(long MeasuredAtMin, SpendTotals Totals);

/// <summary>
/// What the limits and spend boards remember between runs, in <c>dashboard-usage.json</c>: window names, percentages, token and
/// cent totals and minute stamps. No prompt, path, command, name or secret ever goes in it. A file that is missing, damaged or
/// from another version is an empty store; an entry older than 30 days is dropped on load. The boards share one store, so
/// one file and one lock (<see cref="Gate"/>) cover limits, spend and last-heard.
/// </summary>
internal sealed class UsageStore
{
    public const string FileName = "dashboard-usage.json";
    internal static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    private const int Version = 1;
    private readonly string path;
    private bool warned;

    public UsageStore(string path, DateTimeOffset now)
    {
        this.path = path;
        Load(Minutes(now));
    }

    /// <summary>Held by a board for as long as it reads or changes the maps below.</summary>
    internal Lock Gate { get; } = new();

    internal Dictionary<(string Agent, string Window), LimitRow> Limits { get; } = [];

    internal Dictionary<string, SpendRow> Spend { get; } = [];

    internal Dictionary<string, long> Heard { get; } = [];

    internal static long Minutes(DateTimeOffset time) => time.ToUnixTimeSeconds() / 60;

    /// <summary>Stamps a hook message from <paramref name="agent"/>. Written to disk only when the minute moved.</summary>
    public void Touch(string agent, DateTimeOffset now)
    {
        if (!DashWire.IsAgent(agent))
        {
            return;
        }

        lock (Gate)
        {
            var minute = Minutes(now);
            if (Heard.TryGetValue(agent, out var before) && before == minute)
            {
                return;
            }

            Heard[agent] = minute;
            Save();
        }
    }

    /// <summary>UTC minutes of the last accepted hook message from the agent, or -1.</summary>
    public long LastHeard(string agent)
    {
        lock (Gate)
        {
            return Heard.GetValueOrDefault(agent, -1);
        }
    }

    /// <summary>Writes the maps through <see cref="PrivateStorage.Replace"/>. False (logged once) when the disk refuses; memory is unaffected.</summary>
    public bool Save()
    {
        lock (Gate)
        {
            try
            {
                PrivateStorage.Replace(path, temp =>
                {
                    using var stream = File.Create(temp);
                    using var json = new Utf8JsonWriter(stream);
                    json.WriteStartObject();
                    json.WriteNumber("v", Version);
                    json.WriteStartArray("limits");
                    foreach (var ((agent, window), row) in Limits)
                    {
                        json.WriteStartObject();
                        json.WriteString("a", agent);
                        json.WriteString("w", window);
                        json.WriteNumber("p", row.Pct);
                        json.WriteNumber("r", row.ResetAtMin);
                        json.WriteNumber("m", row.MeasuredAtMin);
                        json.WriteEndObject();
                    }

                    json.WriteEndArray();
                    json.WriteStartArray("spend");
                    foreach (var (agent, row) in Spend)
                    {
                        json.WriteStartObject();
                        json.WriteString("a", agent);
                        json.WriteNumber("m", row.MeasuredAtMin);
                        json.WriteStartArray("t");
                        foreach (var n in new[] { row.Totals.TokToday, row.Totals.CentsToday, row.Totals.TokWeek, row.Totals.CentsWeek, row.Totals.TokMonth, row.Totals.CentsMonth })
                        {
                            json.WriteNumberValue(n);
                        }

                        json.WriteEndArray();
                        json.WriteEndObject();
                    }

                    json.WriteEndArray();
                    json.WriteStartObject("heard");
                    foreach (var (agent, minute) in Heard)
                    {
                        json.WriteNumber(agent, minute);
                    }

                    json.WriteEndObject();
                    json.WriteEndObject();
                });
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (!warned)
                {
                    warned = true;
                    Log.Write("dashboard usage not saved");
                }

                return false;
            }
        }
    }

    private void Load(long nowMin)
    {
        var oldest = nowMin - (long)MaxAge.TotalMinutes;
        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            using var doc = JsonDocument.Parse(File.ReadAllBytes(path), new JsonDocumentOptions { MaxDepth = 8 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("v", out var v) || v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var version) || version != Version)
            {
                return;
            }

            foreach (var item in Items(root, "limits"))
            {
                if (Str(item, "a") is { } agent && DashWire.IsAgent(agent) && Str(item, "w") is { } window
                    && DashWire.TryWindow(window, out var canonical) && canonical == window
                    && Num(item, "p") is { } pct && Num(item, "r") is { } reset && Num(item, "m") is { } measured && measured >= oldest)
                {
                    Limits[(agent, window)] = new LimitRow(Math.Clamp(pct, 0, 100), reset, measured);
                }
            }

            foreach (var item in Items(root, "spend"))
            {
                if (Str(item, "a") is { } agent && DashWire.IsAgent(agent) && Num(item, "m") is { } measured && measured >= oldest
                    && item.TryGetProperty("t", out var t) && t.ValueKind == JsonValueKind.Array && t.GetArrayLength() == 6)
                {
                    var n = t.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out var x) && x >= 0 ? x : -1).ToArray();
                    if (n.All(x => x >= 0))
                    {
                        Spend[agent] = new SpendRow(measured, new SpendTotals(n[0], n[1], n[2], n[3], n[4], n[5]));
                    }
                }
            }

            if (root.TryGetProperty("heard", out var heard) && heard.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in heard.EnumerateObject())
                {
                    if (DashWire.IsAgent(property.Name) && property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt64(out var minute) && minute >= oldest)
                    {
                        Heard[property.Name] = minute;
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            Limits.Clear();
            Spend.Clear();
            Heard.Clear();
        }
    }

    private static IEnumerable<JsonElement> Items(JsonElement root, string name) =>
        root.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object)
            : [];

    private static string? Str(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long? Num(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n) ? n : null;
}
