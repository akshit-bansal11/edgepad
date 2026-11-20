using System.Runtime.InteropServices;

namespace Edgepad.Transfer;

/// <summary>
/// Where Windows keeps a file the phone sends: the Downloads folder Windows itself names (so one the owner moved is still
/// found), under <c>Edgepad</c>, and the Mark of the Web that makes SmartScreen and Office treat it as downloaded. The
/// receiver (<see cref="TransferReceiver"/>) never overwrites and never opens what it saves.
/// </summary>
internal static partial class WindowsTransfers
{
    private static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");

    /// <summary>Zone 3 is the Internet zone: what a browser writes for a file it downloaded.</summary>
    internal const string ZoneIdentifier = "[ZoneTransfer]\r\nZoneId=3\r\n";

    /// <summary>Where received files land.</summary>
    public static string Destination() => Path.Combine(DownloadsFolder(), "Edgepad");

    /// <summary>Where a file still arriving, or cut short, is kept; the receiver deletes parts after a week.</summary>
    public static string PartialFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Edgepad", "Partial");

    internal static string DownloadsFolder()
    {
        if (SHGetKnownFolderPath(in DownloadsId, 0, 0, out var path) == 0)
        {
            try
            {
                if (Marshal.PtrToStringUni(path) is { Length: > 0 } known)
                {
                    return known;
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(path);
            }
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    /// <summary>
    /// Writes <c>file:Zone.Identifier</c>. A volume without alternate data streams (FAT, exFAT) refuses it: logged, and the
    /// file stays, since the bytes are saved and checked either way.
    /// </summary>
    public static void MarkFromWeb(string file)
    {
        try
        {
            File.WriteAllText(file + ":Zone.Identifier", ZoneIdentifier);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Log.Write($"A received file could not be marked as downloaded: {e.Message}");
        }
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHGetKnownFolderPath(in Guid folderId, uint flags, nint token, out nint path);
}
