namespace Edgepad.Protocol;

/// <summary>One message on the wire. Type bytes and payload layouts live in <see cref="FrameCodec"/>.</summary>
internal abstract record Frame;

/// <summary>HELLO, phone to laptop: opens a session at the phone's protocol version.</summary>
internal sealed record Hello(byte Version) : Frame;

/// <summary>HELLO_ACK, laptop to phone: accepts the session; on a version mismatch it carries the laptop's version, then closes.</summary>
internal sealed record HelloAck(byte Version) : Frame;

/// <summary>MOVE: moves the pointer by this many laptop pixels, before the OS's acceleration.</summary>
internal sealed record Move(short Dx, short Dy) : Frame;

/// <summary>BUTTON: presses or releases a mouse button, 0 left, 1 right, 2 middle.</summary>
internal sealed record PointerButton(byte Id, bool Down) : Frame;

/// <summary>SCROLL: wheel units, 120 to a notch; a positive <paramref name="Dy"/> is the wheel turned forward.</summary>
internal sealed record Scroll(short Dx, short Dy) : Frame;

/// <summary>ZOOM: Ctrl+wheel units, as a pinch on the pad sends them.</summary>
internal sealed record Zoom(short Delta) : Frame;

/// <summary>ACTION: runs one action of <see cref="ActionId"/>, the table in <c>protocol/actions.txt</c>.</summary>
internal sealed record RunAction(byte Id) : Frame;

/// <summary>SET: sets a control (volume, brightness, a dial) to 0..100; the laptop drops anything above 100.</summary>
internal sealed record SetValue(byte Control, byte Value) : Frame;

/// <summary>PING, phone to laptop: the phone's clock in nanoseconds, echoed back in a <see cref="Pong"/>.</summary>
internal sealed record Ping(long Time) : Frame;

/// <summary>PONG, laptop to phone: a <see cref="Ping"/>'s time echoed, for the round-trip readout.</summary>
internal sealed record Pong(long Time) : Frame;

/// <summary>STATE, laptop to phone: a control's current value. Flags bit 0 is muted for an audio control, playing for media.</summary>
internal sealed record StateReport(byte Control, byte Value, byte Flags) : Frame;

/// <summary>Text, of a <see cref="TextKind"/>: what is playing, the app, the timeline, or characters to type. At most 255 UTF-8 bytes.</summary>
internal sealed record Text(byte Kind, string Value) : Frame;

/// <summary>One keyboard key, by Windows virtual-key code (e.g. 0x41 = A, 0x10 = SHIFT).</summary>
internal sealed record Key(ushort Code, bool Down) : Frame;

/// <summary>
/// The whole controller in one frame: every button, both triggers and both sticks together, sent when any of
/// them changes, and held here until the next one arrives. One frame rather than a frame per control because
/// a gamepad is read as a snapshot, not as a stream of edges — a dropped button release would otherwise stick
/// a button down until the user pressed it again.
/// <para>
/// The fields are deliberately byte-for-byte those of <c>XINPUT_GAMEPAD</c> (<c>wButtons</c>,
/// <c>bLeftTrigger</c>, <c>bRightTrigger</c>, <c>sThumbLX</c>, <c>sThumbLY</c>, <c>sThumbRX</c>,
/// <c>sThumbRY</c>), in that order and at those widths, so this side copies the payload into the struct
/// instead of translating it. A range or a button order of our own would be a conversion on the hot path and
/// a second definition to keep in step with Microsoft's.
/// </para>
/// <para>
/// <c>Buttons</c> is the <see cref="PadButton"/> mask. The triggers are 0..255. The four stick
/// axes are -32768..32767, positive up and right, exactly as XInput reports them.
/// </para>
/// </summary>
internal sealed record PadState(ushort Buttons, byte Lt, byte Rt, short Lx, short Ly, short Rx, short Ry) : Frame;

/// <summary>
/// The laptop asking for the PIN, or refusing the phone for a while. Sent only between the trust question and
/// HELLO_ACK. <c>LockedSeconds</c> is 0 for a question; for a lock-out it is the wait, <c>AttemptsLeft</c> is 0
/// and the link is closed right after.
/// </summary>
internal sealed record PinRequest(byte AttemptsLeft, ushort LockedSeconds) : Frame;

/// <summary>The phone's answer to <see cref="PinRequest"/>. <see cref="Pin"/> keeps the digits out of any log line this frame reaches.</summary>
internal sealed record PinAnswer(Pin Digits) : Frame;

/// <summary>
/// One point of a pen stroke on the annotate or sign surface. <c>X</c> and <c>Y</c> span the whole 0..65535
/// range, 0 the left or top edge of the laptop screen and 65535 the right or bottom one, so no pixel count
/// crosses the link. <c>State</c> is 0 up, 1 down, 2 move; it is not range-checked by the codec, because a
/// newer phone may add states and the dispatcher drops an unknown one and counts it.
/// </summary>
internal sealed record Pen(ushort X, ushort Y, byte State) : Frame;

/// <summary>
/// The start of a file sent to the phone: a capture, when the phone asked for captures to be saved there.
/// <c>Type</c> is a <see cref="FileType"/> id, never a MIME string; <c>Size</c> is the whole file in bytes and
/// <c>Name</c> its file name with no folder. The phone checks all three before it writes anything.
/// </summary>
internal sealed record FileBegin(byte Type, uint Size, string Name) : Frame;

/// <summary>
/// One piece of the file, at most <see cref="FrameCodec.MaxChunkBytes"/> bytes. <c>Seq</c> is the piece's index mod
/// 256, so the phone sees a piece go missing. Equal by content, which a record holding an array is not by default.
/// </summary>
internal sealed record FileChunk(byte Seq, byte[] Data) : Frame
{
    public bool Equals(FileChunk? other) => other is not null && other.Seq == Seq && other.Data.AsSpan().SequenceEqual(Data);

    public override int GetHashCode() => HashCode.Combine(Seq, Data.Length);
}

/// <summary>The end of the file: the SHA-256 of all of it, which the phone checks before it keeps the file.</summary>
internal sealed record FileEnd(byte[] Sha256) : Frame
{
    public bool Equals(FileEnd? other) => other is not null && other.Sha256.AsSpan().SequenceEqual(Sha256);

    public override int GetHashCode() => Sha256.Length == 0 ? 0 : Sha256[0];
}

/// <summary>
/// The start of a blob: the clipboard, or an app list or icon, in either direction, on a lane of its own beside the file
/// one. FILE_BEGIN's shape. <c>Kind</c> is a <see cref="BlobKind"/> id, but it is data here and not a codec error,
/// because the receiver reads an unknown kind to its end and drops it. <c>Size</c> is the whole blob in bytes and
/// <c>Label</c> a short UTF-8 name.
/// </summary>
internal sealed record BlobBegin(byte Kind, uint Size, string Label) : Frame;

/// <summary>One piece of the blob, at most <see cref="FrameCodec.MaxChunkBytes"/> bytes. Equal by content, like <see cref="FileChunk"/>.</summary>
internal sealed record BlobChunk(byte Seq, byte[] Data) : Frame
{
    public bool Equals(BlobChunk? other) => other is not null && other.Seq == Seq && other.Data.AsSpan().SequenceEqual(Data);

    public override int GetHashCode() => HashCode.Combine(Seq, Data.Length);
}

/// <summary>The end of the blob: the SHA-256 of all of it.</summary>
internal sealed record BlobEnd(byte[] Sha256) : Frame
{
    public bool Equals(BlobEnd? other) => other is not null && other.Sha256.AsSpan().SequenceEqual(Sha256);

    public override int GetHashCode() => Sha256.Length == 0 ? 0 : Sha256[0];
}

/// <summary>
/// The first frame of the second socket a phone opens for transfers, HELLO's shape: the magic and the protocol version.
/// A laptop answers a version it does not speak by closing that socket.
/// </summary>
internal sealed record BulkHello(byte Version) : Frame;

/// <summary>
/// The start of a transfer in either direction. <c>Id</c> is 1..255, never 0. <c>Size</c> is the whole file in bytes and
/// <c>Name</c> its file name with no folder, at most <see cref="FrameCodec.MaxXferNameBytes"/> bytes of UTF-8.
/// </summary>
internal sealed record XferBegin(byte Id, ulong Size, string Name) : Frame;

/// <summary>
/// One piece of a transfer: <c>Offset</c> is the full byte offset of the first byte of <c>Data</c>, which replaces the
/// sequence number mod 256 of the file frames. <c>Data</c> is 1 to <see cref="FrameCodec.MaxXferChunkBytes"/> bytes. Equal by content.
/// </summary>
internal sealed record XferChunk(byte Id, ulong Offset, byte[] Data) : Frame
{
    public bool Equals(XferChunk? other) =>
        other is not null && other.Id == Id && other.Offset == Offset && other.Data.AsSpan().SequenceEqual(Data);

    public override int GetHashCode() => HashCode.Combine(Id, Offset, Data.Length);
}

/// <summary>The end of a transfer: the SHA-256 of all of it.</summary>
internal sealed record XferEnd(byte Id, byte[] Sha256) : Frame
{
    public bool Equals(XferEnd? other) => other is not null && other.Id == Id && other.Sha256.AsSpan().SequenceEqual(Sha256);

    public override int GetHashCode() => HashCode.Combine(Id, Sha256.Length == 0 ? 0 : Sha256[0]);
}

/// <summary>
/// How a transfer stands, either direction. <c>Status</c> is 0 READY (<c>Offset</c> is where the sender starts), 1 PROGRESS (the
/// contiguous bytes held), 2 SAVED, 3 BAD_HASH, 4 REJECTED, 5 WRITE_FAILED, 6 CANCELLED or 7 BUSY. A status this build does not
/// know is data, not a codec error: the receiver drops it and counts it.
/// </summary>
internal sealed record XferStatus(byte Id, byte Status, ulong Offset) : Frame;

/// <summary>The phone's answer to a file, one <see cref="FileStatus"/> id. Only <see cref="FileStatus.Saved"/> lets this laptop delete its copy.</summary>
internal sealed record FileResult(byte Status) : Frame;

/// <summary>
/// Six decimal digits, the one secret that crosses the link. Frames are interpolated into log lines
/// (<c>Log.Write($"... {frame}")</c>), so every way a record prints this reaches <c>ToString</c>, which
/// returns stars; the digits are only ever read by the one place that compares them.
/// </summary>
internal readonly record struct Pin
{
    public const int Length = 6;

    public Pin(string digits)
    {
        if (digits.Length != Length || !digits.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("A PIN is exactly six digits", nameof(digits));
        }

        Digits = digits;
    }

    /// <summary>The digits as ASCII text. Not for logging.</summary>
    public string Digits { get; }

    public override string ToString() => "******";
}
