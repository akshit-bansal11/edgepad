namespace Edgepad.Platform;

/// <summary>
/// The tray's "Forward notifications to phone" switch, kept in <c>notify.txt</c> beside the other settings. Off unless
/// the file says <c>on</c>: a laptop that was never asked forwards nothing. Even on, nothing is read or sent until a
/// connected phone has sent SET NOTIFY 1 on its link. Read on session threads, so it is a volatile field loaded once.
/// </summary>
internal static class NotifySetting
{
    private static readonly string FilePath = Path.Combine(Xdg.ConfigFolder, "notify.txt");

    private static volatile bool enabled = Load();

    /// <summary>Raised after every <see cref="Set"/>, on the caller's thread.</summary>
    public static event Action? Changed;

    public static bool Enabled => enabled;

    /// <summary>Applies the choice at once and saves it. False means only that it will not survive a restart.</summary>
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
            Log.Write($"notify.txt could not be written: {e.Message}");
            return false;
        }
        finally
        {
            Changed?.Invoke();
        }
    }

    private static bool Load()
    {
        try
        {
            return File.Exists(FilePath) && File.ReadAllText(FilePath).Trim() == "on";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Unreadable: forwarding is the thing to fail closed on.
            Log.Write($"notify.txt could not be read, so notifications are not forwarded: {e.Message}");
            return false;
        }
    }
}
