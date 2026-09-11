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

    public static void Write(string message)
    {
        lock (Gate)
        {
            System.IO.Directory.CreateDirectory(Directory);
            var file = new FileInfo(FilePath);
            if (file.Exists && file.Length > MaxBytes)
            {
                File.Move(FilePath, FilePath + ".old", overwrite: true);
            }

            File.AppendAllText(FilePath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
    }
}
