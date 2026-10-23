/// Why a frame could not be understood. The caller closes the connection on any of them.
public enum ProtocolError: Error, Equatable {
    case unknownType(UInt8)
    case wrongLength(type: UInt8, expected: Int, got: Int)
    case badMagic
    case badFlag(UInt8)
    /// A PIN_ANSWER byte above 9. It carries no value: a rejected byte is still part of somebody's PIN.
    case badDigit
    case textLengthMismatch
}

/// How many payload bytes follow a type byte.
public enum PayloadLength: Equatable {
    case fixed(Int)
    /// TEXT: two header bytes (kind, length), then as many bytes as the length says.
    case lengthPrefixed
    case unknown
}

/// Frame layout: one type byte, then a payload whose length is fixed by the type, except TEXT, whose two
/// header bytes (kind, length) say how much UTF-8 follows. Little-endian. `protocol/frames.txt` holds the
/// golden bytes every app is tested against.
public enum FrameCodec {
    public static let maxTextBytes = 255
    public static let textHeaderLength = 2

    private static let helloType: UInt8 = 0x01
    private static let helloAckType: UInt8 = 0x02
    private static let pinRequestType: UInt8 = 0x03
    private static let pinAnswerType: UInt8 = 0x04
    private static let moveType: UInt8 = 0x10
    private static let pointerButtonType: UInt8 = 0x11
    private static let scrollType: UInt8 = 0x12
    private static let zoomType: UInt8 = 0x13
    private static let runActionType: UInt8 = 0x20
    private static let setValueType: UInt8 = 0x21
    private static let keyType: UInt8 = 0x22
    private static let padStateType: UInt8 = 0x23
    private static let penType: UInt8 = 0x24
    private static let pingType: UInt8 = 0x30
    private static let pongType: UInt8 = 0x31
    private static let stateType: UInt8 = 0x40
    private static let textType: UInt8 = 0x41

    /// PAD_STATE is the longest fixed payload at 12 bytes, so 13 on the wire.
    public static func payloadLength(_ type: UInt8) -> PayloadLength {
        switch type {
        case helloType:
            return .fixed(5)
        case helloAckType, runActionType:
            return .fixed(1)
        case pointerButtonType, zoomType, setValueType:
            return .fixed(2)
        case stateType, keyType, pinRequestType:
            return .fixed(3)
        case moveType, scrollType:
            return .fixed(4)
        case pinAnswerType:
            return .fixed(Pin.length)
        case pingType, pongType:
            return .fixed(8)
        case padStateType:
            return .fixed(12)
        case penType:
            return .fixed(5)
        case textType:
            return .lengthPrefixed
        default:
            return .unknown
        }
    }

    public static func encode(_ frame: Frame) -> [UInt8] {
        switch frame {
        case .hello(let version):
            return [helloType] + ProtocolConstants.magic + [version]
        case .helloAck(let version):
            return [helloAckType, version]
        case .move(let dx, let dy):
            return [moveType] + littleEndian(dx) + littleEndian(dy)
        case .pointerButton(let id, let down):
            return [pointerButtonType, id, down ? 1 : 0]
        case .scroll(let dx, let dy):
            return [scrollType] + littleEndian(dx) + littleEndian(dy)
        case .zoom(let delta):
            return [zoomType] + littleEndian(delta)
        case .runAction(let id):
            return [runActionType, id]
        case .setValue(let control, let value):
            return [setValueType, control, value]
        case .ping(let time):
            return [pingType] + littleEndian(time)
        case .pong(let time):
            return [pongType] + littleEndian(time)
        case .state(let control, let value, let flags):
            return [stateType, control, value, flags]
        case .text(let kind, let value):
            let body = Array(truncate(value).utf8)
            return [textType, kind, UInt8(truncatingIfNeeded: body.count)] + body
        case .key(let code, let down):
            return [keyType] + littleEndian(code) + [down ? 1 : 0]
        case .padState(let buttons, let lt, let rt, let lx, let ly, let rx, let ry):
            // The XINPUT_GAMEPAD field order exactly: wButtons, bLeftTrigger, bRightTrigger, then the four
            // thumb axes. A Mac only ever decodes PAD_STATE, but the fixture makes every codec prove both
            // directions on every line.
            var out: [UInt8] = [padStateType]
            out += littleEndian(buttons)
            out += [lt, rt]
            out += littleEndian(lx)
            out += littleEndian(ly)
            out += littleEndian(rx)
            out += littleEndian(ry)
            return out
        case .pen(let x, let y, let state):
            var out: [UInt8] = [penType]
            out += littleEndian(x)
            out += littleEndian(y)
            out.append(state)
            return out
        case .pinRequest(let attemptsLeft, let lockedSeconds):
            return [pinRequestType, attemptsLeft] + littleEndian(lockedSeconds)
        case .pinAnswer(let pin):
            // Each digit goes out as its value 0...9, not its ASCII code, so these bytes can never read as text.
            return [pinAnswerType] + pin.digits
        }
    }

    /// Decodes one frame. `payload` excludes the type byte; for TEXT it starts with the two header bytes
    /// (kind, length). Anything malformed throws, and the caller drops the connection.
    public static func decode(type: UInt8, payload: [UInt8]) throws -> Frame {
        switch payloadLength(type) {
        case .unknown:
            throw ProtocolError.unknownType(type)
        case .lengthPrefixed:
            return try decodeText(payload)
        case .fixed(let expected):
            guard payload.count == expected else {
                throw ProtocolError.wrongLength(type: type, expected: expected, got: payload.count)
            }
        }

        switch type {
        case helloType:
            guard Array(payload[0..<4]) == ProtocolConstants.magic else {
                throw ProtocolError.badMagic
            }
            return .hello(version: payload[4])
        case helloAckType:
            return .helloAck(version: payload[0])
        case moveType:
            return .move(dx: int16(payload, 0), dy: int16(payload, 2))
        case pointerButtonType:
            return .pointerButton(id: payload[0], down: try flag(payload[1]))
        case scrollType:
            return .scroll(dx: int16(payload, 0), dy: int16(payload, 2))
        case zoomType:
            return .zoom(delta: int16(payload, 0))
        case runActionType:
            return .runAction(id: payload[0])
        case setValueType:
            return .setValue(control: payload[0], value: payload[1])
        case keyType:
            return .key(code: uint16(payload, 0), down: try flag(payload[2]))
        case pingType:
            return .ping(time: int64(payload))
        case pongType:
            return .pong(time: int64(payload))
        case stateType:
            return .state(control: payload[0], value: payload[1], flags: payload[2])
        case padStateType:
            // Nothing here can be out of range, so nothing here throws: every field spans its whole width,
            // and an undefined button bit is an unknown id, which is dropped and counted rather than an error.
            return .padState(
                buttons: uint16(payload, 0),
                lt: payload[2],
                rt: payload[3],
                lx: int16(payload, 4),
                ly: int16(payload, 6),
                rx: int16(payload, 8),
                ry: int16(payload, 10)
            )
        case penType:
            // Nothing here throws: x and y span their whole width, and a state above 2 is for the dispatcher to
            // drop and count, since closing the link would take the whole session down for a newer phone's state.
            return .pen(x: uint16(payload, 0), y: uint16(payload, 2), state: payload[4])
        case pinRequestType:
            return .pinRequest(attemptsLeft: payload[0], lockedSeconds: uint16(payload, 1))
        case pinAnswerType:
            guard let pin = Pin(digits: payload) else {
                throw ProtocolError.badDigit
            }
            return .pinAnswer(pin)
        default:
            throw ProtocolError.unknownType(type)
        }
    }

    /// The longest prefix of `value` made of whole Unicode scalars whose UTF-8 fits `maxTextBytes`. One pass that
    /// stops at the limit, so a million-character string is not measured whole, and never between the halves
    /// of a character, which would go out as a replacement character. Every codec cuts by the same rule, so
    /// both sides send the same bytes for the same text.
    public static func truncate(_ value: String) -> String {
        var kept = String.UnicodeScalarView()
        var bytes = 0
        for scalar in value.unicodeScalars {
            let width = utf8Width(scalar)
            if bytes + width > maxTextBytes {
                break
            }
            bytes += width
            kept.append(scalar)
        }
        return String(kept)
    }

    private static func decodeText(_ payload: [UInt8]) throws -> Frame {
        guard payload.count >= textHeaderLength, payload.count == textHeaderLength + Int(payload[1]) else {
            throw ProtocolError.textLengthMismatch
        }
        // Invalid UTF-8 becomes U+FFFD rather than an error, so a bad title cannot drop the connection.
        return .text(kind: payload[0], value: String(decoding: payload[textHeaderLength...], as: UTF8.self))
    }

    /// Only 0 and 1 are legal: a flag with two legal values out of 256 means a corrupt stream, not a newer app.
    private static func flag(_ value: UInt8) throws -> Bool {
        switch value {
        case 0:
            return false
        case 1:
            return true
        default:
            throw ProtocolError.badFlag(value)
        }
    }

    private static func utf8Width(_ scalar: Unicode.Scalar) -> Int {
        switch scalar.value {
        case 0..<0x80:
            return 1
        case 0x80..<0x800:
            return 2
        case 0x800..<0x10000:
            return 3
        default:
            return 4
        }
    }

    private static func uint16(_ bytes: [UInt8], _ at: Int) -> UInt16 {
        UInt16(bytes[at]) | UInt16(bytes[at + 1]) << 8
    }

    private static func int16(_ bytes: [UInt8], _ at: Int) -> Int16 {
        Int16(bitPattern: uint16(bytes, at))
    }

    private static func int64(_ bytes: [UInt8]) -> Int64 {
        var value: UInt64 = 0
        for index in 0..<8 {
            value |= UInt64(bytes[index]) << UInt64(index * 8)
        }
        return Int64(bitPattern: value)
    }

    private static func littleEndian(_ value: UInt16) -> [UInt8] {
        [UInt8(truncatingIfNeeded: value), UInt8(truncatingIfNeeded: value >> 8)]
    }

    private static func littleEndian(_ value: Int16) -> [UInt8] {
        littleEndian(UInt16(bitPattern: value))
    }

    private static func littleEndian(_ value: Int64) -> [UInt8] {
        let bits = UInt64(bitPattern: value)
        return (0..<8).map { UInt8(truncatingIfNeeded: bits >> UInt64($0 * 8)) }
    }
}
