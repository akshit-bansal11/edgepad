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

    public bool Enabled => File.Exists(FilePath);

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
    /// The Exec value: quoted when the path has a space. A path with a quote, backtick, dollar or backslash falls back
    /// to the installed command's name, which is on the PATH.
    /// </summary>
    public static string ExecField(string path)
    {
        if (path.IndexOfAny(Awkward) >= 0)
        {
            return "edgepad";
        }

        return path.Contains(' ') ? $"\"{path}\"" : path;
    }
}
