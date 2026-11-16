namespace Edgepad.Platform;

/// <summary>
/// The language the shared core's strings (CoreStrings) are shown in, read from the POSIX locale. The app runs with
/// invariant globalization (no libicu needed), so .NET never learns the locale itself; this names the culture by hand,
/// and the project's PredefinedCulturesOnly=false lets that name be created. Only the UI language moves: numbers keep
/// the invariant format.
/// </summary>
internal static class Language
{
    /// <summary>The languages CoreStrings has a translation for, as their satellite assemblies are named.</summary>
    private static readonly string[] Translated = ["ar", "de", "es", "fr", "hi", "it", "ja", "ko", "nl", "pl", "pt-BR", "ru", "tr", "zh-Hans"];

    /// <summary>The locale variables in POSIX's order of precedence for messages.</summary>
    private static readonly string[] Variables = ["LC_ALL", "LC_MESSAGES", "LANG"];

    /// <summary>
    /// The effective locale as POSIX picks it for messages, the first set of LC_ALL, LC_MESSAGES and LANG, turned into
    /// a translated language; null for English or anything untranslated.
    /// </summary>
    public static string? FromEnvironment(Func<string, string?> variable)
    {
        return FromLocale(Variables.Select(variable).FirstOrDefault(v => !string.IsNullOrEmpty(v)));
    }

    /// <summary>
    /// <c>de_DE.UTF-8</c> -> <c>de</c>, <c>zh_CN</c> -> <c>zh-Hans</c>, any <c>pt</c> -> <c>pt-BR</c>; otherwise
    /// <c>xx-YY</c> if translated, then <c>xx</c>. <c>C</c>, <c>POSIX</c>, empty and untranslated are null.
    /// </summary>
    public static string? FromLocale(string? locale)
    {
        if (string.IsNullOrEmpty(locale))
        {
            return null;
        }

        var name = locale.Split('.', '@')[0];
        if (name is "" or "C" or "POSIX")
        {
            return null;
        }

        var parts = name.Split('_', '-');
        var language = parts[0].ToLowerInvariant();
        var region = parts.Length > 1 ? parts[1].ToUpperInvariant() : null;
        if (language == "zh")
        {
            return region == "CN" ? "zh-Hans" : null;
        }

        if (language == "pt")
        {
            return "pt-BR";
        }

        return Known(region is null ? null : $"{language}-{region}") ?? Known(language);
    }

    private static string? Known(string? name) =>
        name is null ? null : Translated.FirstOrDefault(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase));
}
