namespace Edgepad.Protocol;

/// <summary>One message on the wire. Type bytes and payload layouts live in <see cref="FrameCodec"/>.</summary>
internal abstract record Frame;

internal sealed record Hello(byte Version) : Frame;

internal sealed record HelloAck(byte Version) : Frame;

internal sealed record Move(short Dx, short Dy) : Frame;

internal sealed record PointerButton(byte Id, bool Down) : Frame;

internal sealed record Scroll(short Dx, short Dy) : Frame;

internal sealed record Zoom(short Delta) : Frame;

internal sealed record RunAction(byte Id) : Frame;

internal sealed record SetValue(byte Control, byte Value) : Frame;

internal sealed record Ping(long Time) : Frame;

internal sealed record Pong(long Time) : Frame;

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
