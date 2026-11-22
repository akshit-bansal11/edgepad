using Edgepad.Transfer;
using Edgepad.Ui;

namespace Edgepad.Platform;

/// <summary>
/// Where Linux keeps a file the phone sends: the Downloads folder the desktop names (<c>$XDG_DOWNLOAD_DIR</c>, else
/// <c>user-dirs.dirs</c>, else <c>~/Downloads</c>), under <c>Edgepad</c>. The receiver never overwrites and never opens what it
/// saves; this makes the file readable and writable by its owner only, and never executable.
/// </summary>
internal static class LinuxTransfers
{
    /// <summary>Where received files land.</summary>
    public static string Destination() => Path.Combine(SignatureExport.UserFolder("DOWNLOAD", "Downloads"), "Edgepad");

    /// <summary>Where a file still arriving, or cut short, is kept (private to the owner); the receiver deletes parts after a week.</summary>
    public static string PartialFolder()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var folder = Path.Combine(Xdg.Resolve(Environment.GetEnvironmentVariable("XDG_STATE_HOME"), home, ".local/state"), "edgepad", "partial");
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                _ = Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"The partial folder could not be made: {e.Message}");
        }

        return folder;
    }

    /// <summary>Owner read and write only: no execute bit, nothing for anyone else. A file that cannot be changed is logged and kept.</summary>
    internal static void Restrict(string file)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"A received file's permissions could not be set: {e.Message}");
        }
    }

    /// <summary>The one feed both directions report to; the card reads it.</summary>
    internal static readonly TransferFeed Feed = new();

    private static readonly ReceiveBatch Receiving = new(Feed);

    /// <summary>The receiver's tick: <paramref name="held"/> of <paramref name="size"/> bytes of <paramref name="name"/> are in.</summary>
    internal static void OnProgress(string name, ulong held, ulong size) => Receiving.Progress(name, held, size);

    /// <summary>A file from the phone did not arrive.</summary>
    internal static void OnFailed(string name, XferCode code)
    {
        Log.Write($"Receive from phone: {code}");
        Receiving.Failed(name, code);
    }

    /// <summary>Either end held or released the file in flight.</summary>
    internal static void OnPaused(bool held) => Feed.SetPaused(held);

    /// <summary>
    /// A file has arrived whole and checked. The file name is the phone's, already made safe by the receiver. The card says so;
    /// a notification does only when the card cannot be shown.
    /// </summary>
    public static void Arrived(string file)
    {
        Restrict(file);
        Receiving.Arrived(Path.GetFileName(file));

        if (!TransferCard.CanShow)
        {
            Notifier.Send(LinuxStrings.App_Name, Say.Format(LinuxStrings.Balloon_FileReceived, Say.MiddleTruncate(Path.GetFileName(file))));
        }
    }
}

/// <summary>
/// Turns what the receiver reports (a tick, an arrival, a failure) into one batch on the feed, and ends the batch once nothing
/// more comes: a short while after the last file, or past the receiver's idle drop when one went quiet. A send batch that is
/// showing is never touched.
/// </summary>
internal sealed class ReceiveBatch : IDisposable
{
    internal static readonly TimeSpan Linger = TimeSpan.FromMilliseconds(300);

    private readonly Lock gate = new();
    private readonly TransferFeed feed;
    private readonly Timer end;
    private bool open;
    private string? current;

    public ReceiveBatch(TransferFeed feed)
    {
        this.feed = feed;
        end = new Timer(_ => End(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Dispose() => end.Dispose();

    public void Progress(string name, ulong held, ulong size)
    {
        lock (gate)
        {
            if (Sending())
            {
                return;
            }

            Begin(name, size, incoming: true);
            feed.Bytes(held);
            Arm();
        }
    }

    public void Arrived(string name)
    {
        lock (gate)
        {
            if (Sending())
            {
                return;
            }

            Begin(name, 0, incoming: false);
            feed.FileDone(current ?? name, null);
            current = null;
            Arm();
        }
    }

    public void Failed(string name, XferCode code)
    {
        lock (gate)
        {
            if (Sending())
            {
                return;
            }

            Begin(name, 0, incoming: false);
            feed.FileDone(current ?? name, TransferOutcome.Of(code) ?? TransferFailure.LinkLost);
            current = null;
            Arm();
        }
    }

    private bool Sending() => feed.View is { Phase: not TransferPhase.Idle, Sending: true };

    // `incoming` is the name the phone sends under, and a new one there is a new file. An arrival's name can differ (a numbered
    // copy), so it only starts a file when none is open.
    private void Begin(string name, ulong size, bool incoming)
    {
        if (!open)
        {
            open = true;
            feed.BeginBatch(sending: false, 0);
        }

        if (current is null || (incoming && current != name))
        {
            current = name;
            feed.StartFile(name, size);
        }
    }

    private void Arm() => end.Change(current is null ? Linger : TransferLimits.IdleDrop + Linger, Timeout.InfiniteTimeSpan);

    private void End()
    {
        lock (gate)
        {
            if (!open)
            {
                return;
            }

            open = false;
            if (Sending())
            {
                current = null;
                return;
            }

            // A file still open went quiet past the receiver's idle drop with no word from it: it did not arrive.
            if (current is { } stalled)
            {
                feed.FileDone(stalled, TransferFailure.TimedOut);
            }

            current = null;
            feed.EndBatch();
        }
    }
}
