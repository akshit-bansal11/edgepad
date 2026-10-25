using Edgepad.Protocol;

namespace Edgepad.Capture;

/// <summary>
/// One file as a poll saw it: where it is, how long it was, when it was last written, and whether it is in a folder
/// other things save into too (<see cref="CaptureFolder.Shared"/>).
/// </summary>
internal readonly record struct FileStamp(string Path, long Length, DateTime WrittenUtc, bool Shared = false);

/// <summary>
/// Which file, if any, an OS capture tool just produced. Pure, so every decision is a test: the watch lists the
/// folders and this decides. Every rule fails safe, because a wrong guess sends someone else's file to the phone and
/// then deletes it: a file that was there before the capture is never a candidate, nor one of a type the protocol
/// does not carry, nor a hidden or temporary one (a leading <c>.</c> or <c>~</c>), and two candidates at once are
/// one too many to choose between.
/// </summary>
internal static class CapturePick
{
    /// <summary>What is in the folders before the shortcut is pressed.</summary>
    public static HashSet<string> Before(IEnumerable<FileStamp> files) =>
        files.Select(file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The one new capture among <paramref name="now"/>, or null when there is none yet. <paramref name="ambiguous"/>
    /// is set when there is more than one, which ends the watch: no later poll can make that a safe choice.
    /// </summary>
    public static FileStamp? NewFile(IReadOnlySet<string> before, IEnumerable<FileStamp> now, out bool ambiguous)
    {
        FileStamp? found = null;
        ambiguous = false;
        foreach (var file in now)
        {
            if (before.Contains(file.Path) || !Candidate(file.Path, file.Shared))
            {
                continue;
            }

            if (found is not null)
            {
                ambiguous = true;
                return null;
            }

            found = file;
        }

        return found;
    }

    /// <summary>
    /// Whether a file has stopped changing: the same file, the same length and the same write time as the last poll,
    /// and not empty. A tool that is still writing, a recording above all, changes one of them between polls.
    /// </summary>
    public static bool Unchanged(FileStamp? previous, FileStamp current) => previous == current && current.Length > 0;

    /// <summary>
    /// A file of a type the protocol carries, and not one a tool hides while it writes. In a <paramref name="shared"/>
    /// folder only a name the capture tools give is taken (in English: a tool that names its files in another language
    /// is not recognised there, which keeps the file, the safe side of a wrong guess).
    /// </summary>
    public static bool Candidate(string path, bool shared = false)
    {
        var name = Path.GetFileName(path);
        return name.Length > 0 && name[0] is not ('.' or '~') && FileTypes.FromPath(name) is not null
            && (!shared || CaptureNames.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>How Windows, macOS, GNOME and KDE start the names of their screenshots and recordings.</summary>
    private static readonly string[] CaptureNames = ["Screenshot", "Screen Shot", "Screen Recording", "Screencast"];
}
