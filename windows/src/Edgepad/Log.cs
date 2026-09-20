namespace Edgepad;

/// <summary>
/// Appends lifecycle events to %LOCALAPPDATA%\Edgepad\edgepad.log — the only error channel a tray app has.
/// Never called per input frame: file I/O on the input path would cost latency.
/// </summary>
internal static class Log
{
    private const long MaxBytes = 1024 * 1024;

    private static readonly Lock Gate = new();

    private static readonly string Directory = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Edgepad");

    public static string FilePath { get; } = System.IO.Path.Combine(Directory, "edgepad.log");

    /// <summary>
    /// Appends a line, or gives up quietly. This must not throw, whatever the disk does: almost every
    /// caller is inside a catch block, several of them on thread-pool and background threads where an
    /// escape ends the process. The log open in an editor when it passes a megabyte is enough to do it —
    /// the rotation then fails with "used by another process" — and losing the tray app, the link and the
    /// user's session to a failed log line would be the error channel costing more than every error it
    /// ever reported. There is nowhere to report this one: this is the place errors get reported to.
    /// </summary>
    public static void Write(string message)
    {
        lock (Gate)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > MaxBytes)
                {
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                }

                File.AppendAllText(FilePath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // Deliberately empty. See the note above: there is no second channel to fall back to.
            }
        }
    }
}
