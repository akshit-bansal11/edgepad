namespace Edgepad;

/// <summary>
/// Where this app keeps its files, by the XDG Base Directory spec: settings under <c>$XDG_CONFIG_HOME/edgepad</c>,
/// the single-instance lock under <c>$XDG_CACHE_HOME/edgepad</c>. Lower case, as Linux apps name their folders;
/// the log stays where the shared core puts it (<c>~/.local/share/Edgepad</c>), like the other laptops'.
/// </summary>
internal static class Xdg
{
    private const string App = "edgepad";

    public static string ConfigFolder => Path.Combine(Resolve(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"), Home, ".config"), App);

    public static string CacheFolder => Path.Combine(Resolve(Environment.GetEnvironmentVariable("XDG_CACHE_HOME"), Home, ".cache"), App);

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>
    /// The variable's value when it is set and absolute, else <paramref name="fallback"/> under the home folder.
    /// The spec says a relative path in one of these variables is invalid and is to be ignored.
    /// </summary>
    public static string Resolve(string? value, string home, string fallback) =>
        !string.IsNullOrEmpty(value) && Path.IsPathRooted(value) ? value : Path.Combine(home, fallback);
}
