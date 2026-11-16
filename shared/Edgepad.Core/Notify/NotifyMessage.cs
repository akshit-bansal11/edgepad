using System;
using System.Text;
using Edgepad.Protocol;

namespace Edgepad.Notify;

/// <summary>
/// The bytes of one phone notification: <c>app\ntitle\nbody</c> in UTF-8. Each field is cut to its code point cap
/// and every control character (including the newline that separates the fields) becomes a space, so a field can
/// never add a line. The whole is kept under <see cref="MaxBytes"/> by cutting the body last. The Kotlin codec on the
/// phone does the same, so both sides send the same bytes for the same text.
/// </summary>
public static class NotifyMessage
{
    public const int MaxAppCodePoints = 40;
    public const int MaxTitleCodePoints = 60;
    public const int MaxBodyCodePoints = 200;

    /// <summary>The total is kept strictly below this, in UTF-8 bytes.</summary>
    public const int MaxBytes = 1024;

    /// <summary>Null when the title and the body are both empty after folding: there is nothing to show.</summary>
    public static byte[]? Encode(string app, string title, string body)
    {
        var appText = Fold(app, MaxAppCodePoints);
        var titleText = Fold(title, MaxTitleCodePoints);
        var bodyText = Fold(body, MaxBodyCodePoints);
        if (titleText.Length == 0 && bodyText.Length == 0)
        {
            return null;
        }

        // Two separators, then the body gets whatever bytes the other two fields leave under the total.
        var header = Encoding.UTF8.GetByteCount(appText) + Encoding.UTF8.GetByteCount(titleText) + 2;
        bodyText = FrameCodec.Truncate(bodyText, MaxBytes - 1 - header);
        return Encoding.UTF8.GetBytes($"{appText}\n{titleText}\n{bodyText}");
    }

    /// <summary>The first <paramref name="maxCodePoints"/> code points, with control characters as spaces.</summary>
    private static string Fold(string value, int maxCodePoints)
    {
        var text = new StringBuilder();
        var count = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (count++ == maxCodePoints)
            {
                break;
            }

            text.Append(Rune.IsControl(rune) ? new Rune(' ') : rune);
        }

        return text.ToString();
    }
}
