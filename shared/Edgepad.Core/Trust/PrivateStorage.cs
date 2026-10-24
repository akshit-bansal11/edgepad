namespace Edgepad.Trust;

/// <summary>
/// Keeps the trust and security files to their owner on Unix, as the Mac app does (0700 folders, 0600 files).
/// On Windows the per-user %APPDATA% folder already is, so every method there is the plain call.
/// </summary>
internal static class PrivateStorage
{
    private const UnixFileMode PrivateFolder = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode PrivateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>Creates <paramref name="path"/> and any missing parents; on Unix the new folders are 0700.</summary>
    public static void CreateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            Directory.CreateDirectory(path, PrivateFolder);
        }
    }

    /// <summary>
    /// Call just before writing <paramref name="path"/>. On Unix it leaves an empty 0600 file there first, so
    /// the contents are never on disk under a looser mode, including when the file already existed with one.
    /// </summary>
    public static void PrepareFile(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.WriteAllText(path, "");
            File.SetUnixFileMode(path, PrivateFile);
        }
    }

    /// <summary>
    /// Replaces <paramref name="path"/> whole: <paramref name="write"/> fills a private file beside it, which is then
    /// moved over it. A crash, a full disk or a failed write leaves the old file as it was rather than truncated, so
    /// what is on disk never falls behind what the caller still holds in memory. Throws what the write or the move
    /// threw, after removing the half-written file beside it.
    /// </summary>
    public static void Replace(string path, Action<string> write)
    {
        var temp = path + ".tmp";
        try
        {
            CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            PrepareFile(temp);
            write(temp);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                // The write's failure is the one worth reporting; a leftover temp file is overwritten next time.
            }

            throw;
        }
    }
}
