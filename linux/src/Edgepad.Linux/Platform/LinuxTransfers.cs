using Edgepad.Ui;

namespace Edgepad.Platform;

/// <summary>
/// Where Linux keeps a file the phone sends: the Downloads folder the desktop names (<c>$XDG_DOWNLOAD_DIR</c>, else
/// <c>user-dirs.dirs</c>, else <c>~/Downloads</c>), under <c>Edgepad</c>. The receiver never overwrites and never opens what it
/// saves; this makes the file readable and writable by its owner only, and never executable.
/// </summary>
internal static class LinuxTransfers
{
    /// <summary>Where received files land.</summary>
    public static string Destination() => Path.Combine(SignatureExport.UserFolder("DOWNLOAD", "Downloads"), "Edgepad");

    /// <summary>Where a file still arriving, or cut short, is kept (private to the owner); the receiver deletes parts after a week.</summary>
    public static string PartialFolder()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var folder = Path.Combine(Xdg.Resolve(Environment.GetEnvironmentVariable("XDG_STATE_HOME"), home, ".local/state"), "edgepad", "partial");
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                _ = Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"The partial folder could not be made: {e.Message}");
        }

        return folder;
    }

    /// <summary>Owner read and write only: no execute bit, nothing for anyone else. A file that cannot be changed is logged and kept.</summary>
    internal static void Restrict(string file)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"A received file's permissions could not be set: {e.Message}");
        }
    }

    /// <summary>A file has arrived whole and checked. The file name is the phone's, already made safe by the receiver.</summary>
    public static void Arrived(string file)
    {
        Restrict(file);

        // plain English until 4.5.0 localisation
        Notifier.Send("Edgepad", $"Received {Path.GetFileName(file)} from the phone.");
    }
}
