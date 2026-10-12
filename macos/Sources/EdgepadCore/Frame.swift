/// One message on the wire. Type bytes and payload layouts live in `FrameCodec`; `docs/PROTOCOL.md` says
/// what each one means, and `protocol/frames.txt` holds the golden bytes all three codecs are tested against.
public enum Frame: Equatable {
    case hello(version: UInt8)
    case helloAck(version: UInt8)
    case move(dx: Int16, dy: Int16)
    case pointerButton(id: UInt8, down: Bool)
    case scroll(dx: Int16, dy: Int16)
    case zoom(delta: Int16)
    case runAction(id: UInt8)
    case setValue(control: UInt8, value: UInt8)
    case ping(time: Int64)
    case pong(time: Int64)
    case state(control: UInt8, value: UInt8, flags: UInt8)
    /// Text of a `TextKind`. At most 255 UTF-8 bytes.
    case text(kind: UInt8, value: String)
    /// One keyboard key, by Windows virtual-key code: the wire speaks Windows' codes whatever the laptop is,
    /// and `KeyMap` turns them into this Mac's.
    case key(code: UInt16, down: Bool)
    /// The whole controller at once, laid out like Windows' `XINPUT_GAMEPAD`. A Mac has no virtual
    /// controller to give it to, so it is decoded for the fixture's sake and then dropped.
    case padState(buttons: UInt16, lt: UInt8, rt: UInt8, lx: Int16, ly: Int16, rx: Int16, ry: Int16)
}
