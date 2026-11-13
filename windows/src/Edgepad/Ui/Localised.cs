using System.Globalization;

namespace Edgepad.Ui;

internal static class Localised
{
    /// <summary>
    /// A resource string with its {0}, {1} placeholders filled, in the language and number format the UI is in. .NET
    /// has no plural rules, so a string never builds "N things" around a count: it reads "Things: N" instead.
    /// </summary>
    public static string Fill(this string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, format, args);
}
