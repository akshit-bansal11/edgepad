using System.Globalization;

namespace Edgepad.Ui;

/// <summary>
/// Fills the {0} placeholders of a localised string in the desktop's culture. The format arrives as a parameter, so
/// the string is parsed per call: these run on a click or a notice, never in a loop (CA1863 asks for a cached
/// CompositeFormat only where the format is a known constant).
/// </summary>
internal static class Say
{
    public static string Format(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    /// <summary>
    /// A file name cut in the middle with an ellipsis so the extension stays visible (DES-D29): at 16 characters,
    /// "quarterly-report-final.pdf" becomes "quarterly...al.pdf" with a real ellipsis. A name that fits is returned as it
    /// is; one with no short extension is cut at the end.
    /// </summary>
    public static string MiddleTruncate(string name, int max = 36)
    {
        if (name.Length <= max)
        {
            return name;
        }

        var extension = Path.GetExtension(name);
        var tail = extension.Length is > 0 and <= 8 ? extension.Length + 2 : 0;
        var head = max - 1 - tail;
        if (head > 0 && char.IsHighSurrogate(name[head - 1]))
        {
            head--;
        }

        return tail == 0 ? name[..head] + "…" : name[..head] + "…" + name[^tail..];
    }
}
