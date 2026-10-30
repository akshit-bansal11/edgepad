using System.Text.RegularExpressions;

namespace Edgepad.Macros;

/// <summary>
/// The apps in the Start menu, as shortcuts: what a person finds there is what they would want a button for. Store apps
/// and bare .exe files are not listed (a scan of those is installers and helpers). Launching goes through the shell, which
/// opens a .lnk as a double-click does.
/// </summary>
/// <param name="roots">The folders to walk, the owner's first: a name found twice keeps the first.</param>
/// <param name="resolve">A shortcut's target path, or null.</param>
internal sealed partial class StartMenuCatalog(IReadOnlyList<string> roots, Func<string, string?> resolve) : IAppCatalog
{
    /// <summary>How deep under a root a shortcut is looked for. Start menu folders are one or two levels in practice.</summary>
    internal const int MaxDepth = 4;

    /// <summary>The icon size asked for, which is the smallest tier the phone's list needs.</summary>
    private const int IconSize = 64;

    public StartMenuCatalog()
        : this(
            [Environment.GetFolderPath(Environment.SpecialFolder.Programs), Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)],
            ShortcutTarget.Resolve)
    {
    }

    public IReadOnlyList<AppEntry> Scan()
    {
        var found = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Where(root => root.Length > 0))
        {
            Walk(root, 0, found);
        }

        return [.. found.Values];
    }

    // The icon is the target's, not the shortcut's: a .lnk is "associated", so asking the shell for its picture gives
    // every app the same arrow-on-a-page. A shortcut with no readable target has no icon, which is a label on the phone.
    public byte[]? Icon(AppEntry app) =>
        app.IconPath is null ? null : MacroIcons.Png(new Macro(app.Name, app.IconPath, null), IconSize);

    /// <summary>The shortcut's file name, with the tabs, newlines and other control characters that would corrupt the phone's tab-separated list replaced.</summary>
    internal static string NameOf(string file) => MacroStore.Plain(Path.GetFileNameWithoutExtension(file));

    /// <summary>A shortcut that is not an app: the uninstaller, the manual, the link to the website.</summary>
    internal static bool Skipped(string name) => NotAnApp().IsMatch(name);

    [GeneratedRegex(@"\b(Uninstall\w*|Readme|Help|Documentation|Release Notes|Website)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NotAnApp();

    private void Walk(string folder, int depth, Dictionary<string, AppEntry> found)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.lnk"))
            {
                var name = NameOf(file);
                if (name.Length > 0 && !Skipped(name) && !found.ContainsKey(name))
                {
                    found[name] = new AppEntry(name, file, null, resolve(file));
                }
            }

            if (depth >= MaxDepth)
            {
                return;
            }

            foreach (var sub in Directory.EnumerateDirectories(folder))
            {
                // A junction or symlink can point back up the tree; the depth cap would end it, but not before it
                // had listed the same folder several times over.
                if (!new DirectoryInfo(sub).Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    Walk(sub, depth + 1, found);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // One unreadable folder costs its apps, not the list.
        }
    }
}
