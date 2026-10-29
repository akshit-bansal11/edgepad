namespace Edgepad.Tests;

/// <summary>
/// Two connected in-memory ends, as a socket gives a phone and a laptop: what one writes the other reads, a read
/// blocks until there is something, and disposing either end closes both directions, so a blocked read ends the way
/// a closed socket ends it. A read that waits more than ten seconds fails the test instead of hanging it.
/// </summary>
internal static class Duplex
{
    public static (Stream Phone, Stream Laptop) Create()
    {
        var toLaptop = new Lane();
        var toPhone = new Lane();
        return (new End(toPhone, toLaptop), new End(toLaptop, toPhone));
    }

    private sealed class Lane
    {
        private readonly Queue<byte> bytes = new();
        private readonly object gate = new();
        private bool closed;

        public void Write(ReadOnlySpan<byte> data)
        {
            lock (gate)
            {
                if (closed)
                {
                    throw new IOException("The other end is closed");
                }

                foreach (var b in data)
                {
                    bytes.Enqueue(b);
                }

                Monitor.PulseAll(gate);
            }
        }

        public int Read(Span<byte> buffer)
        {
            lock (gate)
            {
                while (bytes.Count == 0 && !closed)
                {
                    if (!Monitor.Wait(gate, TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException("Nothing arrived within ten seconds");
                    }
                }

                var n = Math.Min(buffer.Length, bytes.Count);
                for (var i = 0; i < n; i++)
                {
                    buffer[i] = bytes.Dequeue();
                }

                return n;
            }
        }

        public void Close()
        {
            lock (gate)
            {
                closed = true;
                Monitor.PulseAll(gate);
            }
        }
    }

    private sealed class End(Lane incoming, Lane outgoing) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => incoming.Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer) => incoming.Read(buffer);

        public override void Write(byte[] buffer, int offset, int count) => outgoing.Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer) => outgoing.Write(buffer);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                incoming.Close();
                outgoing.Close();
            }

            base.Dispose(disposing);
        }
    }
}
