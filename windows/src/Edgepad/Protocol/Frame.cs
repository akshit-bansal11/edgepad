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
