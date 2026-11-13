using System.Globalization;

namespace Edgepad.Ui;

/// <summary>
/// What a code or PIN box keeps of what was typed or pasted. Normalise first, then limit: a paste of " 482 913",
/// "482-913" or full-width "４８２９１３" has to arrive whole before it is cut to length, so the boxes' own MaxLength
/// is <see cref="PasteLimit"/>, wide enough for any such paste, and the real length is applied here.
/// </summary>
internal static class DigitInput
{
    /// <summary>The TextBox.MaxLength for a box fed by <see cref="Apply"/>.</summary>
    public const int PasteLimit = 32;

    /// <summary>
    /// The decimal digits of <paramref name="typed"/> in any script, as ASCII, at most <paramref name="max"/>;
    /// spaces, dashes and every other character are dropped.
    /// </summary>
    public static string Normalize(string typed, int max)
    {
        var digits = new string([.. typed.Where(char.IsDigit).Select(d => (char)('0' + CharUnicodeInfo.GetDecimalDigitValue(d)))]);
        return digits.Length > max ? digits[..max] : digits;
    }

    /// <summary>
    /// Rewrites <paramref name="box"/> to its normalised digits when they differ, keeping the caret after the digits
    /// that were before it, so typing carries on where the paste ended. Returns the digits.
    /// </summary>
    public static string Apply(TextBox box, int max)
    {
        var digits = Normalize(box.Text, max);
        if (digits != box.Text)
        {
            var caret = Normalize(box.Text[..Math.Min(box.SelectionStart, box.Text.Length)], max).Length;
            box.Text = digits;
            box.SelectionStart = caret;
        }

        return digits;
    }
}
