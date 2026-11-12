using System.Text;
using Edgepad.Protocol;

namespace Edgepad.Power;

/// <summary>One device for TEXT BATTERIES. <paramref name="Kind"/> is a <see cref="BatteryKind"/> token. A device with no level is never built: the reader omits it.</summary>
internal sealed record BatteryEntry(string Kind, int Level, bool Charging, string Name);

/// <summary>Builds the payloads of TEXT BATTERIES and TEXT OUTPUT. The format lives in docs/PROTOCOL.md.</summary>
internal static class BatteryText
{
    private const int MaxBytes = 255;
    private const int NameCodePoints = 24;

    /// <summary>The whole list as one snapshot, laptop first. Entries are dropped from the end until it fits 255 bytes, so the laptop stays and none is cut in half.</summary>
    public static string Encode(IEnumerable<BatteryEntry> entries)
    {
        var parts = new List<string>();
        foreach (var e in entries)
        {
            var kind = BatteryKind.Of(e.Kind);
            var name = Sanitize(e.Name, NameCodePoints);
            parts.Add($"{kind}:{Math.Clamp(e.Level, 0, 100)}:{(e.Charging ? 1 : 0)}:{(name.Length == 0 ? kind : name)}");
        }

        var text = string.Join('|', parts);
        while (parts.Count > 1 && Encoding.UTF8.GetByteCount(text) > MaxBytes)
        {
            parts.RemoveAt(parts.Count - 1);
            text = string.Join('|', parts);
        }

        return text;
    }

    /// <summary>Control characters and "|" become a space, then trim, then cut to <paramref name="max"/> code points, never inside a surrogate pair.</summary>
    internal static string Sanitize(string name, int max)
    {
        var clean = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            clean.Append(char.IsControl(c) || c == '|' ? ' ' : c);
        }

        var kept = new StringBuilder();
        var count = 0;
        foreach (var rune in clean.ToString().Trim().EnumerateRunes())
        {
            if (count++ == max)
            {
                break;
            }

            kept.Append(rune.ToString());
        }

        return kept.ToString().Trim();
    }
}

/// <summary>Builds the payload of TEXT OUTPUT.</summary>
internal static class OutputText
{
    private const int NameCodePoints = 40;

    /// <summary>"kind/name", the name sanitised and cut to 40 code points.</summary>
    public static string Encode(string kind, string name) =>
        $"{OutputKind.Of(kind)}/{BatteryText.Sanitize(name, NameCodePoints)}";
}
