using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Edgepad.Blob;
using Edgepad.Input;

namespace Edgepad.Platform;

/// <summary>What <see cref="LinuxClipboard.Pick"/> decided from the types a clipboard offers.</summary>
internal readonly record struct ClipboardPick(bool Sensitive, BlobKind? Kind, string? Target);

/// <summary>
/// This desktop's clipboard through wl-clipboard (Wayland) or xclip (X11), the tools typing already depends on, since
/// Avalonia 12.1.4's clipboard needs a window and offers no change event. Nothing is read in a shell; the tools get
/// their arguments as they are. Best effort and UNVERIFIED on hardware (docs/LINUX.md). Without the tool, or when it
/// fails, there is simply nothing to share.
/// </summary>
internal sealed class LinuxClipboard : IClipboardSurface
{
    /// <summary>Written by KeePassXC, Bitwarden and others for a copy that must not be kept or sent.</summary>
    private const string PasswordHint = "x-kde-passwordManagerHint";

    private const string Png = "image/png";

    private const int MaxListingBytes = 64 * 1024;

    private static readonly string[] TextTypes = ["text/plain;charset=utf-8", "UTF8_STRING"];
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(1);

    private readonly bool wayland;
    private readonly bool available;

    public LinuxClipboard(string? waylandDisplay, string? display)
    {
        wayland = !string.IsNullOrEmpty(waylandDisplay);
        available = wayland || !string.IsNullOrEmpty(display);
    }

    /// <summary>
    /// What to take from a clipboard that offers <paramref name="types"/>: a password manager's hint anywhere means
    /// nothing is read at all; otherwise a PNG, else UTF-8 text, else nothing.
    /// </summary>
    internal static ClipboardPick Pick(IEnumerable<string> types)
    {
        var offered = new HashSet<string>(types.Select(t => t.Trim()), StringComparer.OrdinalIgnoreCase);
        if (offered.Contains(PasswordHint))
        {
            return new ClipboardPick(true, null, null);
        }

        if (offered.Contains(Png))
        {
            return new ClipboardPick(false, BlobKind.ClipImage, Png);
        }

        var text = TextTypes.FirstOrDefault(offered.Contains);
        return text is null ? default : new ClipboardPick(false, BlobKind.ClipText, text);
    }

    public ClipboardContent? Read()
    {
        if (!available)
        {
            return null;
        }

        var listing = wayland
            ? Capture("wl-paste", ["--list-types"], MaxListingBytes)
            : Capture("xclip", ["-selection", "clipboard", "-o", "-t", "TARGETS"], MaxListingBytes);
        if (listing is null)
        {
            return null;
        }

        var pick = Pick(Encoding.UTF8.GetString(listing).Split('\n', StringSplitOptions.RemoveEmptyEntries));
        if (pick.Sensitive)
        {
            // Never fetched: the flag is the whole answer.
            return new ClipboardContent(BlobKind.ClipText, [], Sensitive: true);
        }

        if (pick is not { Kind: { } kind, Target: { } target })
        {
            return null;
        }

        var limit = (kind == BlobKind.ClipImage ? ClipboardSync.MaxImageBytes : ClipboardSync.MaxTextBytes) + 1;
        var data = wayland
            ? Capture("wl-paste", ["--no-newline", "--type", target], limit)
            : Capture("xclip", ["-selection", "clipboard", "-o", "-t", target], limit);
        return data is null ? null : new ClipboardContent(kind, data);
    }

    public void Write(ClipboardContent content)
    {
        if (!available)
        {
            throw new InvalidOperationException("no display for a clipboard tool");
        }

        var image = content.Kind == BlobKind.ClipImage;
        var tool = wayland
            ? new ClipboardTool("wl-copy", ["--type", image ? Png : TextTypes[0]])
            : new ClipboardTool("xclip", ["-selection", "clipboard", "-t", image ? Png : TextTypes[1]]);
        if (Typing.Copy(tool, content.Data) is { } failure)
        {
            throw new InvalidOperationException(failure);
        }
    }

    /// <summary>
    /// Tells <paramref name="sync"/> when this clipboard changes, by polling (no change event exists to listen to).
    /// The first read after start, or after the owner's toggle came back on, only learns what is there, so nothing
    /// old is sent. Nothing is read while the toggle is off. ponytail: one tool run a second, an inotify-like
    /// watcher (wl-paste --watch) only if that shows up in a profile.
    /// </summary>
    public static async Task Poll(IClipboardSurface surface, ClipboardSync sync, Func<bool> allowed, CancellationToken ended)
    {
        using var timer = new PeriodicTimer(PollEvery);
        byte[]? last = null;
        var primed = false;
        try
        {
            while (await timer.WaitForNextTickAsync(ended))
            {
                if (!allowed())
                {
                    primed = false;
                    continue;
                }

                try
                {
                    var content = surface.Read();
                    var now = content is { Sensitive: false, Data.Length: > 0 } ? SHA256.HashData(content.Data) : null;
                    if (primed && now is not null && (last is null || !now.AsSpan().SequenceEqual(last)))
                    {
                        sync.LocalChanged();
                    }

                    last = now;
                    primed = true;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    Log.Write($"Clipboard read failed: {e.GetType().Name}: {e.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The session ended.
        }
    }

    /// <summary>
    /// Standard output of a tool that exits 0 within the timeout, or null; at most <paramref name="limit"/> bytes, and
    /// a tool that would give more is stopped. Binary-safe, which <see cref="Proc"/>'s text result is not.
    /// </summary>
    private static byte[]? Capture(string file, string[] arguments, long limit)
    {
        var info = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(info);
            if (process is null)
            {
                return null;
            }

            using var timeout = new CancellationTokenSource(ToolTimeout);
            using var stop = timeout.Token.Register(() => Kill(process));
            process.StandardInput.Close();
            process.ErrorDataReceived += (_, _) => { };
            process.BeginErrorReadLine();
            using var output = new MemoryStream();
            var buffer = new byte[16 * 1024];
            int read;
            while ((read = process.StandardOutput.BaseStream.Read(buffer)) > 0)
            {
                output.Write(buffer, 0, read);
                if (output.Length >= limit)
                {
                    Kill(process);
                    break;
                }
            }

            process.WaitForExit();
            return output.Length >= limit || (process.ExitCode == 0 && !timeout.IsCancellationRequested)
                ? output.ToArray()
                : null;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
    }
}
