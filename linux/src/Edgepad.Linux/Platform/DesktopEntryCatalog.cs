using Edgepad.Macros;

namespace Edgepad.Platform;

/// <summary>
/// The apps with a <c>.desktop</c> entry, which is what a Linux launcher lists. A macro for one runs <c>gio launch</c> on
/// the entry, so the desktop starts it as a click would, with no shell. Every name here is text from a file on this
/// machine that may have been written by anyone, and goes into a tab-separated list, so it is cleaned by
/// <see cref="MacroStore.Plain"/> (control characters, tabs and newlines replaced).
/// </summary>
/// <param name="folders">The <c>applications</c> folders, the owner's first: an entry id found twice keeps the first.</param>
/// <param name="languages">Locale names to try for <c>Name[xx]</c>, best first, such as <c>en_US</c> then <c>en</c>.</param>
internal sealed class DesktopEntryCatalog(IReadOnlyList<string> folders, IReadOnlyList<string> languages) : IAppCatalog
{
    /// <summary>The icon size asked for, the smallest tier the phone's list needs.</summary>
    private const int IconSize = 64;

    public DesktopEntryCatalog()
        : this(DefaultFolders(), Languages(Environment.GetEnvironmentVariable("LANG")))
    {
    }

    public IReadOnlyList<AppEntry> Scan()
    {
        var found = new Dictionary<string, AppEntry>(StringComparer.Ordinal);
        foreach (var folder in folders)
        {
            foreach (var file in Files(folder))
            {
                var id = Path.GetFileName(file);

                // The launcher splits its arguments on spaces, so a path with one could not be launched.
                if (found.ContainsKey(id) || file.Contains(' ', StringComparison.Ordinal))
                {
                    continue;
                }

                if (Read(file) is { } name)
                {
                    found[id] = new AppEntry(name, "gio", $"launch {file}", file);
                }
            }
        }

        return [.. found.Values];
    }

    // The entry is the icon's source: DesktopIcons finds its Icon= from the .desktop path.
    public byte[]? Icon(AppEntry app) =>
        app.IconPath is null ? null : LinuxMacroIcons.Png(new Macro(app.Name, app.Target, app.Arguments, app.IconPath), IconSize);

    /// <summary>The app's name, or null when the entry is not one to list: hidden, not an application, or nameless.</summary>
    internal static string? Parse(IEnumerable<string> lines, IReadOnlyList<string> languages)
    {
        string? type = null;
        string? name = null;
        var localised = new Dictionary<string, string>(StringComparer.Ordinal);
        var inEntry = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (line[0] == '[')
            {
                if (inEntry)
                {
                    break;
                }

                inEntry = line == "[Desktop Entry]";
                continue;
            }

            var equals = line.IndexOf('=', StringComparison.Ordinal);
            if (!inEntry || equals <= 0)
            {
                continue;
            }

            var key = line[..equals].TrimEnd();
            var value = line[(equals + 1)..].Trim();
            switch (key)
            {
                case "Type":
                    type ??= value;
                    break;
                case "NoDisplay" or "Hidden" when value == "true":
                    return null;
                case "Name":
                    name ??= Unescape(value);
                    break;
                default:
                    if (key.StartsWith("Name[", StringComparison.Ordinal) && key.EndsWith(']'))
                    {
                        localised.TryAdd(key[5..^1], Unescape(value));
                    }

                    break;
            }
        }

        var chosen = languages.Select(l => localised.GetValueOrDefault(l)).FirstOrDefault(n => n is not null) ?? name;
        var clean = chosen is null ? string.Empty : MacroStore.Plain(chosen);
        return type == "Application" && clean.Length > 0 ? clean : null;
    }

    /// <summary>The locales to try for <c>LANG</c> such as <c>de_AT.UTF-8@euro</c>: <c>de_AT</c>, then <c>de</c>.</summary>
    internal static IReadOnlyList<string> Languages(string? lang)
    {
        var bare = (lang ?? string.Empty).Split('.', '@')[0];
        var language = bare.Split('_')[0];
        return bare.Length == 0 || bare is "C" or "POSIX" ? [] : language == bare ? [bare] : [bare, language];
    }

    /// <summary>The value with the escapes the Desktop Entry spec defines for strings turned into what they stand for.</summary>
    internal static string Unescape(string value) =>
        value.Contains('\', StringComparison.Ordinal)
            ? value.Replace("\s", " ", StringComparison.Ordinal).Replace("\n", "\n", StringComparison.Ordinal)
                .Replace("\t", "\t", StringComparison.Ordinal).Replace("\r", "\r", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal)
            : value;

    private string? Read(string file)
    {
        try
        {
            return Parse(File.ReadLines(file), languages);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static List<string> Files(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*.desktop").Order(StringComparer.Ordinal).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static List<string> DefaultFolders()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var data = DesktopIcons.DataDirs(
            Environment.GetEnvironmentVariable("XDG_DATA_HOME"), Environment.GetEnvironmentVariable("XDG_DATA_DIRS"), home);
        var folders = data.Select(dir => Path.Combine(dir, "applications")).ToList();
        folders.Add(Path.Combine(home, ".local/share/flatpak/exports/share/applications"));
        folders.Add("/var/lib/flatpak/exports/share/applications");
        folders.Add("/var/lib/snapd/desktop/applications");
        return folders.Distinct(StringComparer.Ordinal).ToList();
    }
}
