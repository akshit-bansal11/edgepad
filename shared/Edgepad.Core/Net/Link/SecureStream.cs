using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Edgepad.Net;

/// <summary>
/// The encrypted half of the network link: AES-256-GCM records over the raw socket stream, so that
/// <see cref="FrameStream"/> above it reads and writes frames exactly as it does over RFCOMM.
///
/// A record is <c>u16 length | ciphertext | 16-byte tag</c>, the length counting the tag and authenticated as the
/// record's associated data. The nonce is never sent: it is the direction's 4-byte prefix and a counter both ends
/// keep, so a record replayed, dropped or moved fails its tag like a forged one. Any record that fails, or a
/// length out of range, closes the link for good; nothing after it is read, and nothing more is sent.
///
/// One thread reads and any thread may write, as <see cref="FrameStream"/> does: each direction has its own lock,
/// cipher and counter. It owns the raw stream and closes it.
/// </summary>
internal sealed class SecureStream : Stream
{
    private readonly Stream inner;
    private readonly AesGcm sealer;
    private readonly AesGcm opener;
    private readonly byte[] sendPrefix;
    private readonly byte[] receivePrefix;

    private readonly Lock readGate = new();
    private readonly Lock writeGate = new();

    private readonly byte[] inRecord = new byte[LinkWire.MaxRecordPlain + LinkCrypto.AeadTagLength];
    private readonly byte[] plain = new byte[LinkWire.MaxRecordPlain];
    private readonly byte[] outRecord = new byte[2 + LinkWire.MaxRecordPlain + LinkCrypto.AeadTagLength];

    private ulong sent;
    private ulong received;
    private int plainOffset;
    private int plainCount;

    // Set by the first record that fails. A link that has carried one forged or misplaced record is not trusted
    // with another in either direction.
    private volatile bool failed;

    /// <param name="prk">The handshake's secret, from which both directions' keys are taken; the caller zeroes it.</param>
    /// <param name="isPhone">Which end this is: each sends with the other's receive key.</param>
    public SecureStream(Stream inner, ReadOnlySpan<byte> prk, bool isPhone)
    {
        this.inner = inner;
        var (p2l, l2p, iv) = LinkCrypto.SessionKeys(prk);
        try
        {
            sealer = new AesGcm(isPhone ? p2l : l2p, LinkCrypto.AeadTagLength);
            opener = new AesGcm(isPhone ? l2p : p2l, LinkCrypto.AeadTagLength);
            sendPrefix = isPhone ? iv[..4] : iv[4..];
            receivePrefix = isPhone ? iv[4..] : iv[..4];
        }
        finally
        {
            CryptographicOperations.ZeroMemory(p2l);
            CryptographicOperations.ZeroMemory(l2p);
            CryptographicOperations.ZeroMemory(iv);
        }
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    /// <summary>
    /// Whatever is left of the current record, up to <paramref name="buffer"/>'s length, reading the next record
    /// only when none is left. Zero only when the peer closed cleanly between records.
    /// </summary>
    public override int Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty)
        {
            return 0;
        }

        lock (readGate)
        {
            if (plainCount == 0 && !NextRecord())
            {
                return 0;
            }

            var n = Math.Min(buffer.Length, plainCount);
            plain.AsSpan(plainOffset, n).CopyTo(buffer);
            plainOffset += n;
            plainCount -= n;
            return n;
        }
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    /// <summary>Seals <paramref name="buffer"/> into as many records as it takes, each at most 16 KiB of plaintext.</summary>
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Span<byte> nonce = stackalloc byte[LinkCrypto.NonceLength];
        lock (writeGate)
        {
            while (!buffer.IsEmpty)
            {
                if (failed)
                {
                    throw new IOException("The link was closed after a record failed");
                }

                var chunk = buffer[..Math.Min(buffer.Length, LinkWire.MaxRecordPlain)];
                var length = chunk.Length + LinkCrypto.AeadTagLength;
                BinaryPrimitives.WriteUInt16LittleEndian(outRecord, (ushort)length);
                LinkCrypto.Nonce(sendPrefix, sent, nonce);

                // Spent before anything can fail: a nonce is sealed under once, whatever then happens to the record.
                sent++;
                LinkCrypto.Seal(
                    sealer,
                    nonce,
                    chunk,
                    outRecord.AsSpan(0, 2),
                    outRecord.AsSpan(2, chunk.Length),
                    outRecord.AsSpan(2 + chunk.Length, LinkCrypto.AeadTagLength));
                try
                {
                    inner.Write(outRecord, 0, 2 + length);
                }
                catch (Exception e)
                {
                    // Closed for good: part of a record may be on the wire, and the peer would read whatever came next as
                    // its continuation. A stream closed under this write (a failed read, the session ending) is reported
                    // as the IOException the sessions expect.
                    Fail();
                    if (e is ObjectDisposedException)
                    {
                        throw new IOException("The link is closed", e);
                    }

                    throw;
                }

                buffer = buffer[chunk.Length..];
            }
        }
    }

    public override void Flush()
    {
        lock (writeGate)
        {
            try
            {
                inner.Flush();
            }
            catch (Exception e)
            {
                Fail();
                if (e is ObjectDisposedException)
                {
                    throw new IOException("The link is closed", e);
                }

                throw;
            }
        }
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // No lock: a reader blocked on the socket holds the read lock until the close below ends its read.
            failed = true;
            inner.Dispose();
            opener.Dispose();
            sealer.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Reads and opens the next record into <see cref="plain"/>. False for a clean end, before a record's first byte;
    /// a stream that ends inside a record throws <see cref="EndOfStreamException"/>. Called under the read lock.
    /// </summary>
    private bool NextRecord()
    {
        if (failed)
        {
            throw new IOException("The link was closed after a record failed");
        }

        Span<byte> header = stackalloc byte[2];
        var first = inner.ReadByte();
        if (first < 0)
        {
            return false;
        }

        header[0] = (byte)first;
        inner.ReadExactly(header[1..]);
        var length = BinaryPrimitives.ReadUInt16LittleEndian(header);

        // A record always carries at least one byte: Write never seals an empty one.
        if (length <= LinkCrypto.AeadTagLength || length > inRecord.Length)
        {
            Fail();
            throw new InvalidDataException($"A link record of {length} bytes");
        }

        inner.ReadExactly(inRecord, 0, length);
        var plainLength = length - LinkCrypto.AeadTagLength;
        Span<byte> nonce = stackalloc byte[LinkCrypto.NonceLength];
        LinkCrypto.Nonce(receivePrefix, received, nonce);
        try
        {
            LinkCrypto.Open(
                opener,
                nonce,
                inRecord.AsSpan(0, plainLength),
                inRecord.AsSpan(plainLength, LinkCrypto.AeadTagLength),
                header,
                plain.AsSpan(0, plainLength));
        }
        catch (CryptographicException e)
        {
            Fail();
            throw new InvalidDataException("A link record failed its check: forged, replayed, reordered or corrupted", e);
        }

        received++;
        plainOffset = 0;
        plainCount = plainLength;
        return true;
    }

    private void Fail()
    {
        failed = true;
        inner.Dispose();
    }
}
