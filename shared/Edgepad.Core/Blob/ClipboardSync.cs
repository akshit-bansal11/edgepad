using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Unicode;
using Edgepad.Protocol;

namespace Edgepad.Blob;

/// <summary>What a clipboard holds. <c>Sensitive</c> is the platform's verdict; a flag it could not read counts as set.</summary>
internal sealed record ClipboardContent(BlobKind Kind, byte[] Data, bool Sensitive = false);

/// <summary>This OS's clipboard. Either call may block or throw: <see cref="ClipboardSync"/> never makes them on the read thread.</summary>
internal interface IClipboardSurface
{
    /// <summary>The clipboard now, or null for nothing that can be shared.</summary>
    ClipboardContent? Read();

    void Write(ClipboardContent content);
}

/// <summary>
/// The shared clipboard's rules, the same on every laptop: nothing moves unless the phone asked on this link and the
/// owner's own toggle is on, sensitive content never leaves, and a copy that came from the phone does not go back.
/// One per session, so what the phone asked for is forgotten with it.
/// <para>
/// A change here waits <see cref="DebounceMs"/> for the app that made it to finish, then goes on a thread of its own,
/// since a transfer is paced. A blob from the phone is checked on the read thread and written from another, because
/// the clipboard can be held by another app and a read loop that waited for it would stop the pointer too.
/// </para>
/// </summary>
internal sealed class ClipboardSync
{
    public const long MaxTextBytes = 1024 * 1024;
    public const long MaxImageBytes = 4 * 1024 * 1024;

    /// <summary>A small PNG can inflate to any size, and each platform decodes it to write it. 8K is 33 million.</summary>
    public const ulong MaxImagePixels = 50_000_000;

    public const int DebounceMs = 150;

    /// <summary>
    /// How long after a write the clipboard is not read for sending. The hash cannot catch every echo of our own write:
    /// a platform that hands an image back re-encoded has different bytes.
    /// </summary>
    public const long WriteQuietMs = 500;

    /// <summary>The label is for kinds that need a name; a copy has none.</summary>
    private const string NoLabel = "";

    /// <summary>The PNG signature, then IHDR's length and name, then its width and height.</summary>
    private const int PngHeaderBytes = 24;

    private readonly Lock gate = new();
    private readonly IClipboardSurface surface;
    private readonly BlobSender sender;
    private readonly Func<bool> allowed;
    private readonly CancellationToken ended;
    private readonly Func<long> now;
    private readonly Action<int, Action> schedule;
    private readonly BlobReceiver receiver;

    private volatile bool shared;
    private int changes;

    /// <summary>The SHA-256 of what both ends last agreed on, so neither sends it to the other again.</summary>
    private byte[]? known;

    /// <summary>The latest blob waiting to be written. A newer one replaces it.</summary>
    private ClipboardContent? incoming;

    private long quietUntil;

    /// <param name="surface">This OS's clipboard.</param>
    /// <param name="sender">The session's sender.</param>
    /// <param name="allowed">The owner's own toggle, read every time, since it can change during a session.</param>
    /// <param name="ended">The session's end.</param>
    /// <param name="clock">Milliseconds, for tests.</param>
    /// <param name="later">Runs work off the read thread after a delay, for tests.</param>
    public ClipboardSync(
        IClipboardSurface surface,
        BlobSender sender,
        Func<bool> allowed,
        CancellationToken ended,
        Func<long>? clock = null,
        Action<int, Action>? later = null)
    {
        this.surface = surface;
        this.sender = sender;
        this.allowed = allowed;
        this.ended = ended;
        now = clock ?? (() => Environment.TickCount64);
        schedule = later ?? Later;
        receiver = new BlobReceiver(Cap, Received);
    }

    private bool Active => shared && !ended.IsCancellationRequested && allowed();

    /// <summary>
    /// SET CLIPBOARD. False, dropped and counted, for a value that is neither 0 nor 1, and for a 1 while the owner's
    /// toggle is off. Turning it on sends nothing: only the next copy goes, so a laptop just selected learns nothing old.
    /// </summary>
    public bool SetShared(byte value)
    {
        if (value > 1 || (value == 1 && !allowed()))
        {
            return false;
        }

        shared = value == 1;
        if (!shared)
        {
            receiver.Reset();
            lock (gate)
            {
                incoming = null;
                known = null;
            }
        }

        return true;
    }

    /// <summary>The platform saw this clipboard change. Any thread; returns at once.</summary>
    public void LocalChanged()
    {
        if (Active)
        {
            var change = Interlocked.Increment(ref changes);
            schedule(DebounceMs, () => Guarded(() => Flush(change)));
        }
    }

    public bool Take(BlobBegin begin) => Active ? receiver.Take(begin) : Refuse();

    public bool Take(BlobChunk chunk) => Active ? receiver.Take(chunk) : Refuse();

    public bool Take(BlobEnd end) => Active ? receiver.Take(end) : Refuse();

    private static long? Cap(byte kind) => (BlobKind)kind switch
    {
        BlobKind.ClipText => MaxTextBytes,
        BlobKind.ClipImage => MaxImageBytes,
        _ => null,
    };

    private static bool Valid(BlobKind kind, byte[] data) => data.Length > 0 && kind switch
    {
        BlobKind.ClipText => Utf8.IsValid(data),
        BlobKind.ClipImage => IsPng(data),
        _ => false,
    };

    /// <summary>A PNG whose header is one this laptop will decode. The rest of the file is the platform decoder's to refuse.</summary>
    private static bool IsPng(ReadOnlySpan<byte> data)
    {
        ReadOnlySpan<byte> start = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52];
        if (data.Length < PngHeaderBytes || !data.StartsWith(start))
        {
            return false;
        }

        ulong width = BinaryPrimitives.ReadUInt32BigEndian(data[start.Length..]);
        ulong height = BinaryPrimitives.ReadUInt32BigEndian(data[(start.Length + sizeof(uint))..]);
        return width * height is > 0 and <= MaxImagePixels;
    }

    private static void Later(int ms, Action work) =>
        _ = Task.Delay(ms).ContinueWith(_ => work(), TaskScheduler.Default);

    /// <summary>The last stop before a thread with nothing above it, so a clipboard that fails costs one copy and no more.</summary>
    private static void Guarded(Action work)
    {
        try
        {
            work();
        }
        catch (Exception e)
        {
            Log.Write($"Clipboard: {e.GetType().Name}: {e.Message}");
        }
    }

    private bool Refuse()
    {
        receiver.Reset();
        return false;
    }

    private bool Received(byte kind, byte[] data, byte[] sha256)
    {
        if (!Valid((BlobKind)kind, data))
        {
            return false;
        }

        lock (gate)
        {
            known = sha256;
            incoming = new ClipboardContent((BlobKind)kind, data);
        }

        schedule(0, () => Guarded(Apply));
        return true;
    }

    private void Apply()
    {
        ClipboardContent? content;
        lock (gate)
        {
            content = incoming;
            incoming = null;
        }

        if (content is null || !Active)
        {
            return;
        }

        surface.Write(content);
        lock (gate)
        {
            quietUntil = now() + WriteQuietMs;
        }
    }

    private void Flush(int change)
    {
        if (change != Volatile.Read(ref changes) || !Active)
        {
            return;
        }

        lock (gate)
        {
            if (now() < quietUntil)
            {
                return;
            }
        }

        if (surface.Read() is not { Sensitive: false } content || content.Data.Length == 0)
        {
            return;
        }

        if (Cap((byte)content.Kind) is not { } cap || content.Data.Length > cap)
        {
            Log.Write($"Clipboard: {content.Data.Length} bytes of {content.Kind} is over the cap, so it stays here");
            return;
        }

        var hash = SHA256.HashData(content.Data);
        lock (gate)
        {
            if (known is not null && hash.AsSpan().SequenceEqual(known))
            {
                return;
            }

            known = hash;
        }

        var sent = false;
        try
        {
            sent = sender.Send(content.Kind, NoLabel, content.Data, () => !Active);
        }
        finally
        {
            if (!sent)
            {
                Forget(hash);
            }
        }
    }

    /// <summary>The phone never got this, so the same content copied again must go. A blob since then stays known.</summary>
    private void Forget(byte[] hash)
    {
        lock (gate)
        {
            if (ReferenceEquals(known, hash))
            {
                known = null;
            }
        }
    }
}
