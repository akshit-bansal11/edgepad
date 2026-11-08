namespace Edgepad.Platform;

/// <summary>
/// <c>updates.txt</c>: the "Check for updates daily" switch, <c>enabled=0|1</c>, as on Windows. On when there is no file or
/// it cannot be read: a check only asks GitHub for the newest version number, and an owner who turned it off said so in
/// a file Edgepad wrote. Read once and kept.
/// </summary>
internal sealed class UpdateSetting(string path)
{
    private const string Off = "enabled=0";
    private const string On = "enabled=1";

    private volatile bool enabled = Load(path);

    public bool Enabled => enabled;

    public static UpdateSetting ForCurrentUser() => new(Path.Combine(Xdg.ConfigFolder, "updates.txt"));

    /// <summary>Takes effect at once whatever the disk says; false, logged, means the next launch will not remember it.</summary>
    public bool Set(bool on)
    {
        enabled = on;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, (on ? On : Off) + "\n");
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"The update setting could not be saved to {path}: {e.Message}");
            return false;
        }
    }

    private static bool Load(string file)
    {
        try
        {
            return File.ReadAllText(file).Trim() != Off;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"The update setting could not be read from {file}, so updates are checked daily: {e.Message}");
            return true;
        }
    }
}
