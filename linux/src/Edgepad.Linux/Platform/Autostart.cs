namespace Edgepad.Platform;

/// <summary>
/// "Start at login": an entry in <c>$XDG_CONFIG_HOME/autostart</c> (the freedesktop Autostart spec), present or not.
/// The launcher's own entry is not copied: its Exec is a bare name, and this one names the file that is running.
/// </summary>
internal sealed class Autostart(string folder, string exec)
{
    // Characters the Exec quoting rules make awkward to escape.
    private static readonly char[] Awkward = ['"', '`', '$', (char)92];

    public string FilePath => Path.Combine(folder, "edgepad.desktop");

    /// <summary>
    /// An entry that starts this file. One left by a copy that moved or was replaced starts something else, or
    /// nothing, so it reads as off, and turning the switch on writes this path.
    /// </summary>
    public bool Enabled
    {
        get
        {
            try
            {
                return File.Exists(FilePath) && Starts(File.ReadAllText(FilePath), exec);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    public static Autostart ForCurrentUser() =>
        new(Path.Combine(Xdg.ConfigHome, "autostart"), Environment.ProcessPath ?? "edgepad");

    /// <summary>False, logged, when the entry could not be written or removed.</summary>
    public bool Set(bool on)
    {
        try
        {
            if (!on)
            {
                File.Delete(FilePath);
                return true;
            }

            Directory.CreateDirectory(folder);
            File.WriteAllText(FilePath, Entry(exec));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"The autostart entry {FilePath} could not be changed: {e.Message}");
            return false;
        }
    }

    public static string Entry(string exec) =>
        "[Desktop Entry]\nType=Application\nName=Edgepad\nComment=Use your phone as a touchpad, keyboard and gamepad\n"
            + $"Exec={ExecField(exec)}\nIcon=edgepad\nTerminal=false\nX-GNOME-Autostart-enabled=true\n";

    /// <summary>
    /// The Exec value: quoted when the path has a space, and a % doubled, since a lone % starts a field code. A path
    /// with a quote, backtick, dollar or backslash falls back to the installed command's name, which is on the PATH.
    /// </summary>
    public static string ExecField(string path)
    {
        if (path.IndexOfAny(Awkward) >= 0)
        {
            return "edgepad";
        }

        var escaped = path.Replace("%", "%%", StringComparison.Ordinal);
        return escaped.Contains(' ') ? $"\"{escaped}\"" : escaped;
    }

    /// <summary>Whether <paramref name="entry"/>'s Exec line is the one <see cref="Entry"/> writes for <paramref name="exec"/>.</summary>
    public static bool Starts(string entry, string exec)
    {
        var wanted = $"Exec={ExecField(exec)}";
        return entry.Split('\n').Any(line => line.TrimEnd('\r') == wanted);
    }
}
