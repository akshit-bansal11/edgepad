using System.Globalization;
using System.Text;
using System.Text.Json;
using Edgepad.Dashboard;
using Edgepad.Protocol;

namespace Edgepad.Hooks;

/// <summary>A request line of the local hook channel, once read. The agent always came from the plugin's argv, never the payload.</summary>
internal abstract record HookRequest(string Token, string Agent);

/// <summary>
/// The tool fields a permission request may carry, as received (the hook client already whitelisted them). <paramref name="CommandSafe"/>
/// is false when <paramref name="Command"/> was a list (Codex's array form) with a control character inside one element: joined
/// with spaces it would not show what runs, so the broker offers Deny only.
/// </summary>
internal sealed record ToolInput(string? Command, string? FilePath, string? Url, bool CommandSafe = true);

/// <summary>A PermissionRequest: <paramref name="Tool"/> is 1..24 of A-Za-z0-9_.-.</summary>
internal sealed record PermRequest(string Token, string Agent, string Session, string Cwd, string Tool, ToolInput Input) : HookRequest(Token, Agent);

/// <summary>A lifecycle hook. <paramref name="NotificationType"/> only for a Notification.</summary>
internal sealed record SessionRequest(string Token, string Agent, string Session, string Event, string Cwd, string? NotificationType) : HookRequest(Token, Agent);

/// <summary>Limits as the agent reported them (Claude's <c>kind</c> or Codex minutes) and the context fill, for <see cref="LimitsBoard"/>.</summary>
internal sealed record UsageRequest(string Token, string Agent, IReadOnlyList<RawLimit> Windows, double? ContextPct) : HookRequest(Token, Agent);

/// <summary>Token and cent totals from the spend scanner, for <see cref="SpendBoard"/>.</summary>
internal sealed record SpendRequest(string Token, string Agent, SpendTotals Totals) : HookRequest(Token, Agent);

/// <summary>
/// The wire of the local hook channel: one request line of JSON <c>{"v":1,"token","agent","kind","d"}</c> and one reply line.
/// Strict: a wrong type, a missing field, an unknown agent or kind, a tool name outside its charset, depth over 8 or a line over
/// 64 KiB is no request at all. Strings are capped here; text is cleaned later, where it is shown. protocol/hook-vectors.txt
/// holds the cases the C# and Swift hook clients check too.
/// </summary>
internal static class HookProtocol
{
    public const int MaxLineBytes = 64 * 1024;
    public const int MaxDepth = 8;
    public const int MaxToolLength = 24;
    public const int MaxSessionLength = SessionBoard.MaxIdLength;
    public const int MaxCwdLength = 1024;
    public const int MaxTextLength = 16 * 1024;
    public const int MaxNameLength = 64;
    public const int MaxWindows = 16;
    public const int MaxCommandParts = 1024;

    public const string Perm = "perm";
    public const string Session = "session";
    public const string Usage = "usage";
    public const string Spend = "spend";

    public const string Allow = "allow";
    public const string Deny = "deny";
    public const string Defer = "defer";

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = MaxDepth };

    /// <summary>The reply line to a permission request, line feed included.</summary>
    public static byte[] DecisionReply(string decision) => Encoding.UTF8.GetBytes($$"""{"v":1,"decision":"{{decision}}"}""" + "\n");

    /// <summary>The reply line to every other kind, line feed included.</summary>
    public static byte[] OkReply() => Encoding.UTF8.GetBytes("""{"v":1,"ok":true}""" + "\n");

    /// <summary>The request in <paramref name="line"/> (no line feed), or null when it is not exactly one.</summary>
    public static HookRequest? TryParse(ReadOnlySpan<byte> line)
    {
        if (line.Length is 0 or > MaxLineBytes)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(line.ToArray(), Options);
            return Read(doc.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            // Invalid UTF-8 surfaces as an ArgumentException from some readers.
            return null;
        }
    }

    private static HookRequest? Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("v", out var v) || v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var version) || version != 1
            || Str(root, "token", 256) is not { Length: > 0 } token
            || Str(root, "agent", 16) is not { } agent || !DashWire.IsAgent(agent)
            || Str(root, "kind", 16) is not { } kind
            || !root.TryGetProperty("d", out var d) || d.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return kind switch
        {
            Perm => ReadPerm(token, agent, d),
            Session => ReadSession(token, agent, d),
            Usage => ReadUsage(token, agent, d),
            Spend => ReadSpend(token, agent, d),
            _ => null,
        };
    }

    private static PermRequest? ReadPerm(string token, string agent, JsonElement d)
    {
        if (Str(d, "tool_name", MaxNameLength) is not { Length: > 0 and <= MaxToolLength } tool || !tool.All(IsToolChar)
            || OptStr(d, "session_id", MaxSessionLength, out var session) is false
            || OptStr(d, "cwd", MaxCwdLength, out var cwd) is false)
        {
            return null;
        }

        var input = new ToolInput(null, null, null);
        if (d.TryGetProperty("tool_input", out var raw))
        {
            if (raw.ValueKind != JsonValueKind.Object
                || OptStr(raw, "file_path", MaxTextLength, out var file) is false
                || OptStr(raw, "url", MaxTextLength, out var url) is false
                || ReadCommand(raw, out var command, out var safe) is false)
            {
                return null;
            }

            input = new ToolInput(command, file, url, safe);
        }

        return new PermRequest(token, agent, session ?? string.Empty, cwd ?? string.Empty, tool, input);
    }

    // A string, or (Codex's array form) a list of strings joined with spaces. False on anything else.
    private static bool ReadCommand(JsonElement input, out string? command, out bool safe)
    {
        command = null;
        safe = true;
        if (!input.TryGetProperty("command", out var value))
        {
            return true;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            command = Cut(value.GetString()!, MaxTextLength);
            return true;
        }

        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() is 0 or > MaxCommandParts)
        {
            return false;
        }

        var parts = new List<string>();
        foreach (var part in value.EnumerateArray())
        {
            if (part.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var text = part.GetString()!;
            safe &= !text.Any(char.IsControl);
            parts.Add(text);
        }

        command = Cut(string.Join(' ', parts), MaxTextLength);
        return true;
    }

    private static SessionRequest? ReadSession(string token, string agent, JsonElement d)
    {
        if (Str(d, "hook_event_name", MaxNameLength) is not { Length: > 0 } name
            || OptStr(d, "session_id", MaxSessionLength, out var session) is false
            || OptStr(d, "cwd", MaxCwdLength, out var cwd) is false
            || OptStr(d, "notification_type", MaxNameLength, out var type) is false)
        {
            return null;
        }

        return new SessionRequest(token, agent, session ?? string.Empty, name, cwd ?? string.Empty, name == "Notification" ? type : null);
    }

    private static UsageRequest? ReadUsage(string token, string agent, JsonElement d)
    {
        var windows = new List<RawLimit>();
        if (d.TryGetProperty("limits", out var limits))
        {
            if (limits.ValueKind != JsonValueKind.Array || limits.GetArrayLength() > MaxWindows)
            {
                return null;
            }

            foreach (var limit in limits.EnumerateArray())
            {
                if (limit.ValueKind != JsonValueKind.Object
                    || Str(limit, "kind", MaxNameLength) is not { Length: > 0 } kind
                    || !limit.TryGetProperty("percentUsed", out var pct) || pct.ValueKind != JsonValueKind.Number
                    || !pct.TryGetDouble(out var percent) || double.IsNaN(percent) || double.IsInfinity(percent)
                    || !TryResetsAt(limit, out var resets))
                {
                    return null;
                }

                windows.Add(new RawLimit(kind, percent, resets));
            }
        }

        double? context = null;
        if (d.TryGetProperty("context", out var ctx) && ctx.ValueKind != JsonValueKind.Null)
        {
            var number = ctx.ValueKind == JsonValueKind.Object && ctx.TryGetProperty("percent", out var p) ? p : ctx;
            if (number.ValueKind == JsonValueKind.Null)
            {
                return new UsageRequest(token, agent, windows, null);
            }

            if (number.ValueKind != JsonValueKind.Number || !number.TryGetDouble(out var value) || double.IsNaN(value) || double.IsInfinity(value))
            {
                return null;
            }

            context = value;
        }

        return new UsageRequest(token, agent, windows, context);
    }

    // resetsAt is an ISO date, epoch seconds or milliseconds (as a number or text), null or absent.
    private static bool TryResetsAt(JsonElement limit, out string? resets)
    {
        resets = null;
        if (!limit.TryGetProperty("resetsAt", out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString()!;
            resets = text.Length <= MaxNameLength ? text : null;
            return text.Length <= MaxNameLength;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) && number >= 0)
        {
            resets = number.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        return false;
    }

    private static SpendRequest? ReadSpend(string token, string agent, JsonElement d)
    {
        if (!Period(d, "today", out var tokToday, out var centsToday)
            || !Period(d, "week", out var tokWeek, out var centsWeek)
            || !Period(d, "month", out var tokMonth, out var centsMonth))
        {
            return null;
        }

        if (d.TryGetProperty("unpriced", out var unpriced) && unpriced.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return null;
        }

        return new SpendRequest(token, agent, new SpendTotals(tokToday, centsToday, tokWeek, centsWeek, tokMonth, centsMonth));
    }

    private static bool Period(JsonElement d, string name, out long tok, out long cents)
    {
        tok = 0;
        cents = 0;
        return d.TryGetProperty(name, out var period) && period.ValueKind == JsonValueKind.Object
            && period.TryGetProperty("tok", out var t) && t.ValueKind == JsonValueKind.Number && t.TryGetInt64(out tok)
            && period.TryGetProperty("cents", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt64(out cents);
    }

    // A required string, cut to max UTF-16 units (never inside a surrogate pair); null when absent or not a string.
    private static string? Str(JsonElement obj, string name, int max) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? Cut(value.GetString()!, max) : null;

    // An optional string: true with the value (or null when absent or JSON null), false when present with another type.
    private static bool OptStr(JsonElement obj, string name, int max, out string? text)
    {
        text = null;
        if (!obj.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        text = Cut(value.GetString()!, max);
        return true;
    }

    private static string Cut(string text, int max) =>
        text.Length <= max ? text : text[..(char.IsHighSurrogate(text[max - 1]) ? max - 1 : max)];

    private static bool IsToolChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-';
}
