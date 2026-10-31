using System.Security.Cryptography;
using Edgepad.Capture;
using Edgepad.Net;
using Edgepad.Protocol;

namespace Edgepad.Blob;

/// <summary>
/// Sends blobs to the phone as BLOB_BEGIN, BLOB_CHUNKs and BLOB_END, one at a time, because the far end holds one
/// transfer and a second BEGIN would abort the first. One per session. It blocks its caller for the whole transfer, so
/// it is never called on the session's read thread.
/// <para>
/// Paced exactly as a capture is (<see cref="CaptureToPhone"/>): each chunk is its own send, so a PONG or a STATE gets
/// between any two, and a tick's budget drops to <see cref="BusyChunksPerTick"/> while the phone is sending input.
/// </para>
/// </summary>
internal sealed class BlobSender(
    Action<Frame> send,
    LinkKind link,
    Func<long> lastInputMs,
    CancellationToken ended,
    Func<long>? clock = null,
    Func<int, bool>? pause = null)
{
    public const int TickMs = CaptureToPhone.TickMs;
    public const int BluetoothChunksPerTick = CaptureToPhone.ChunksPerTick;
    public const int NetworkChunksPerTick = 128;
    public const int BusyChunksPerTick = CaptureToPhone.BusyChunksPerTick;
    public const long InputQuietMs = CaptureToPhone.InputQuietMs;

    private readonly Lock lane = new();
    private readonly Func<long> now = clock ?? (() => Environment.TickCount64);
    private readonly Func<int, bool> wait = pause ?? (ms => !ended.WaitHandle.WaitOne(ms));

    /// <summary>
    /// Sends one blob. False when the session ended or <paramref name="abandoned"/> said so part way: no BLOB_END goes,
    /// and the next BLOB_BEGIN clears what the far end was holding. A write that fails throws, as any send does.
    /// </summary>
    public bool Send(BlobKind kind, string label, ReadOnlyMemory<byte> data, Func<bool>? abandoned = null)
    {
        lock (lane)
        {
            send(new BlobBegin((byte)kind, (uint)data.Length, label));
            byte seq = 0;
            var sent = 0;
            for (var at = 0; at < data.Length; at += FrameCodec.MaxChunkBytes)
            {
                if (abandoned?.Invoke() == true)
                {
                    return false;
                }

                send(new BlobChunk(seq++, data.Slice(at, Math.Min(FrameCodec.MaxChunkBytes, data.Length - at)).ToArray()));
                if (++sent >= Budget() && at + FrameCodec.MaxChunkBytes < data.Length)
                {
                    sent = 0;
                    if (!wait(TickMs))
                    {
                        return false;
                    }
                }
            }

            send(new BlobEnd(SHA256.HashData(data.Span)));
            return true;
        }
    }

    private int Budget()
    {
        if (now() - lastInputMs() < InputQuietMs)
        {
            return BusyChunksPerTick;
        }

        return link == LinkKind.Bluetooth ? BluetoothChunksPerTick : NetworkChunksPerTick;
    }
}
