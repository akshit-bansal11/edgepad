namespace Edgepad.Trust;

/// <summary>
/// Where the Windows app keeps its files: the trust list, paired-link keys, PIN hash, link identity, macros and
/// settings. Local AppData, not Roaming: a domain profile copies Roaming to other machines, and these are secrets
/// or sit beside them. Up to 4.2.0 they lived in Roaming; <see cref="Migrate"/> moves them once at startup.
/// Linux builds its own paths from XDG and never uses this.
/// </summary>
internal static class UserFolder
{
    public static string Current => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Edgepad");

    public static string Legacy => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Edgepad");

    /// <summary>
    /// Moves every top-level file in <paramref name="legacy"/> that <paramref name="current"/> does not have yet,
    /// never overwriting one, then removes <paramref name="legacy"/> if that left it empty. Never throws: a file that
    /// cannot be moved stays where it was and is logged, and the app starts without it, as unpaired, which is safe.
    /// A move keeps the file's own permissions, which were already private. Logs names, never contents.
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
            foreach (var from in Directory.EnumerateFiles(legacy))
            {
                var name = Path.GetFileName(from);
                var to = Path.Combine(current, name);
                if (File.Exists(to))
                {
                    log($"Kept {name} in {legacy}: {current} already has one");
                    continue;
                }

                try
                {
                    File.Move(from, to);
                    log($"Moved {name} from {legacy} to {current}");
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    log($"Could not move {name} from {legacy} to {current}: {e.Message}");
                }
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
}
