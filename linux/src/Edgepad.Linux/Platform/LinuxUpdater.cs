using System.ComponentModel;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Edgepad.Updates;

namespace Edgepad.Platform;

/// <summary>How installing a downloaded update went.</summary>
internal enum UpdateOutcome
{
    /// <summary>The new program replaced the old one and was started; its second-launch path ends this copy.</summary>
    Started,

    /// <summary>Verified and kept in the cache; the install location is not the user's to write, so the owner runs install.sh.</summary>
    NeedsRoot,

    /// <summary>Nothing was installed.</summary>
    Failed,
}

/// <summary>
/// Installs a newer release over the running program. The order is UpdateVerifier's: signed list, exact asset hash,
/// download to ".download", hash match, and only then anything is moved or extracted. Only the program file is ever
/// written, and only from the entry named exactly <c>edgepad-VERSION/edgepad</c>; udev and desktop files change only
/// through install.sh. An install the user cannot write is never escalated: the verified archive is kept and the
/// command to run is shown.
/// </summary>
internal static class LinuxUpdater
{
    public const long MaxExtractBytes = 150L * 1024 * 1024;

    private const string StagedName = ".edgepad.new";
    private const UnixFileMode Private = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode Executable = Private
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    public static async Task<UpdateOutcome> InstallAsync(
        Version version, string cacheFolder, string? exePath, IProgress<int>? progress, CancellationToken ct)
    {
        if (!UpdateKey.IsSet)
        {
            return UpdateOutcome.Failed;
        }

        var sums = await UpdateDownload.GetBytesAsync(UpdateCheck.SumsUrl(version), UpdateDownload.MaxSumsBytes, null, ct).ConfigureAwait(false);
        var sig = await UpdateDownload.GetBytesAsync(UpdateCheck.SigUrl(version), UpdateDownload.MaxSignatureBytes, null, ct).ConfigureAwait(false);
        if (sums is null || sig is null || !UpdateVerifier.VerifySums(sums, sig, UpdateKey.Spki()))
        {
            Log.Write("The update list was not fetched or its signature did not verify, so nothing was installed.");
            return UpdateOutcome.Failed;
        }

        var name = UpdateCheck.LinuxName(version);
        if (name is null)
        {
            Log.Write($"There is no release archive for {RuntimeInformation.OSArchitecture}, so nothing was installed.");
            return UpdateOutcome.Failed;
        }

        if (!UpdateVerifier.TryHashFor(Encoding.UTF8.GetString(sums), name, out var listed))
        {
            Log.Write($"The signed list does not name {name} exactly once, so nothing was installed.");
            return UpdateOutcome.Failed;
        }

        var archive = Path.Combine(cacheFolder, name);
        try
        {
            // The guard is for the platform analyzer, which the class-level attribute in Program.cs does not reach.
            if (!OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(cacheFolder, Private);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"The update folder {cacheFolder} could not be made: {e.Message}");
            return UpdateOutcome.Failed;
        }

        var actual = await UpdateDownload.DownloadAsync(UpdateCheck.AssetUrl(version, name), archive, UpdateDownload.MaxAssetBytes, progress, null, ct).ConfigureAwait(false);
        if (actual is null)
        {
            return UpdateOutcome.Failed;
        }

        var temp = archive + ".download";
        try
        {
            if (!UpdateVerifier.HashesMatch(actual, listed))
            {
                Log.Write($"{name} does not match the signed list, so it was discarded.");
                File.Delete(temp);
                return UpdateOutcome.Failed;
            }

            File.Move(temp, archive, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"The verified update could not be kept: {e.Message}");
            return UpdateOutcome.Failed;
        }

        var dir = exePath is null ? null : Path.GetDirectoryName(exePath);
        if (exePath is null || dir is null || Path.GetFileName(exePath) == "dotnet" || !CanWrite(dir))
        {
            return UpdateOutcome.NeedsRoot;
        }

        return ReplaceProgram(archive, version, exePath) ? UpdateOutcome.Started : UpdateOutcome.Failed;
    }

    /// <summary>Whether a file can be made in <paramref name="dir"/> by this user, found by making and removing one.</summary>
    public static bool CanWrite(string dir)
    {
        var probe = Path.Combine(dir, $".edgepad-probe-{Guid.NewGuid():N}");
        try
        {
            File.Create(probe).Dispose();
            File.Delete(probe);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>What to run when the install is not the user's: unpack the kept archive and run its install.sh as root.</summary>
    public static string RootCommand(string archivePath, Version version)
    {
        var folder = Path.GetDirectoryName(archivePath) ?? ".";
        return $"cd {Quote(folder)} && tar -xzf {Quote(Path.GetFileName(archivePath))} && cd edgepad-{version} && sudo ./install.sh";
    }

    /// <summary>
    /// Writes the program from <paramref name="archive"/> beside <paramref name="exePath"/> as ".edgepad.new", makes it
    /// executable, renames it over the old program (the running copy keeps its old file) and starts it.
    /// </summary>
    internal static bool ReplaceProgram(string archive, Version version, string exePath)
    {
        var staged = Path.Combine(Path.GetDirectoryName(exePath)!, StagedName);
        try
        {
            if (!ExtractProgram(archive, version, staged))
            {
                return false;
            }

            if (!IsProgramFor(staged, RuntimeInformation.OSArchitecture))
            {
                Log.Write("The update's program is not built for this CPU, so the installed one was left alone.");
                TryDelete(staged);
                return false;
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(staged, Executable);
            }

            File.Move(staged, exePath, overwrite: true);
            Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = false })?.Dispose();
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception)
        {
            Log.Write($"The update could not be put in place: {e.Message}");
            TryDelete(staged);
            return false;
        }
    }

    /// <summary>
    /// Writes only the entry named exactly <c>edgepad-VERSION/edgepad</c> to <paramref name="dest"/>. The whole archive is
    /// refused when any entry has an absolute name, a ".." part or is a link, when the entries add up to more than
    /// <paramref name="cap"/>, or when the program entry is missing, repeated or not a plain file. On refusal
    /// <paramref name="dest"/> does not exist.
    /// </summary>
    internal static bool ExtractProgram(string archive, Version version, string dest, long cap = MaxExtractBytes)
    {
        var wanted = $"edgepad-{version}/edgepad";
        var found = false;
        try
        {
            using var file = File.OpenRead(archive);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new TarReader(gzip);
            long total = 0;
            TarEntry? entry;
            while ((entry = reader.GetNextEntry(copyData: false)) is not null)
            {
                total += entry.Length;
                if (total > cap || !SafeName(entry.Name) || entry.EntryType is TarEntryType.SymbolicLink or TarEntryType.HardLink)
                {
                    Log.Write($"The update archive was refused at entry {entry.Name}.");
                    TryDelete(dest);
                    return false;
                }

                if (entry.Name != wanted)
                {
                    continue;
                }

                if (found || entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream is null)
                {
                    Log.Write("The update archive's program entry is repeated or not a plain file.");
                    TryDelete(dest);
                    return false;
                }

                found = true;
                using var output = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None);
                entry.DataStream.CopyTo(output);
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Log.Write($"The update archive could not be read: {e.Message}");
            TryDelete(dest);
            return false;
        }

        if (!found)
        {
            Log.Write($"The update archive has no {wanted}.");
            TryDelete(dest);
        }

        return found;
    }

    /// <summary>
    /// Whether <paramref name="path"/> is a 64-bit little-endian ELF program for <paramref name="arch"/>, read from the
    /// header's machine field. False for any other CPU, a short or unreadable file.
    /// </summary>
    internal static bool IsProgramFor(string path, Architecture arch)
    {
        var machine = arch switch
        {
            Architecture.X64 => 0x3E,
            Architecture.Arm64 => 0xB7,
            _ => -1,
        };
        if (machine < 0)
        {
            return false;
        }

        try
        {
            using var file = File.OpenRead(path);
            Span<byte> head = stackalloc byte[20];
            return file.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) == head.Length
                && head[0] == 0x7F && head[1] == (byte)'E' && head[2] == (byte)'L' && head[3] == (byte)'F'
                && head[4] == 2 && head[5] == 1
                && (head[18] | (head[19] << 8)) == machine;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool SafeName(string name) =>
        name.Length > 0 && !name.StartsWith('/') && !name.Contains('\\') && !name.Contains('\0') && !name.Split('/').Contains("..");

    private static string Quote(string s) => "'" + s.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"{path} could not be removed: {e.Message}");
        }
    }
}
