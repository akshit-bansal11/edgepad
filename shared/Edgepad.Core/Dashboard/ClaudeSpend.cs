// Adapted from Plimsoll by Akshit Bansal, MIT (scripts/scan.mjs, scripts/prices.mjs). Added: IsPriced.
using System.Globalization;
using System.Text.Json;

namespace Edgepad.Dashboard;

/// <summary>One counted Claude response: its local day (yyyy-MM-dd), input + output tokens, estimated dollars and whether its model has a price.</summary>
internal sealed record ClaudeRow(string Day, double Tokens, double Usd, bool Priced);

/// <summary>The first local day (yyyy-MM-dd) of today and of the last 7 and 30 days, today included in each; <see cref="From"/> is the earliest of them.</summary>
internal readonly record struct ClaudePeriods(string Today, string Start7, string Start30, string From);

/// <summary>What the responses of one local day added up to once their ids were no longer needed: tokens, dollars and whether an unpriced model had tokens.</summary>
internal sealed record ClaudeDay(double Tokens, double Usd, bool Unpriced);

/// <summary>
/// Anthropic API list prices in US dollars per million tokens, as of 2026-10-20 (Plimsoll's table). Used to ESTIMATE cost from
/// transcript tokens: a subscription is not billed this way. Update the table when prices change.
/// </summary>
internal static class ClaudePrices
{
    private const double Write5m = 1.25;
    private const double Write1h = 2;

    // Claude Haiku 5.5 charges 5x every price for a request whose prompt (input plus cache reads and writes) is over 100,000 tokens.
    private const string LongPromptPrefix = "claude-haiku-5-5";
    private const double LongPromptOver = 100_000;
    private const double LongPromptTimes = 5;

    // Model id prefix, input $/MTok, output $/MTok, cache-read multiplier of input. A longer prefix comes before a shorter one it
    // starts with (claude-opus-5-5 before claude-opus-5); the first match wins.
    private static readonly (string Prefix, double Input, double Output, double Read)[] Table =
    [
        ("claude-fable-5-1", 10, 50, 0.025),
        ("claude-mythos-5-1", 10, 50, 0.025),
        ("claude-fable-5", 10, 50, 0.1),
        ("claude-mythos-5", 10, 50, 0.1),
        ("claude-opus-5-5", 4, 20, 0.05),
        ("claude-opus-5", 5, 25, 0.1),
        ("claude-opus-4-8", 5, 25, 0.1),
        ("claude-opus-4-7", 5, 25, 0.1),
        ("claude-opus-4-6", 5, 25, 0.1),
        ("claude-opus-4-5", 5, 25, 0.1),
        ("claude-opus-4-1", 15, 75, 0.1),
        ("claude-opus-4", 15, 75, 0.1),
        ("claude-sonnet-5-5", 2, 10, 0.05),
        ("claude-sonnet-5", 2, 10, 0.1),
        ("claude-sonnet-4-6", 3, 15, 0.1),
        ("claude-sonnet-4-5", 3, 15, 0.1),
        ("claude-sonnet-4", 3, 15, 0.1),
        ("claude-haiku-5-5", 0.1, 0.5, 0.1),
        ("claude-haiku-4-5", 1, 5, 0.1),
        ("claude-3-5-haiku", 0.8, 4, 0.1),
    ];

    /// <summary>False for a model the table does not know: its tokens count, its dollars do not.</summary>
    public static bool IsPriced(string? model) => RowOf(KeyOf(model)) is not null;

    /// <summary>The estimated dollars of one response's <c>usage</c> object; 0 for an unknown model rather than a guessed price.</summary>
    public static double Usd(string? model, JsonElement usage)
    {
        var key = KeyOf(model);
        if (RowOf(key) is not { } row)
        {
            return 0;
        }

        var input = ClaudeSpend.Num(usage, "input_tokens");
        var read = ClaudeSpend.Num(usage, "cache_read_input_tokens");
        var written = ClaudeSpend.Num(usage, "cache_creation_input_tokens");

        // cache_creation splits the write by lifetime when present; without it every written token is 5-minute.
        var write1h = usage.TryGetProperty("cache_creation", out var split) && split.ValueKind == JsonValueKind.Object ? ClaudeSpend.Num(split, "ephemeral_1h_input_tokens") : 0;
        var write5m = Math.Max(0, written - write1h);
        var usd = ((input * row.Input) + (ClaudeSpend.Num(usage, "output_tokens") * row.Output) + (read * row.Input * row.Read)
            + (write5m * row.Input * Write5m) + (write1h * row.Input * Write1h)) / 1e6;
        return key.StartsWith(LongPromptPrefix, StringComparison.Ordinal) && input + read + written > LongPromptOver ? usd * LongPromptTimes : usd;
    }

    // Bedrock (us.anthropic.claude-opus-4-1-...) and Vertex (claude-sonnet-4-5@...) ids carry a prefix.
    private static string KeyOf(string? model)
    {
        var id = model ?? string.Empty;
        var at = id.IndexOf("claude-", StringComparison.Ordinal);
        return at > 0 ? id[at..] : id;
    }

    private static (string Prefix, double Input, double Output, double Read)? RowOf(string key)
    {
        foreach (var row in Table)
        {
            if (key.StartsWith(row.Prefix, StringComparison.Ordinal))
            {
                return row;
            }
        }

        return null;
    }
}

/// <summary>
/// Sums Claude Code's transcripts the way Plimsoll does: responses by message id (a response is written once per content block, its
/// usage final on the last, so the last row of an id is kept), days in local time, the week the last 7 days. Tokens are input + output;
/// cache reads and writes are priced, not counted as tokens. A line over <see cref="MaxLine"/> bytes is skipped unread.
/// </summary>
internal static class ClaudeSpend
{
    public const int MaxLine = 4 * 1024 * 1024;

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 64 };

    /// <summary>The periods of the local day <paramref name="localNow"/> falls in: today, the last 7 days and the last 30, each counting today.</summary>
    public static ClaudePeriods Periods(DateTime localNow)
    {
        var today = DateOnly.FromDateTime(localNow);
        var start30 = DayOf(today.AddDays(-29));
        return new ClaudePeriods(DayOf(today), DayOf(today.AddDays(-6)), start30, start30);
    }

    /// <summary>Adds responses to per-day totals (a new dictionary when <paramref name="into"/> is null), so their ids can be forgotten.</summary>
    public static Dictionary<string, ClaudeDay> Fold(IEnumerable<ClaudeRow> rows, Dictionary<string, ClaudeDay>? into = null)
    {
        var days = into ?? new Dictionary<string, ClaudeDay>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            Merge(days, row.Day, new ClaudeDay(row.Tokens, row.Usd, !row.Priced && row.Tokens > 0));
        }

        return days;
    }

    /// <summary>Adds one day's totals into <paramref name="into"/>.</summary>
    public static void Merge(Dictionary<string, ClaudeDay> into, string day, ClaudeDay add) =>
        into[day] = into.TryGetValue(day, out var have) ? new ClaudeDay(have.Tokens + add.Tokens, have.Usd + add.Usd, have.Unpriced || add.Unpriced) : add;

    /// <summary>One transcript's responses on or after <paramref name="from"/>, by message id.</summary>
    public static Dictionary<string, ClaudeRow> SumFile(Stream stream, string from)
    {
        var rows = new Dictionary<string, ClaudeRow>(StringComparer.Ordinal);
        var buffer = new byte[64 * 1024];
        using var line = new MemoryStream();
        var skipping = false;
        int n;
        while ((n = stream.Read(buffer)) > 0)
        {
            var span = buffer.AsSpan(0, n);
            while (true)
            {
                var nl = span.IndexOf((byte)'\n');
                var part = nl < 0 ? span : span[..nl];
                if (!skipping && line.Length + part.Length > MaxLine)
                {
                    skipping = true;
                    line.SetLength(0);
                }

                if (!skipping)
                {
                    line.Write(part);
                }

                if (nl < 0)
                {
                    break;
                }

                if (!skipping)
                {
                    Read(line.GetBuffer().AsMemory(0, (int)line.Length), from, rows);
                }

                line.SetLength(0);
                skipping = false;
                span = span[(nl + 1)..];
            }
        }

        if (!skipping && line.Length > 0)
        {
            Read(line.GetBuffer().AsMemory(0, (int)line.Length), from, rows);
        }

        return rows;
    }

    /// <summary>
    /// Today, the last 7 days, the last 30 days and all time, from responses already deduplicated by id plus the per-day totals of
    /// the ones whose ids were let go; true when tokens of an unpriced model counted.
    /// </summary>
    public static (SpendTotals Totals, bool Unpriced) Total(IEnumerable<ClaudeRow> rows, IEnumerable<KeyValuePair<string, ClaudeDay>> older, ClaudePeriods periods)
    {
        double tokDay = 0, usdDay = 0, tokWeek = 0, usdWeek = 0, tokMonth = 0, usdMonth = 0, tokAll = 0, usdAll = 0;
        var unpriced = false;

        void Add(string day, double tokens, double usd, bool noPrice)
        {
            tokAll += tokens;
            usdAll += usd;
            unpriced |= noPrice;
            if (string.CompareOrdinal(day, periods.Start30) < 0)
            {
                return;
            }

            tokMonth += tokens;
            usdMonth += usd;
            if (string.CompareOrdinal(day, periods.Start7) >= 0)
            {
                tokWeek += tokens;
                usdWeek += usd;
            }

            if (day == periods.Today)
            {
                tokDay += tokens;
                usdDay += usd;
            }
        }

        foreach (var row in rows)
        {
            Add(row.Day, row.Tokens, row.Usd, !row.Priced && row.Tokens > 0);
        }

        foreach (var (day, total) in older)
        {
            Add(day, total.Tokens, total.Usd, total.Unpriced);
        }

        return (new SpendTotals(Round(tokDay), Round(usdDay * 100), Round(tokWeek), Round(usdWeek * 100), Round(tokMonth), Round(usdMonth * 100), TokAll: Round(tokAll), CentsAll: Round(usdAll * 100)), unpriced);
    }

    /// <summary>A usage field as a count: a string, a negative or anything odd counts as nothing, never NaN.</summary>
    public static double Num(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var d) && double.IsFinite(d) && d > 0 ? d : 0;

    private static void Read(ReadOnlyMemory<byte> text, string from, Dictionary<string, ClaudeRow> rows)
    {
        if (text.Span.IndexOf("\"usage\""u8) < 0)
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(text, Options);
            var row = doc.RootElement;
            if (row.ValueKind != JsonValueKind.Object || Str(row, "type") != "assistant"
                || !row.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object
                || !message.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var model = Str(message, "model");
            if (model == "<synthetic>"
                || Str(row, "timestamp") is not { } stamp
                || !DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
                || (Str(message, "id") ?? Str(row, "uuid")) is not { } id)
            {
                return;
            }

            var day = DayOf(DateOnly.FromDateTime(at.ToLocalTime().DateTime));
            if (string.CompareOrdinal(day, from) < 0)
            {
                return;
            }

            var usd = ClaudePrices.Usd(model, usage);
            rows[id] = new ClaudeRow(day, Num(usage, "input_tokens") + Num(usage, "output_tokens"), double.IsFinite(usd) && usd > 0 ? usd : 0, ClaudePrices.IsPriced(model));
        }
        catch (Exception e) when (e is JsonException or ArgumentException)
        {
            // a torn or odd line counts as nothing
        }
    }

    private static string? Str(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string DayOf(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static long Round(double value) => (long)Math.Round(Math.Min(value, long.MaxValue / 2.0), MidpointRounding.AwayFromZero);
}
