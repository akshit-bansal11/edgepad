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
