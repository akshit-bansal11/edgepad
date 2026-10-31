namespace Edgepad.Platform;

/// <summary>
/// The tray's "Share clipboard" switch, kept in <c>clipboard.txt</c> beside the other settings. On unless the file says
/// <c>off</c>: the owner asked for a shared clipboard, and it still moves nothing until a phone asks on its link.
/// Read on every session thread, so it is a volatile field loaded once.
/// </summary>
internal static class ClipboardSetting
{
    private static readonly string FilePath = Path.Combine(Xdg.ConfigFolder, "clipboard.txt");

    private static volatile bool enabled = Load();

    public static bool Enabled => enabled;

    /// <summary>
    /// Applies the choice at once, and saves it. False means only that it will not survive a restart: an owner turning
    /// sharing off on a full disk still gets it off.
    /// </summary>
    public static bool Set(bool on)
    {
        enabled = on;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, on ? "on\n" : "off\n");
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"clipboard.txt could not be written: {e.Message}");
            return false;
        }
    }

    private static bool Load()
    {
        try
        {
            return !(File.Exists(FilePath) && File.ReadAllText(FilePath).Trim() == "off");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Unreadable: sharing is the thing to fail closed on.
            Log.Write($"clipboard.txt could not be read, so the clipboard is not shared: {e.Message}");
            return false;
        }
    }
}
