using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Edgepad.Updates;

internal enum UpdateOutcome
{
    /// <summary>The verified new copy was started; it asks this one to quit.</summary>
    Started,

    /// <summary>The signature, the list or the hash did not check out. Nothing was put in place.</summary>
    Unverified,

    /// <summary>The network, the disk or the start failed. Nothing was installed.</summary>
    Failed,
}

/// <summary>
/// Replaces the running exe with a newer release. The order is <see cref="UpdateVerifier"/>'s: nothing is renamed to
/// its final name, let alone started, before the signature and the hash both pass. With no key embedded it does nothing.
/// </summary>
internal static class WindowsUpdater
{
    public static async Task<UpdateOutcome> InstallAsync(Version version, string runningPath, CancellationToken ct)
    {
        string? partial = null;
        try
        {
            if (!UpdateKey.IsSet)
            {
                return UpdateOutcome.Failed;
            }

            var sums = await UpdateDownload.GetBytesAsync(UpdateCheck.SumsUrl(version), UpdateDownload.MaxSumsBytes, null, ct);
            var sig = sums is null
                ? null
                : await UpdateDownload.GetBytesAsync(UpdateCheck.SigUrl(version), UpdateDownload.MaxSignatureBytes, null, ct);
            if (sums is null || sig is null)
            {
                return UpdateOutcome.Failed;
            }

            if (!UpdateVerifier.VerifySums(sums, sig, UpdateKey.Spki())
                || !UpdateVerifier.TryHashFor(Encoding.UTF8.GetString(sums), UpdateCheck.ExeName(version), out var expected))
            {
                Log.Write($"Edgepad {version}: the signed list did not check out; nothing was installed");
                return UpdateOutcome.Unverified;
            }

            var directory = UpdatePaths.ChooseDirectory(
                Path.GetDirectoryName(runningPath) ?? string.Empty, UpdatePaths.FallbackDirectory(), UpdatePaths.IsWritable);
            Directory.CreateDirectory(directory);
            var target = Path.Combine(directory, UpdateCheck.ExeName(version));
            partial = target + ".download";

            var actual = await UpdateDownload.DownloadAsync(
                UpdateCheck.AssetUrl(version, UpdateCheck.ExeName(version)), target, UpdateDownload.MaxAssetBytes, null, null, ct);
            if (actual is null)
            {
                return UpdateOutcome.Failed;
            }

            if (!UpdateVerifier.HashesMatch(actual, expected))
            {
                Log.Write($"Edgepad {version}: the download does not match the signed hash; it was discarded");
                return UpdateOutcome.Unverified;
            }

            File.Move(partial, target, overwrite: true);
            partial = null;
            var start = new ProcessStartInfo(target) { UseShellExecute = false };
            start.ArgumentList.Add(UpdatePaths.ReplacesOption);
            start.ArgumentList.Add(runningPath);
            using var started = Process.Start(start);
            return UpdateOutcome.Started;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            Log.Write($"Edgepad {version} could not be put in place: {e.Message}");
            return UpdateOutcome.Failed;
        }
        finally
        {
            if (partial is not null)
            {
                try
                {
                    File.Delete(partial);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    Log.Write($"The partial update {partial} could not be removed: {e.Message}");
                }
            }
        }
    }

    /// <summary>
    /// After hand-over the new copy removes the exe it replaced, if <see cref="UpdatePaths.IsReplaceable"/> says it may:
    /// five tries a second apart, since the old process may still be exiting.
    /// </summary>
    public static async Task DeleteReplacedAsync(string? candidate, string selfPath, Version self)
    {
        if (candidate is null || !UpdatePaths.IsReplaceable(candidate, selfPath, self))
        {
            return;
        }

        const int tries = 5;
        for (var i = 0; i < tries; i++)
        {
            try
            {
                File.Delete(candidate);
                Log.Write("The replaced Edgepad was removed");
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (i == tries - 1)
                {
                    Log.Write($"The replaced Edgepad could not be removed: {e.Message}");
                    return;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }
}
