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
    /// The laptop asking for the PIN, or refusing the phone for `lockedSeconds`. Only a laptop sends it, so a
    /// Mac decodes it for the fixture's sake and then drops it.
    case pinRequest(attemptsLeft: UInt8, lockedSeconds: UInt16)
    /// The phone's answer to `pinRequest`. `Pin` keeps the digits out of any description of the frame.
    case pinAnswer(Pin)
    /// One point of a pen stroke, 0...65535 across the whole screen with 0 the left or top edge. `state` is 0 up,
    /// 1 down, 2 move and is not range-checked here, because a newer phone may add states: the dispatcher drops
    /// an unknown one and counts it.
    case pen(x: UInt16, y: UInt16, state: UInt8)
}

/// Six decimal digits, the one secret that crosses the link. A frame can end up in a log line or a debug
/// print, so both descriptions hide the digits; the values are stored as 0...9 because that is what the wire
/// carries, and nothing here turns them back into text.
public struct Pin: Equatable, CustomStringConvertible, CustomDebugStringConvertible {
    public static let length = 6

    let digits: [UInt8]

    /// Nil unless there are exactly six values, each 0...9.
    public init?(digits: [UInt8]) {
        guard digits.count == Pin.length, digits.allSatisfy({ $0 <= 9 }) else {
            return nil
        }
        self.digits = digits
    }

    public var description: String { "••••••" }

    public var debugDescription: String { "••••••" }
}
