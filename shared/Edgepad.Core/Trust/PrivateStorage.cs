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
}
