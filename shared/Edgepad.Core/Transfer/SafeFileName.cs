using System.Globalization;
using System.Text;
using static System.FormattableString;

namespace Edgepad.Transfer;

/// <summary>
/// Turns a name a phone sent into a file name this laptop will create, or refuses it. The pairs in
/// protocol/filenames.txt pin it, and the Kotlin and Swift sanitisers read the same file.
/// </summary>
internal static class SafeFileName
{
    private const string Invalid = "<>:\"/\\|?*";

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// The last path segment, with control and invisible format (Cf) characters dropped and <c>&lt; &gt; : " / \ | ? *</c> replaced by an underscore,
    /// trailing dots and spaces trimmed, cut to <see cref="TransferLimits.MaxNameBytes"/> UTF-8 bytes keeping the
    /// extension. Null when nothing usable is left, or when the name is a Windows device name with or without an extension.
    /// </summary>
    public static string? Clean(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        var cut = raw.LastIndexOfAny(['/', '\\']);
        var segment = cut >= 0 ? raw[(cut + 1)..] : raw;
        var text = new StringBuilder(segment.Length);
        foreach (var ch in segment)
        {
            if (!char.IsControl(ch) && char.GetUnicodeCategory(ch) != UnicodeCategory.Format)
            {
                text.Append(Invalid.Contains(ch, StringComparison.Ordinal) ? '_' : ch);
            }
        }

        var name = CutToBytes(text.ToString().TrimEnd('.', ' ')).TrimEnd('.', ' ');
        if (name.Length == 0)
        {
            return null;
        }

        var firstDot = name.IndexOf('.', StringComparison.Ordinal);
        var stem = (firstDot >= 0 ? name[..firstDot] : name).TrimEnd(' ');
        return Reserved.Contains(stem) ? null : name;
    }

    /// <summary>
    /// <paramref name="name"/> if nothing by that name exists, otherwise <c>name (1).ext</c>, <c>name (2).ext</c> and so on:
    /// a transfer never overwrites.
    /// </summary>
    public static string Unique(string name, Func<string, bool> exists)
    {
        if (!exists(name))
        {
            return name;
        }

        var dot = name.LastIndexOf('.');
        var stem = dot > 0 ? name[..dot] : name;
        var extension = dot > 0 ? name[dot..] : "";
        for (var n = 1; ; n++)
        {
            var candidate = Invariant($"{stem} ({n}){extension}");
            if (!exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static string CutToBytes(string name)
    {
        if (Encoding.UTF8.GetByteCount(name) <= TransferLimits.MaxNameBytes)
        {
            return name;
        }

        var dot = name.LastIndexOf('.');
        var extension = dot > 0 ? name[dot..] : "";
        if (Encoding.UTF8.GetByteCount(extension) > TransferLimits.MaxExtensionBytes)
        {
            extension = "";
        }

        var budget = TransferLimits.MaxNameBytes - Encoding.UTF8.GetByteCount(extension);
        var kept = new StringBuilder();
        foreach (var rune in name[..(name.Length - extension.Length)].EnumerateRunes())
        {
            budget -= rune.Utf8SequenceLength;
            if (budget < 0)
            {
                break;
            }

            kept.Append(rune.ToString());
        }

        return kept.ToString().TrimEnd('.', ' ') + extension;
    }
}
