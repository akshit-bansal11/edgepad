using System.Globalization;

namespace Edgepad.Platform;

internal enum SecondLaunch
{
    /// <summary>The same version is already running: say so and leave.</summary>
    Notice,

    /// <summary>A different version is running: this one replaces it (a second copy is how an update is installed).</summary>
    TakeOver,
}

/// <summary>
/// Who holds the lock. The running copy writes "VERSION PID" beside it; a second launch of the same version only says
/// so, and one of a different version asks the first to quit and takes its place.
/// </summary>
internal static class SingleInstance
{
    public static string Line(string version, int pid) => string.Create(CultureInfo.InvariantCulture, $"{version} {pid}");

    public static (string Version, int Pid)? Parse(string? text)
    {
        var parts = text?.Trim().Split(' ');
        return parts is { Length: 2 } && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var pid) && pid > 1
            ? (parts[0], pid)
            : null;
    }

    /// <summary>
    /// Takes over only from a copy that named itself, whose process is still an Edgepad (a stale file's pid may belong
    /// to anything now). Anything unclear is a notice: never ask an unknown process to quit.
    /// </summary>
    public static SecondLaunch Decide((string Version, int Pid)? running, string mine, bool sameProgram) =>
        running is { } r && sameProgram && r.Version != mine ? SecondLaunch.TakeOver : SecondLaunch.Notice;

    /// <summary>Whether the process at /proc/PID/exe is this program, by file name (a replaced file reads "... (deleted)").</summary>
    public static bool SameProgram(string? exe, string? mine)
    {
        const string Deleted = " (deleted)";
        if (exe is null || mine is null)
        {
            return false;
        }

        var name = exe.EndsWith(Deleted, StringComparison.Ordinal) ? exe[..^Deleted.Length] : exe;
        return string.Equals(Path.GetFileName(name), Path.GetFileName(mine), StringComparison.Ordinal);
    }
}
