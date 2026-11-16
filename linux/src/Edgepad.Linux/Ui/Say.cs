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
}
