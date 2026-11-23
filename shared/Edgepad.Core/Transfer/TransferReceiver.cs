using System.Security.Cryptography;
using Edgepad.Protocol;
using static System.FormattableString;

namespace Edgepad.Transfer;

/// <summary>
/// Takes one file at a time from the peer, in either direction of a session: XFER_BEGIN, XFER_CHUNKs and XFER_END in, XFER_STATUS
/// out. Chunks must arrive contiguously; one that does not is answered with the bytes held, and the sender restarts there. The
/// file grows as <c>name.size.part</c> in <c>partialFolder</c>, hashed as it arrives, and is only renamed into
/// <c>destination</c> (never over a file, never opened) when the peer's SHA-256 matches. A session that ends keeps the part,
/// and a begin for the same name and size resumes it; parts older than <see cref="TransferLimits.PartialKept"/> go when this
/// is created. Called on the session's read thread; <see cref="Cancel"/> may come from any other.
/// <para>
/// Marking the file (Mark of the Web, the quarantine attribute) is the platform's, in <c>beforeSave</c>, which gets the part before
/// it is moved into <c>destination</c>: a file there is never seen unmarked.
/// </para>
/// </summary>
internal sealed class TransferReceiver : IDisposable
{
    private readonly Lock gate = new();
    private readonly string destination;
    private readonly string partialFolder;
    private readonly Action<XferStatus> reply;
    private readonly Action<string>? arrived;
    private readonly Func<string, long> freeSpace;
    private readonly Func<DateTimeOffset> clock;
    private readonly Action<string>? beforeSave;
    private readonly Action<string, ulong, ulong>? progress;
    private readonly Action<string, XferCode>? failed;
    private readonly Action<bool>? pausedChanged;
    private readonly TimeSpan holdWatch;
    private Active? active;

    /// <summary>The name the failed hook gives a file whose own name could not be made safe; language-neutral.</summary>
    internal const string UnnamedFile = "…";

    public TransferReceiver(
        string destination,
        string partialFolder,
        Action<XferStatus> reply,
        Action<string>? arrived = null,
        Func<string, long>? freeSpace = null,
        Func<DateTimeOffset>? now = null,
        Action<string>? beforeSave = null,
        Action<string, ulong, ulong>? progress = null,
        Action<string, XferCode>? failed = null,
        Action<bool>? pausedChanged = null,
        TimeSpan? holdWatch = null)
    {
        this.pausedChanged = pausedChanged;
        this.holdWatch = holdWatch ?? TimeSpan.FromSeconds(5);
        this.destination = destination;
        this.partialFolder = partialFolder;
        this.reply = reply;
        this.arrived = arrived;
        this.freeSpace = freeSpace ?? FreeSpaceOf;
        this.beforeSave = beforeSave;
        this.progress = progress;
        this.failed = failed;
        clock = now ?? (() => DateTimeOffset.UtcNow);
        Purge(clock());
    }

    /// <summary>Whether a file is arriving.</summary>
    public bool Busy
    {
        get
        {
            lock (gate)
            {
                DropIfIdle();
                return active is not null;
            }
        }
    }

    public bool Take(XferBegin begin)
    {
        lock (gate)
        {
            DropIfIdle();
            var name = SafeFileName.Clean(begin.Name);
            if (active is { } current)
            {
                // The same file begun again while held: the sender gave up first (its cap runs out before ours) and is
                // back. The held transfer is let go, its part kept, and the begin resumes from it.
                if (current.HeldSince is null || name is null || name != current.Name || begin.Size != current.Size)
                {
                    Reply(begin.Id, XferCode.Busy, 0);
                    return true;
                }

                Finish(deletePart: false);
            }

            // The phone's raw name (control, format, bidi characters) never reaches the UI: the cleaned one or a stand-in.
            var shown = name ?? UnnamedFile;
            if (name is null || begin.Size > TransferLimits.MaxBytes)
            {
                Fail(begin.Id, shown, XferCode.Rejected);
                return true;
            }

            try
            {
                Directory.CreateDirectory(destination);
                Directory.CreateDirectory(partialFolder);
                // The part file grows in partialFolder, which can be another volume than the destination it is renamed into.
                var need = (long)begin.Size + TransferLimits.FreeSpaceMargin;
                if (freeSpace(destination) < need || freeSpace(partialFolder) < need)
                {
                    Fail(begin.Id, shown, XferCode.Rejected);
                    return true;
                }

                Open(begin, name);
            }
            catch (ArgumentException)
            {
                // A destination the volume probe cannot name (a share): free space is unknown, so the file is refused.
                Fail(begin.Id, shown, XferCode.Rejected);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Fail(begin.Id, shown, XferCode.WriteFailed);
            }

            return true;
        }
    }

    public bool Take(XferChunk chunk)
    {
        lock (gate)
        {
            if (active is not { } a || a.Id != chunk.Id)
            {
                return false;
            }

            a.LastChunk = clock();

            if (chunk.Offset != a.Held)
            {
                Reply(a.Id, XferCode.Progress, a.Held);
                return true;
            }

            if (a.Held + (ulong)chunk.Data.Length > a.Size)
            {
                Finish(deletePart: true);
                Fail(chunk.Id, a.Name, XferCode.Rejected);
                return true;
            }

            try
            {
                a.File.Write(chunk.Data);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Finish(deletePart: true);
                Fail(chunk.Id, a.Name, XferCode.WriteFailed);
                return true;
            }

            a.Hash.AppendData(chunk.Data);
            a.Held += (ulong)chunk.Data.Length;
            progress?.Invoke(a.Name, a.Held, a.Size);
            if (a.Held - a.Reported >= TransferLimits.ProgressEveryBytes)
            {
                a.Reported = a.Held;
                Reply(a.Id, XferCode.Progress, a.Held);
            }

            return true;
        }
    }

    public bool Take(XferEnd end)
    {
        lock (gate)
        {
            if (active is not { } a || a.Id != end.Id)
            {
                return false;
            }

            // Short of the size is not a bad file, it is not all of it yet: say where it stands and let the sender go on.
            if (a.Held != a.Size)
            {
                Reply(a.Id, XferCode.Progress, a.Held);
                return true;
            }

            if (!CryptographicOperations.FixedTimeEquals(a.Hash.GetCurrentHash(), end.Sha256))
            {
                Finish(deletePart: true);
                Fail(end.Id, a.Name, XferCode.BadHash);
                return true;
            }

            Save(a);
            return true;
        }
    }

    /// <summary>
    /// The sender's CANCELLED, PAUSED or RESUME. Any other status is the sender's to receive, not this side's. PAUSED holds the
    /// transfer here too (no idle drop). RESUME is answered with READY at the bytes held, unless this end holds it as well,
    /// when <see cref="Resume"/> answers.
    /// </summary>
    public bool Take(XferStatus status)
    {
        lock (gate)
        {
            if (active is not { } a || a.Id != status.Id)
            {
                return false;
            }

            switch (status.Status)
            {
                case (byte)XferCode.Cancelled:
                    Finish(deletePart: true);
                    failed?.Invoke(a.Name, XferCode.Cancelled);
                    return true;
                case (byte)XferCode.Paused:
                    SetHold(a, peer: true);
                    return true;
                case (byte)XferCode.Resume:
                    SetHold(a, peer: false);
                    a.LastChunk = clock();
                    if (!a.Paused)
                    {
                        Reply(a.Id, XferCode.Ready, a.Held);
                    }

                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// Holds the file arriving: the sender is told with PAUSED and stops after its chunk in flight, which is still taken; the
    /// idle drop stops until <see cref="Resume"/>. False when none arrives or it is held already.
    /// </summary>
    public bool Pause()
    {
        lock (gate)
        {
            DropIfIdle();
            if (active is not { } a || a.Paused)
            {
                return false;
            }

            SetHold(a, own: true);
            Reply(a.Id, XferCode.Paused, a.Held);
            return true;
        }
    }

    /// <summary>
    /// Lets go of this end's hold: RESUME, then READY at the bytes held so the sender goes on from there. Where the sender
    /// still holds it, only RESUME goes and the sender's own RESUME is answered with the READY. False when this end does not hold it.
    /// </summary>
    public bool Resume()
    {
        lock (gate)
        {
            DropIfIdle();
            if (active is not { } a || !a.Paused)
            {
                return false;
            }

            SetHold(a, own: false);
            a.LastChunk = clock();
            Reply(a.Id, XferCode.Resume, a.Held);
            if (!a.PeerPaused)
            {
                Reply(a.Id, XferCode.Ready, a.Held);
            }

            return true;
        }
    }

    /// <summary>Stops the file arriving, deletes its part and tells the sender. False when none is.</summary>
    public bool Cancel()
    {
        lock (gate)
        {
            if (active is not { } a)
            {
                return false;
            }

            Finish(deletePart: true);
            Fail(a.Id, a.Name, XferCode.Cancelled);
            return true;
        }
    }

    /// <summary>
    /// The session is over: the part stays, for the next BEGIN of the same name and size. A file still arriving is reported
    /// to the failed hook as SESSION_ENDED.
    /// </summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (active is { } a)
            {
                Finish(deletePart: false);
                failed?.Invoke(a.Name, XferCode.SessionEnded);
            }
        }
    }

    /// <summary>
    /// The free bytes on the volume that holds <paramref name="folder"/>, or -1 when they cannot be read (a share, a drive that
    /// is not ready, anything unknown), which the caller refuses on.
    /// </summary>
    private static long FreeSpaceOf(string folder)
    {
        try
        {
            var mounts = DriveInfo.GetDrives().Select(d => (d.RootDirectory.FullName, (Func<long>)(() => d.IsReady ? d.AvailableFreeSpace : -1))).ToList();
            return FreeSpaceFor(Path.GetFullPath(folder), mounts);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return -1;
        }
    }

    /// <summary>
    /// The free bytes of the mount whose root is the longest prefix of <paramref name="folder"/> (a separate /home beats /),
    /// or -1 when none holds it. The root counts only at a separator, so /home does not hold /homework. Only that mount's
    /// free space is read, so a stale mount elsewhere (a dead FUSE share) that throws on being read cannot refuse the file.
    /// </summary>
    internal static long FreeSpaceFor(string folder, IReadOnlyList<(string Root, Func<long> Free)> mounts)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var best = -1;
        for (var i = 0; i < mounts.Count; i++)
        {
            var root = mounts[i].Root;
            var holds = folder.StartsWith(root, comparison)
                && (folder.Length == root.Length || root[^1] == Path.DirectorySeparatorChar || folder[root.Length] == Path.DirectorySeparatorChar);
            if (holds && (best < 0 || root.Length > mounts[best].Root.Length))
            {
                best = i;
            }
        }

        return best < 0 ? -1 : mounts[best].Free();
    }

    /// <summary>
    /// A transfer that has had no chunk for <see cref="TransferLimits.IdleDrop"/> is let go (its part stays, for a resume), so a
    /// sender that gave up never leaves the receiver answering BUSY, and a sender still there is told with CANCELLED. The owner
    /// hears it as TIMED_OUT.
    /// Called with the gate held.
    /// </summary>
    private void DropIfIdle()
    {
        if (active is not { } a)
        {
            return;
        }

        // Held by either end: no idle drop, only the cap, and that ends it as CANCELLED, the part kept.
        if (a.HeldSince is { } since)
        {
            if (clock() - since > TransferLimits.PauseCap)
            {
                Finish(deletePart: false);
                Reply(a.Id, XferCode.Cancelled, 0);
                failed?.Invoke(a.Name, XferCode.Cancelled);
            }

            return;
        }

        if (clock() - a.LastChunk > TransferLimits.IdleDrop)
        {
            Finish(deletePart: false);
            Reply(a.Id, XferCode.Cancelled, 0);
            failed?.Invoke(a.Name, XferCode.TimedOut);
        }
    }

    private void Watch(Active a)
    {
        lock (gate)
        {
            if (ReferenceEquals(active, a))
            {
                DropIfIdle();
            }
        }
    }

    // Under the gate: sets this end's hold, the sender's, or both, and tells the owner when the file starts or stops being held.
    private void SetHold(Active a, bool? own = null, bool? peer = null)
    {
        var was = a.Paused || a.PeerPaused;
        a.Paused = own ?? a.Paused;
        a.PeerPaused = peer ?? a.PeerPaused;
        var held = a.Paused || a.PeerPaused;
        a.HeldSince = held ? a.HeldSince ?? clock() : null;
        if (held != was)
        {
            // While held nothing else would look at the clock, so a timer does: the cap shows without a touch.
            a.Watch?.Dispose();
            a.Watch = held ? new Timer(_ => Watch(a), null, holdWatch, holdWatch) : null;
            pausedChanged?.Invoke(held);
        }
    }

    private void Open(XferBegin begin, string name)
    {
        var partPath = Path.Combine(partialFolder, Invariant($"{name}.{begin.Size}{TransferLimits.PartialExtension}"));
        var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        FileStream? file = null;
        try
        {
            file = new FileStream(partPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if ((ulong)file.Length > begin.Size)
            {
                file.SetLength(0);
            }

            // What is held is hashed again from disk: the digest at the far end is of the whole file, not of this run.
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = file.Read(buffer)) > 0)
            {
                hash.AppendData(buffer.AsSpan(0, read));
            }

            var held = (ulong)file.Length;
            active = new Active(begin.Id, begin.Size, name, partPath, file, hash) { Held = held, Reported = held, LastChunk = clock() };
            Reply(begin.Id, XferCode.Ready, held);
        }
        catch
        {
            file?.Dispose();
            hash.Dispose();
            active = null;
            throw;
        }
    }

    private void Save(Active a)
    {
        var id = a.Id;
        var size = a.Size;
        string? finalPath = null;
        try
        {
            a.File.Flush(flushToDisk: true);
            a.File.Dispose();
            beforeSave?.Invoke(a.PartPath);
            while (finalPath is null)
            {
                var candidate = Path.Combine(destination, SafeFileName.Unique(a.Name, n => File.Exists(Path.Combine(destination, n))));

                // Move without overwrite: a file that appeared since the check makes this throw, and the loop names around it.
                try
                {
                    File.Move(a.PartPath, candidate, overwrite: false);
                    finalPath = candidate;
                }
                catch (IOException) when (File.Exists(candidate))
                {
                    // Taken in the meantime: the next pass names around it.
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Finish(deletePart: false);
            Fail(id, a.Name, XferCode.WriteFailed);
            return;
        }

        Finish(deletePart: false);
        Reply(id, XferCode.Saved, size);
        arrived?.Invoke(finalPath);
    }

    private void Finish(bool deletePart)
    {
        if (active is not { } a)
        {
            return;
        }

        active = null;
        a.Watch?.Dispose();
        a.File.Dispose();
        a.Hash.Dispose();
        if (a.HeldSince is not null)
        {
            pausedChanged?.Invoke(false);
        }

        if (!deletePart)
        {
            return;
        }

        try
        {
            File.Delete(a.PartPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A part that will not delete is purged by age.
        }
    }

    private void Purge(DateTimeOffset now)
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(partialFolder, "*" + TransferLimits.PartialExtension))
            {
                if (now - File.GetLastWriteTimeUtc(path) > TransferLimits.PartialKept)
                {
                    File.Delete(path);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // No folder yet, or one in use: nothing to purge, or it is purged next time.
        }
    }

    /// <summary>Tells the sender and the owner's UI that a file ended badly. Called with the gate held, so the callee only enqueues.</summary>
    private void Fail(byte id, string name, XferCode code)
    {
        Reply(id, code, 0);
        failed?.Invoke(name, code);
    }

    private void Reply(byte id, XferCode code, ulong offset) => reply(new XferStatus(id, (byte)code, offset));

    private sealed class Active(byte id, ulong size, string name, string partPath, FileStream file, IncrementalHash hash)
    {
        public byte Id { get; } = id;

        public ulong Size { get; } = size;

        public string Name { get; } = name;

        public string PartPath { get; } = partPath;

        public FileStream File { get; } = file;

        public IncrementalHash Hash { get; } = hash;

        public ulong Held { get; set; }

        public ulong Reported { get; set; }

        public DateTimeOffset LastChunk { get; set; }

        /// <summary>This end holds the file.</summary>
        public bool Paused { get; set; }

        /// <summary>The sender holds the file.</summary>
        public bool PeerPaused { get; set; }

        /// <summary>When the hold began, or null when no end holds the file.</summary>
        public DateTimeOffset? HeldSince { get; set; }

        /// <summary>Looks at the cap while held; null otherwise.</summary>
        public Timer? Watch { get; set; }
    }
}
