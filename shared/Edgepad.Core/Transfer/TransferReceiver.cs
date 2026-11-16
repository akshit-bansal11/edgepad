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
/// Marking the file (Mark of the Web, the quarantine attribute) is the platform's, in <c>arrived</c>.
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
    private Active? active;

    public TransferReceiver(
        string destination,
        string partialFolder,
        Action<XferStatus> reply,
        Action<string>? arrived = null,
        Func<string, long>? freeSpace = null,
        Func<DateTimeOffset>? now = null)
    {
        this.destination = destination;
        this.partialFolder = partialFolder;
        this.reply = reply;
        this.arrived = arrived;
        this.freeSpace = freeSpace ?? FreeSpaceOf;
        Purge((now ?? (() => DateTimeOffset.UtcNow))());
    }

    /// <summary>Whether a file is arriving.</summary>
    public bool Busy
    {
        get
        {
            lock (gate)
            {
                return active is not null;
            }
        }
    }

    public bool Take(XferBegin begin)
    {
        lock (gate)
        {
            if (active is not null)
            {
                Reply(begin.Id, XferCode.Busy, 0);
                return true;
            }

            var name = SafeFileName.Clean(begin.Name);
            if (name is null || begin.Size > TransferLimits.MaxBytes)
            {
                Reply(begin.Id, XferCode.Rejected, 0);
                return true;
            }

            try
            {
                Directory.CreateDirectory(destination);
                Directory.CreateDirectory(partialFolder);
                if (freeSpace(destination) < (long)begin.Size + TransferLimits.FreeSpaceMargin)
                {
                    Reply(begin.Id, XferCode.Rejected, 0);
                    return true;
                }

                Open(begin, name);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Reply(begin.Id, XferCode.WriteFailed, 0);
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

            if (chunk.Offset != a.Held)
            {
                Reply(a.Id, XferCode.Progress, a.Held);
                return true;
            }

            if (a.Held + (ulong)chunk.Data.Length > a.Size)
            {
                Finish(deletePart: true);
                Reply(chunk.Id, XferCode.Rejected, 0);
                return true;
            }

            try
            {
                a.File.Write(chunk.Data);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Finish(deletePart: true);
                Reply(chunk.Id, XferCode.WriteFailed, 0);
                return true;
            }

            a.Hash.AppendData(chunk.Data);
            a.Held += (ulong)chunk.Data.Length;
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
                Reply(end.Id, XferCode.BadHash, 0);
                return true;
            }

            Save(a);
            return true;
        }
    }

    /// <summary>The sender's CANCELLED. Any other status is the sender's to receive, not this side's.</summary>
    public bool Take(XferStatus status)
    {
        lock (gate)
        {
            if (status.Status != (byte)XferCode.Cancelled || active is not { } a || a.Id != status.Id)
            {
                return false;
            }

            Finish(deletePart: true);
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
            Reply(a.Id, XferCode.Cancelled, 0);
            return true;
        }
    }

    /// <summary>The session is over: the part stays, for the next BEGIN of the same name and size.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            Finish(deletePart: false);
        }
    }

    private static long FreeSpaceOf(string folder) => new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!).AvailableFreeSpace;

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
            active = new Active(begin.Id, begin.Size, name, partPath, file, hash) { Held = held, Reported = held };
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
            Reply(id, XferCode.WriteFailed, 0);
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
        a.File.Dispose();
        a.Hash.Dispose();
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
    }
}
