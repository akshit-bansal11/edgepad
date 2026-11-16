namespace Edgepad.Trust;

/// <summary>
/// Where the Windows app keeps its files: the trust list, paired-link keys, PIN hash, link identity, macros and
/// settings. Local AppData, not Roaming: a domain profile copies Roaming to other machines, and these are secrets
/// or sit beside them. Up to 4.2.0 they lived in Roaming; <see cref="Migrate"/> moves them once at startup.
/// Linux builds its own paths from XDG and never uses this.
/// </summary>
internal static class UserFolder
{
    /// <summary>
    /// In the new folder: the name of every file that has reached it from Roaming, one per line. A marked name is
    /// never read from Roaming again, so a stale copy there (a roaming profile syncing it back, a source that could
    /// not be deleted, a run of 4.2.0) cannot bring back phones, keys or a verified list the owner has since forgotten.
    /// </summary>
    internal const string MarkerName = "moved-from-roaming.txt";

    public static string Current => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Edgepad");

    public static string Legacy => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Edgepad");

    /// <summary><see cref="PathFor(string, string, Action{string}, string)"/> on the real folders.</summary>
    public static string PathFor(string name) => PathFor(Legacy, Current, Log.Write, name);

    /// <summary>
    /// Where a store opens the file <paramref name="name"/>, each file on its own: in <paramref name="current"/> when
    /// it is there, or when it has been moved before (a file the owner has deleted since stays deleted); else in
    /// <paramref name="legacy"/> when only that has it, because <see cref="Migrate"/> could not move it, logged; else
    /// in <paramref name="current"/>, for a new file. Fails closed: a PIN or trust list that could not move is read
    /// where it is, never replaced by the defaults of a missing file. Never a folder for a group of files: a partial
    /// move would leave one of them missing from whichever folder was chosen.
    /// </summary>
    internal static string PathFor(string legacy, string current, Action<string> log, string name)
    {
        var moved = Path.Combine(current, name);
        var left = Path.Combine(legacy, name);
        if (!File.Exists(moved) && File.Exists(left) && !Marked(current).Contains(name))
        {
            log($"Reading {name} from {legacy}: it could not be moved to {current}");
            return left;
        }

        return moved;
    }

    /// <summary>
    /// Moves every top-level file in <paramref name="legacy"/> to <paramref name="current"/>, then removes
    /// <paramref name="legacy"/> if that left it empty. Each file is copied to a temporary name, checked byte for
    /// byte, renamed into place, marked, and only then deleted from Roaming, so a crash at any point leaves either
    /// the old file readable or the new one complete. A file <paramref name="current"/> already has, or one marked
    /// as moved before, is never copied again: its Roaming copy is deleted. Never throws: a file that cannot be
    /// moved stays where it was and is logged, and <see cref="PathFor(string, string, Action{string}, string)"/>
    /// reads it there; a Roaming copy that cannot be deleted is logged and, being marked, never read. A copy keeps
    /// no looser permissions than the private folder it lands in. Logs names, never contents.
    /// </summary>
    public static void Migrate(string legacy, string current, Action<string> log)
    {
        try
        {
            if (!Directory.Exists(legacy))
            {
                return;
            }

            PrivateStorage.CreateDirectory(current);
            var marked = Marked(current);
            foreach (var from in Directory.EnumerateFiles(legacy))
            {
                var name = Path.GetFileName(from);
                var to = Path.Combine(current, name);
                if (marked.Contains(name) || File.Exists(to))
                {
                    Mark(current, name, marked, log);
                    Delete(from, name, legacy, log, $"Removed the stale copy of {name} from {legacy}");
                    continue;
                }

                var temp = to + ".tmp";
                try
                {
                    File.Copy(from, temp, overwrite: true);
                    if (!File.ReadAllBytes(from).AsSpan().SequenceEqual(File.ReadAllBytes(temp)))
                    {
                        throw new IOException("the copy does not match the original");
                    }

                    File.Move(temp, to);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    log($"Could not move {name} from {legacy} to {current}: {e.Message}");
                    Delete(temp, name, current, log, null);
                    continue;
                }

                Mark(current, name, marked, log);
                Delete(from, name, legacy, log, $"Moved {name} from {legacy} to {current}");
            }

            if (!Directory.EnumerateFileSystemEntries(legacy).Any())
            {
                Directory.Delete(legacy);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log($"Could not move the files in {legacy} to {current}: {e.Message}");
        }
    }

    /// <summary>The names marked as moved; empty when the marker is missing or cannot be read.</summary>
    private static HashSet<string> Marked(string current)
    {
        try
        {
            var path = Path.Combine(current, MarkerName);
            return File.Exists(path)
                ? new HashSet<string>(File.ReadAllLines(path).Where(line => line.Length > 0), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void Mark(string current, string name, HashSet<string> marked, Action<string> log)
    {
        if (!marked.Add(name))
        {
            return;
        }

        try
        {
            File.AppendAllLines(Path.Combine(current, MarkerName), [name]);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log($"Could not note that {name} was moved to {current}: {e.Message}");
        }
    }

    private static void Delete(string path, string name, string folder, Action<string> log, string? done)
    {
        try
        {
            File.Delete(path);
            if (done is not null)
            {
                log(done);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log($"Could not remove {name} from {folder}: {e.Message}");
        }
    }
}
