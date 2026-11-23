using System.Text.Json;
using Edgepad.Protocol;

namespace Edgepad.Dashboard;

/// <summary>One rate-limit window of a Codex rollout. <paramref name="ResetsAtSec"/> is epoch seconds, -1 unknown.</summary>
internal sealed record RolloutWindow(int Minutes, int Pct, long ResetsAtSec);

/// <summary>
/// What the end of a rollout says: working or idle (null when the tail holds no task_started, task_complete or turn_aborted), the
/// newest limits, what the first line says about the session, and how full its context window was at the last <c>token_count</c>
/// (the last request's tokens over <c>model_context_window</c>, 0..100, -1 when either is missing).
/// </summary>
internal sealed record RolloutTail(string? State, IReadOnlyList<RolloutWindow> Limits, string Cwd, string CliVersion, int ContextPct = -1);

/// <summary>The cumulative usage a rollout reports last, and the model it last ran. Input includes the cached part.</summary>
internal readonly record struct RolloutTotals(string? Model, long Input, long CachedInput, long Output)
{
    /// <summary>Input that was not served from the cache.</summary>
    public long Uncached => Math.Max(0, Input - CachedInput);

    /// <summary>Uncached input plus output; cached input is left out, as Plimsoll does for Claude.</summary>
    public long Tokens => Uncached + Output;
}

/// <summary>
/// Reads the parts of a Codex rollout (<c>rollout-*.jsonl</c>, one JSON object per line) the dashboard needs. It never throws on
/// content: a line that does not parse, is too deep, is too long or is cut short is skipped. Which files may be opened at all is
/// <see cref="CodexWatcher"/>'s rule, not this class's.
/// </summary>
internal static class CodexRollout
{
    /// <summary>How much of the end of a file is read for the state and the limits.</summary>
    public const int TailBytes = 256 * 1024;

    /// <summary>The first line holds session_meta; it is read only if it fits this.</summary>
    public const int HeadBytes = 64 * 1024;

    /// <summary>How much of the end of a file is read first for the newest total usage.</summary>
    public const int TotalsTailBytes = 1024 * 1024;

    private const int MaxLine = 1024 * 1024;
    private const int MaxMinutes = 525_600;
    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 16 };

    /// <summary>The state, the newest limits, the working folder (empty if the first line is too large to read) and the CLI version.</summary>
    public static RolloutTail ParseTail(Stream stream)
    {
        var scan = new TailScan();
        if (stream.CanSeek)
        {
            var head = new byte[HeadBytes];
            stream.Seek(0, SeekOrigin.Begin);
            var read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            var end = Array.IndexOf(head, (byte)'\n', 0, read);
            if (end > 0)
            {
                scan.Take(head.AsMemory(0, end));
            }

            var start = Math.Max(0, stream.Length - TailBytes);
            stream.Seek(start, SeekOrigin.Begin);
            ForEachLine(stream, dropFirst: start > 0, scan.Take);
        }
        else
        {
            ForEachLine(stream, dropFirst: false, scan.Take);
        }

        return scan.Result();
    }

    /// <summary>
    /// The last cumulative usage and the last model of the file. The last <see cref="TotalsTailBytes"/> are read first; the whole
    /// file is read only when they hold no total, or no model and <paramref name="knownModel"/> (the model this file was last seen
    /// with) is null.
    /// </summary>
    public static RolloutTotals Totals(Stream stream, string? knownModel = null)
    {
        if (stream.CanSeek && stream.Length > TotalsTailBytes)
        {
            var start = stream.Length - TotalsTailBytes;
            stream.Seek(start, SeekOrigin.Begin);
            var (tailModel, tailUsage) = ScanTotals(stream, dropFirst: true);

            // ponytail: a model switch before the tail but after the last full read is missed until the file is read whole again.
            if (tailUsage is { } found && (tailModel ?? knownModel) is { } model)
            {
                return found with { Model = model };
            }

            stream.Seek(0, SeekOrigin.Begin);
        }

        var (lastModel, usage) = ScanTotals(stream, dropFirst: false);
        return (usage ?? default) with { Model = lastModel };
    }

    private static (string? Model, RolloutTotals? Usage) ScanTotals(Stream stream, bool dropFirst)
    {
        string? model = null;
        RolloutTotals? usage = null;
        ForEachLine(stream, dropFirst, line =>
        {
            if (!Has(line, "turn_context"u8) && !Has(line, "total_token_usage"u8))
            {
                return;
            }

            using var doc = Parse(line);
            if (doc is null || !Payload(doc.RootElement, out var type, out var payload))
            {
                return;
            }

            if (type == "turn_context" && payload.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String)
            {
                model = Short(m.GetString());
            }
            else if (type == "token_count" && Child(payload, "info", out var info) && Child(info, "total_token_usage", out var total))
            {
                usage = new RolloutTotals(null, Count(total, "input_tokens"), Count(total, "cached_input_tokens"), Count(total, "output_tokens"));
            }
        });
        return (model, usage);
    }

    /// <summary>The window of a rate-limit object (<c>used_percent</c>, <c>window_minutes</c>, <c>resets_at</c>), or null when it does not read.</summary>
    internal static RolloutWindow? WindowOf(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object
            || !e.TryGetProperty("used_percent", out var used) || used.ValueKind != JsonValueKind.Number || !used.TryGetDouble(out var pct) || double.IsNaN(pct)
            || !e.TryGetProperty("window_minutes", out var window) || window.ValueKind != JsonValueKind.Number || !window.TryGetInt32(out var minutes)
            || minutes is < 1 or > MaxMinutes)
        {
            return null;
        }

        var reset = e.TryGetProperty("resets_at", out var at) && at.ValueKind == JsonValueKind.Number && at.TryGetInt64(out var seconds) && seconds >= 0 ? seconds : -1;
        return new RolloutWindow(minutes, (int)Math.Floor(Math.Clamp(pct, 0, 100) + 0.5), reset);
    }

    /// <summary>Calls <paramref name="onLine"/> for every complete line, with its newline removed. A line over 1 MiB is skipped without being held.</summary>
    private static void ForEachLine(Stream stream, bool dropFirst, Action<ReadOnlyMemory<byte>> onLine)
    {
        var buffer = new byte[16 * 1024];
        var line = new MemoryStream();
        var skipping = dropFirst;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            var at = 0;
            while (at < read)
            {
                var newline = Array.IndexOf(buffer, (byte)'\n', at, read - at);
                var end = newline < 0 ? read : newline;
                if (!skipping)
                {
                    if (line.Length + (end - at) > MaxLine)
                    {
                        skipping = true;
                        line.SetLength(0);
                    }
                    else
                    {
                        line.Write(buffer, at, end - at);
                    }
                }

                if (newline < 0)
                {
                    break;
                }

                if (!skipping && line.Length > 0)
                {
                    onLine(new ReadOnlyMemory<byte>(line.GetBuffer(), 0, (int)line.Length));
                }

                line.SetLength(0);
                skipping = false;
                at = newline + 1;
            }
        }

        // The last line has no newline yet when Codex is mid-write; it is usually cut short and then simply does not parse.
        if (!skipping && line.Length > 0)
        {
            onLine(new ReadOnlyMemory<byte>(line.GetBuffer(), 0, (int)line.Length));
        }
    }

    private static bool Has(ReadOnlyMemory<byte> line, ReadOnlySpan<byte> word) => line.Span.IndexOf(word) >= 0;

    private static JsonDocument? Parse(ReadOnlyMemory<byte> line)
    {
        try
        {
            return JsonDocument.Parse(line, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads <c>type</c> and <c>payload</c> of a line. For an event_msg the type returned is the payload's own type.</summary>
    private static bool Payload(JsonElement root, out string type, out JsonElement payload)
    {
        type = string.Empty;
        payload = default;
        if (root.ValueKind != JsonValueKind.Object || !Str(root, "type", out var outer) || !Child(root, "payload", out payload))
        {
            return false;
        }

        type = outer;
        return outer != "event_msg" || Str(payload, "type", out type);
    }

    private static bool Child(JsonElement parent, string name, out JsonElement child)
    {
        child = default;
        return parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out child) && child.ValueKind == JsonValueKind.Object;
    }

    private static bool Str(JsonElement parent, string name, out string value)
    {
        value = string.Empty;
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var e) || e.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = e.GetString() ?? string.Empty;
        return true;
    }

    private static long Count(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out var n) && n > 0 ? n : 0;

    private static string? Short(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Length > 100 ? text[..100] : text;

    private sealed class TailScan
    {
        private string? state;
        private string cwd = string.Empty;
        private string version = string.Empty;
        private int context = -1;
        private IReadOnlyList<RolloutWindow>? preferred;
        private IReadOnlyList<RolloutWindow>? other;

        public void Take(ReadOnlyMemory<byte> line)
        {
            if (!Has(line, "event_msg"u8) && !Has(line, "session_meta"u8))
            {
                return;
            }

            using var doc = Parse(line);
            if (doc is null || !Payload(doc.RootElement, out var type, out var payload))
            {
                return;
            }

            switch (type)
            {
                case "task_started":
                    state = SessionStateToken.Busy;
                    break;
                case "task_complete" or "turn_aborted":
                    state = SessionStateToken.Idle;
                    break;
                case "session_meta":
                    cwd = Str(payload, "cwd", out var folder) ? folder : cwd;
                    version = Str(payload, "cli_version", out var v) ? v : version;
                    break;
                case "token_count":
                    TakeLimits(payload);
                    TakeContext(payload);
                    break;
            }
        }

        public RolloutTail Result() =>
            new(state, preferred ?? other ?? [], cwd.Length > 4096 ? string.Empty : cwd, version.Length > 64 ? string.Empty : version, context);

        // A token_count with no info (Codex sends one right after the limits change) leaves the last fill as it was.
        private void TakeContext(JsonElement payload)
        {
            if (Child(payload, "info", out var info) && Child(info, "last_token_usage", out var last)
                && info.TryGetProperty("model_context_window", out var window) && window.ValueKind == JsonValueKind.Number
                && window.TryGetInt64(out var size) && size > 0)
            {
                var used = Count(last, "total_tokens");
                context = (int)Math.Min(100, (used * 100) / size);
            }
        }

        private void TakeLimits(JsonElement payload)
        {
            if (!Child(payload, "rate_limits", out var limits) || !limits.TryGetProperty("primary", out var primary) || WindowOf(primary) is not { } first)
            {
                return;
            }

            var windows = new List<RolloutWindow> { first };
            if (limits.TryGetProperty("secondary", out var secondary) && WindowOf(secondary) is { } second && second.Minutes != first.Minutes)
            {
                windows.Add(second);
            }

            if (Str(limits, "limit_id", out var id) && id == "codex")
            {
                preferred = windows;
            }
            else
            {
                other = windows;
            }
        }
    }
}
