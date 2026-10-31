using Edgepad.Macros;

namespace Edgepad.Platform;

/// <summary>
/// Finds the picture file for a macro button from what a Linux desktop already keeps: <c>.desktop</c> entries and
/// the hicolor icon theme, following the freedesktop Desktop Entry and Icon Theme specs as far as they are needed
/// here. It only names a file; decoding and sending it is someone else's job.
///
/// Like the Windows icons this reads files on the laptop and nothing the phone sends ever reaches a path: the phone
/// asks for "the icons" and gets whatever the owner's own list points at. A target that cannot be resolved, or a
/// file that cannot be read, is a button with a label and not an error, so nothing here throws on a missing or
/// unreadable file. Arguments are never looked at. Strings and files only, so the lookup is testable with a temp
/// folder and no desktop.
/// </summary>
// ponytail: hicolor only. The user's icon theme (Adwaita, Papirus, ...) and its Inherits= chain are not read, so an
// app that ships its icon only in a theme gets no picture. Upgrade: parse index.theme from the current theme and
// walk Inherits=.
// ponytail: only the top level of applications/ is scanned, and the Exec= scan reads every .desktop file per lookup
// (no index); cache an Exec->Icon index per WANT_ICONS if a machine with thousands of entries shows a pause.
internal static class DesktopIcons
{
    /// <summary>What a picture file may be called. Anything else (an .xpm, say) is not something the phone can be sent.</summary>
    private static readonly string[] Pictures = [".png", ".svg", ".jpg", ".jpeg"];

    /// <summary>
    /// Folders under <c>icons/hicolor</c>, best first, largest leading because a phone may ask for up to 512 pixels and
    /// a smaller picture only soft-scales up; <c>scalable</c> is a vector, sharp at any size, so it ranks above the
    /// small rasters but after the big ones, which need no render.
    /// </summary>
    private static readonly string[] HicolorSizes =
        ["512x512", "256x256", "128x128", "scalable", "96x96", "72x72", "64x64", "48x48", "36x36", "32x32"];

    /// <summary>
    /// The folders to look in, best first: <c>$XDG_DATA_HOME</c> (or <c>~/.local/share</c>), then
    /// <c>$XDG_DATA_DIRS</c> (or <c>/usr/local/share:/usr/share</c>). A relative entry is invalid by the spec and
    /// dropped. The environment is read by the caller, so this stays pure.
    /// </summary>
    public static IReadOnlyList<string> DataDirs(string? dataHome, string? dataDirs, string home)
    {
        var dirs = new List<string> { Xdg.Resolve(dataHome, home, ".local/share") };
        var system = string.IsNullOrEmpty(dataDirs) ? "/usr/local/share:/usr/share" : dataDirs;
        foreach (var dir in system.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Path.IsPathRooted(dir))
            {
                dirs.Add(dir);
            }
        }

        return dirs;
    }

    /// <summary>
    /// The picture file for <paramref name="macro"/>, or null. The override wins when it resolves and falls through to
    /// the target when it does not, as on Windows and for the same reason: a macro whose icon has been deleted should
    /// go back to the program's own icon rather than to none.
    /// </summary>
    public static string? Find(Macro macro, IReadOnlyList<string> dataDirs, string home, Func<string, PathKind> classify) =>
        Resolve(macro.Icon, dataDirs, home, classify) ?? Resolve(macro.Target, dataDirs, home, classify);

    private static string? Resolve(string? text, IReadOnlyList<string> dataDirs, string home, Func<string, PathKind> classify)
    {
        // A URL has no icon here (a site's favicon is a web request this will not make).
        if (string.IsNullOrEmpty(text) || LinuxLauncher.IsUrl(text))
        {
            return null;
        }

        var path = text.StartsWith("~/", StringComparison.Ordinal) ? Path.Combine(home, text[2..]) : text;
        if (!path.Contains('/', StringComparison.Ordinal))
        {
            // A bare program name, which the launcher would find on PATH.
            return ByProgram(path, dataDirs);
        }

        // A folder or a missing path keeps its label: there is no per-folder icon to look up without a MIME database.
        var kind = classify(path);
        if (kind is PathKind.Folder or PathKind.None)
        {
            return null;
        }

        var extension = Path.GetExtension(path);
        if (Pictures.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return path;
        }

        if (extension.Equals(".desktop", StringComparison.OrdinalIgnoreCase))
        {
            return IconFor(ReadEntryFile(path).Icon, dataDirs);
        }

        // A program is looked up by its name; a plain document has no icon of its own to borrow without MIME data.
        return kind == PathKind.Executable ? ByProgram(Path.GetFileName(path), dataDirs) : null;
    }

    /// <summary>
    /// A program's icon: the <c>Icon=</c> of its <c>&lt;name&gt;.desktop</c> entry, else of whichever entry's
    /// <c>Exec=</c> starts the same program, else an icon named like the program itself. Entries are tried in data
    /// folder order, so the user's own entry overrides a system one.
    /// </summary>
    private static string? ByProgram(string name, IReadOnlyList<string> dataDirs)
    {
        foreach (var dir in dataDirs)
        {
            var entry = Path.Combine(dir, "applications", name + ".desktop");
            if (File.Exists(entry) && IconFor(ReadEntryFile(entry).Icon, dataDirs) is { } named)
            {
                return named;
            }
        }

        foreach (var dir in dataDirs)
        {
            foreach (var entry in DesktopFiles(Path.Combine(dir, "applications")))
            {
                var (icon, exec) = ReadEntryFile(entry);
                if (exec is not null
                    && string.Equals(ExecProgram(exec), name, StringComparison.Ordinal)
                    && IconFor(icon, dataDirs) is { } found)
                {
                    return found;
                }
            }
        }

        return IconFor(name, dataDirs);
    }

    /// <summary>The <c>.desktop</c> files directly in <paramref name="folder"/>, sorted so the answer never depends on the disk.</summary>
    private static List<string> DesktopFiles(string folder)
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

    private static (string? Icon, string? Exec) ReadEntryFile(string path)
    {
        try
        {
            return ReadEntry(File.ReadLines(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (null, null);
        }
    }

    /// <summary>
    /// The <c>Icon</c> and <c>Exec</c> of the <c>[Desktop Entry]</c> group. Later groups (<c>[Desktop Action ...]</c>)
    /// carry their own Icon and are not the app's. Keys match exactly, so a translated <c>Icon[de]=</c> is ignored;
    /// the first of a key wins; blank lines and comments are skipped.
    /// </summary>
    internal static (string? Icon, string? Exec) ReadEntry(IEnumerable<string> lines)
    {
        string? icon = null;
        string? exec = null;
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
            if (key == "Icon")
            {
                icon ??= value;
            }
            else if (key == "Exec")
            {
                exec ??= value;
            }
        }

        return (icon, exec);
    }

    /// <summary>
    /// The program an <c>Exec=</c> line starts, as a bare file name: its first word, with one leading double quote
    /// honoured for a path with spaces. Field codes (<c>%u</c>, <c>%f</c>) never matter because only the first word is
    /// read, and a wrapper such as <c>env FOO=1 prog</c> is therefore the wrapper.
    /// </summary>
    internal static string ExecProgram(string exec)
    {
        var text = exec.Trim();
        string word;
        if (text.StartsWith('"'))
        {
            var close = text.IndexOf('"', 1);
            word = close < 0 ? text[1..] : text[1..close];
        }
        else
        {
            var space = text.AsSpan().IndexOfAny(' ', '\t');
            word = space < 0 ? text : text[..space];
        }

        return Path.GetFileName(word);
    }

    /// <summary>
    /// Extensions an old <c>Icon=</c> name carries that the theme search must not see (an .xpm may have a .png twin).
    /// </summary>
    private static readonly string[] LegacyExtensions = [".png", ".svg", ".xpm"];

    /// <summary>
    /// The file an <c>Icon=</c> value names, or null. An absolute path is that file or nothing, and never falls to a
    /// name search. A name is looked up in hicolor and then in pixmaps; a name that is itself a path is refused, since
    /// a name is never one.
    /// </summary>
    private static string? IconFor(string? icon, IReadOnlyList<string> dataDirs)
    {
        if (string.IsNullOrEmpty(icon))
        {
            return null;
        }

        if (Path.IsPathRooted(icon))
        {
            return Pictures.Contains(Path.GetExtension(icon), StringComparer.OrdinalIgnoreCase) && File.Exists(icon) ? icon : null;
        }

        if (icon.Contains('/', StringComparison.Ordinal) || icon.Contains('\\', StringComparison.Ordinal))
        {
            return null;
        }

        // Old entries write the file name, Icon=foo.png; the spec says a name has no extension, and the theme search
        // adds its own, so foo.png would be looked for as foo.png.png and never found.
        if (LegacyExtensions.Contains(Path.GetExtension(icon), StringComparer.OrdinalIgnoreCase))
        {
            icon = Path.GetFileNameWithoutExtension(icon);
        }

        foreach (var dir in dataDirs)
        {
            foreach (var size in HicolorSizes)
            {
                var folder = Path.Combine(dir, "icons", "hicolor", size, "apps");
                if (FirstExisting(folder, icon) is { } themed)
                {
                    return themed;
                }
            }
        }

        foreach (var dir in dataDirs)
        {
            if (FirstExisting(Path.Combine(dir, "pixmaps"), icon) is { } loose)
            {
                return loose;
            }
        }

        return null;
    }

    /// <summary>The PNG for <paramref name="name"/> in <paramref name="folder"/>, else the SVG, else null.</summary>
    private static string? FirstExisting(string folder, string name)
    {
        var png = Path.Combine(folder, name + ".png");
        if (File.Exists(png))
        {
            return png;
        }

        var svg = Path.Combine(folder, name + ".svg");
        return File.Exists(svg) ? svg : null;
    }
}
