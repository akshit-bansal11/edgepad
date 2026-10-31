using System.Buffers;
using System.Security.Cryptography;
using Edgepad.Protocol;

namespace Edgepad.Blob;

/// <summary>
/// Puts one blob back together from what the phone sent, and trusts none of it. Nothing is allocated from the size a
/// BLOB_BEGIN claims: the buffer grows with the bytes that arrived and stops at that size, which <paramref name="cap"/>
/// has already bounded, so at most one capped blob is held at a time.
/// <para>
/// A blob that goes wrong is drained to its BLOB_END and dropped, and the next BLOB_BEGIN starts clean: the framing was
/// valid, so the link stays up. A refused blob answers false once, for the dispatcher's count, and is quiet after
/// that. Every method runs on the session's read thread and none of them throws.
/// </para>
/// </summary>
/// <param name="cap">The most bytes a kind may carry, or null for a kind this side does not take.</param>
/// <param name="completed">Handed the kind, the bytes and their checked SHA-256; false drops and counts them.</param>
internal sealed class BlobReceiver(Func<byte, long?> cap, Func<byte, byte[], byte[], bool> completed)
{
    /// <summary>All the blob bytes one connection may send, kept or drained. Past it every blob is refused.</summary>
    public const long MaxConnectionBytes = 256L * 1024 * 1024;

    private ArrayBufferWriter<byte>? buffer;
    private bool draining;
    private byte kind;
    private long size;
    private byte seq;
    private long spent;

    /// <summary>Starts a blob, aborting any still open: the sender gave up on that one.</summary>
    public bool Take(BlobBegin begin)
    {
        Reset();
        if (cap(begin.Kind) is not { } limit
            || begin.Size > limit
            || spent + begin.Size > MaxConnectionBytes
            || begin.Label.Any(char.IsControl))
        {
            draining = true;
            return false;
        }

        kind = begin.Kind;
        size = begin.Size;
        seq = 0;
        buffer = new ArrayBufferWriter<byte>();
        return true;
    }

    public bool Take(BlobChunk chunk)
    {
        spent += chunk.Data.Length;
        if (draining)
        {
            return true;
        }

        if (buffer is null)
        {
            return false;
        }

        // An empty chunk is refused so the number of chunks is bounded by the size, not only their bytes.
        if (chunk.Data.Length == 0 || chunk.Seq != seq || buffer.WrittenCount + chunk.Data.Length > size)
        {
            buffer = null;
            draining = true;
            return false;
        }

        buffer.Write<byte>(chunk.Data);
        seq++;
        return true;
    }

    public bool Take(BlobEnd end)
    {
        var open = buffer;
        var drained = draining;
        Reset();
        if (drained)
        {
            return true;
        }

        if (open is null || open.WrittenCount != size)
        {
            return false;
        }

        var data = open.WrittenSpan.ToArray();
        return SHA256.HashData(data).AsSpan().SequenceEqual(end.Sha256) && completed(kind, data, end.Sha256);
    }

    /// <summary>Forgets any blob in progress. What this connection has already spent stays spent.</summary>
    public void Reset()
    {
        buffer = null;
        draining = false;
    }
}
