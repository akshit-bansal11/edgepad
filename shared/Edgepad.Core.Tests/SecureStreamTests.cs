using Edgepad.Net;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// The record layer on its own: what the phone end seals is captured as bytes, changed or not, and handed to a laptop
/// end to open. Any record that is not exactly the next one the phone sent must close the link.
/// </summary>
public sealed class SecureStreamTests
{
    private static readonly byte[] Prk = [.. Enumerable.Range(0, 32).Select(i => (byte)i)];
    private static readonly byte[] One = [1];
    private static readonly byte[] Two = [2];
    private static readonly byte[] Three = [1, 2, 3];

    // What the phone end puts on the wire for each write, one array per write.
    private static List<byte[]> Sealed(params byte[][] writes)
    {
        using var wire = new MemoryStream();
        using var phone = new SecureStream(new Untouched(wire), Prk, isPhone: true);
        List<byte[]> records = [];
        foreach (var write in writes)
        {
            var before = (int)wire.Length;
            phone.Write(write);
            records.Add(wire.ToArray()[before..]);
        }

        return records;
    }

    private static SecureStream Laptop(params byte[][] records) =>
        new(new MemoryStream([.. records.SelectMany(r => r)]), Prk, isPhone: false);

    [Fact]
    public void OneByteAndFortyKilobytesComeThroughWhole()
    {
        byte[] one = [42];
        byte[] big = [.. Enumerable.Range(0, 40 * 1024).Select(i => (byte)(i * 7))];
        var records = Sealed(one, big);

        // 40 KB is three records: two full ones of 16 KiB and the rest, each with its length and tag.
        Assert.Equal(big.Length + (3 * (2 + LinkCrypto.AeadTagLength)), records[1].Length);

        using var laptop = Laptop([.. records]);
        Assert.Equal(42, laptop.ReadByte());
        var read = new byte[big.Length];
        laptop.ReadExactly(read);
        Assert.Equal(big, read);
        Assert.Equal(0, laptop.Read(new byte[1]));
    }

    [Fact]
    public void ReadsHandOutPartOfARecordAndKeepTheRest()
    {
        byte[] five = [1, 2, 3, 4, 5];
        using var laptop = Laptop([.. Sealed(five)]);
        var first = new byte[2];
        Assert.Equal(2, laptop.Read(first));
        byte[] firstTwo = [1, 2];
        Assert.Equal(firstTwo, first);
        var rest = new byte[10];
        Assert.Equal(3, laptop.Read(rest));
        byte[] theRest = [3, 4, 5];
        Assert.Equal(theRest, rest[..3]);
    }

    [Fact]
    public void ATamperedRecordCloses()
    {
        var records = Sealed(Three);
        records[0][3] ^= 0x80;

        using var laptop = Laptop([.. records]);
        Assert.Throws<InvalidDataException>(() => laptop.ReadByte());
        Assert.Throws<IOException>(() => laptop.ReadByte());
        Assert.Throws<IOException>(() => laptop.Write(One));
    }

    [Fact]
    public void ATamperedLengthCloses()
    {
        var records = Sealed(Three);

        // Still in range, so the record is read; the length is authenticated, so it fails.
        records[0][0]--;
        using var laptop = Laptop(records[0][..^1]);
        Assert.Throws<InvalidDataException>(() => laptop.ReadByte());
    }

    [Fact]
    public void RecordsOutOfOrderClose()
    {
        var records = Sealed(One, Two);

        using var laptop = Laptop(records[1], records[0]);
        Assert.Throws<InvalidDataException>(() => laptop.ReadByte());
    }

    [Fact]
    public void AReplayedRecordCloses()
    {
        var records = Sealed(One, Two);

        using var laptop = Laptop(records[0], records[0]);
        Assert.Equal(1, laptop.ReadByte());
        Assert.Throws<InvalidDataException>(() => laptop.ReadByte());
    }

    [Fact]
    public void ATruncatedRecordEndsTheStream()
    {
        var records = Sealed(Three);

        using var laptop = Laptop(records[0][..^1]);
        Assert.Throws<EndOfStreamException>(() => laptop.ReadByte());
    }

    [Fact]
    public void ALengthOutOfRangeCloses()
    {
        byte[] tagOnly = [16, 0];
        using var empty = Laptop(tagOnly);
        Assert.Throws<InvalidDataException>(() => empty.ReadByte());

        byte[] tooLong = [0xff, 0xff];
        using var huge = Laptop(tooLong);
        Assert.Throws<InvalidDataException>(() => huge.ReadByte());
    }

    [Fact]
    public void ARecordSealedForTheOtherDirectionDoesNotOpen()
    {
        // The laptop sealing to itself: right keys, wrong direction.
        using var wire = new MemoryStream();
        using (var laptop = new SecureStream(new Untouched(wire), Prk, isPhone: false))
        {
            laptop.Write(One);
        }

        using var reader = Laptop(wire.ToArray());
        Assert.Throws<InvalidDataException>(() => reader.ReadByte());
    }

    /// <summary>A stream that ignores Dispose, so what was written can still be read back after the writer is closed.</summary>
    private sealed class Untouched(MemoryStream inner) : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
