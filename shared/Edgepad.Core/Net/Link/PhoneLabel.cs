using System.Text;

namespace Edgepad.Net;

/// <summary>
/// What the laptop calls a Wi-Fi or USB phone, the twin of the Mac's <c>PhoneLabel</c>. The name is the phone's own
/// word, sent before anything is proved, so it can hold newlines, bidi overrides, hundreds of characters, or read
/// like part of a window; it is cleaned before it is shown or logged. Counted in runes, never chars, and combining
/// marks are dropped, since one letter can carry any number of them.
/// </summary>
internal static class PhoneLabel
{
    public const int MaxNameLength = 40;

    /// <summary>A name cannot legitimately be longer than its field on the wire, so nothing past this is looked at.</summary>
    public const int MaxInputRunes = 256;

    /// <summary>What a phone is called when nothing usable is left of its name.</summary>
    public const string Unnamed = "A phone";

    private const string AllowedPunctuation = "'-_.()";

    /// <summary>Letters, digits, single spaces and a little punctuation, at most 40 runes; empty when nothing usable is left.</summary>
    public static string Clean(string? name)
    {
        var kept = new StringBuilder();
        foreach (var rune in (name ?? "").EnumerateRunes().Take(MaxInputRunes))
        {
            if (Rune.IsLetter(rune) || Rune.IsDigit(rune) || rune.Value == ' '
                || (rune.IsAscii && AllowedPunctuation.Contains((char)rune.Value, StringComparison.Ordinal)))
            {
                _ = kept.Append(rune.ToString());
            }
        }

        var collapsed = string.Join(' ', kept.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var cut = new StringBuilder();
        foreach (var rune in collapsed.EnumerateRunes().Take(MaxNameLength))
        {
            _ = cut.Append(rune.ToString());
        }

        return cut.ToString().Trim();
    }

    /// <summary><see cref="Clean"/>, or <see cref="Unnamed"/> when that leaves nothing.</summary>
    public static string Shown(string? name) => Clean(name) is { Length: > 0 } shown ? shown : Unnamed;
}
