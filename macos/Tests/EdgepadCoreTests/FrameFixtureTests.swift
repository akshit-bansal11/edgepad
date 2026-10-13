import XCTest

@testable import EdgepadCore

/// Runs protocol/frames.txt, the same golden frames every other app's suite runs.
final class FrameFixtureTests: XCTestCase {
    func testEveryFixtureLineEncodesToItsGoldenBytes() throws {
        for line in try Fixtures.lines("frames.txt") {
            let (frame, bytes) = try parse(line)

            XCTAssertEqual(FrameCodec.encode(frame), bytes, line)
        }
    }

    func testEveryFixtureLineDecodesFromItsGoldenBytes() throws {
        for line in try Fixtures.lines("frames.txt") {
            let (frame, bytes) = try parse(line)

            XCTAssertEqual(try FrameCodec.decode(type: bytes[0], payload: Array(bytes.dropFirst())), frame, line)
        }
    }

    func testAnUnknownTypeIsRejected() {
        XCTAssertThrowsError(try FrameCodec.decode(type: 0x7F, payload: [])) { error in
            XCTAssertEqual(error as? ProtocolError, .unknownType(0x7F))
        }
    }

    func testHelloWithoutTheMagicIsRejected() {
        XCTAssertThrowsError(try FrameCodec.decode(type: 0x01, payload: [0, 0, 0, 0, 1])) { error in
            XCTAssertEqual(error as? ProtocolError, .badMagic)
        }
    }

    func testAButtonFlagOtherThanZeroOrOneIsRejected() {
        XCTAssertThrowsError(try FrameCodec.decode(type: 0x11, payload: [0, 2])) { error in
            XCTAssertEqual(error as? ProtocolError, .badFlag(2))
        }
    }

    func testAKeyFlagOtherThanZeroOrOneIsRejected() {
        XCTAssertThrowsError(try FrameCodec.decode(type: 0x22, payload: [0x41, 0, 7])) { error in
            XCTAssertEqual(error as? ProtocolError, .badFlag(7))
        }
    }

    func testAShortPayloadIsRejected() {
        XCTAssertThrowsError(try FrameCodec.decode(type: 0x10, payload: [1, 2])) { error in
            XCTAssertEqual(error as? ProtocolError, .wrongLength(type: 0x10, expected: 4, got: 2))
        }
    }

    func testTextWhoseLengthDoesNotMatchItsHeaderIsRejected() {
        XCTAssertThrowsError(try FrameCodec.decode(type: 0x41, payload: [0, 3, 0x41])) { error in
            XCTAssertEqual(error as? ProtocolError, .textLengthMismatch)
        }
    }

    func testTextWithoutItsHeaderIsRejected() {
        XCTAssertThrowsError(try FrameCodec.decode(type: 0x41, payload: [0])) { error in
            XCTAssertEqual(error as? ProtocolError, .textLengthMismatch)
        }
    }

    func testLongTextIsCutToTheLimitWithoutSplittingACharacter() throws {
        let text = Frame.text(kind: 0, value: String(repeating: "a", count: 254) + "é")

        let bytes = FrameCodec.encode(text)

        XCTAssertEqual(bytes.count, 1 + FrameCodec.textHeaderLength + 254)
        let expected = Frame.text(kind: 0, value: String(repeating: "a", count: 254))
        XCTAssertEqual(try FrameCodec.decode(type: bytes[0], payload: Array(bytes.dropFirst())), expected)
    }

    func testAnEmojiStraddlingTheLimitIsDroppedWholeRatherThanHalved() throws {
        // 252 bytes of "a", then a four-byte emoji over bytes 253 to 256. Cutting a UTF-16 unit at a time
        // stopped after the emoji's high half and sent a replacement character the phone never typed.
        let text = Frame.text(kind: 0, value: String(repeating: "a", count: 252) + "\u{1F3B5}")

        let bytes = FrameCodec.encode(text)

        XCTAssertEqual(bytes.count, 1 + FrameCodec.textHeaderLength + 252)
        XCTAssertEqual(bytes[2], 252)
        let expected = Frame.text(kind: 0, value: String(repeating: "a", count: 252))
        XCTAssertEqual(try FrameCodec.decode(type: bytes[0], payload: Array(bytes.dropFirst())), expected)
    }

    func testAnEmojiEndingExactlyOnTheLimitIsKept() {
        let text = String(repeating: "a", count: 251) + "\u{1F3B5}"

        XCTAssertEqual(FrameCodec.truncate(text), text)
        XCTAssertEqual(FrameCodec.encode(.text(kind: 0, value: text)).count, 1 + FrameCodec.textHeaderLength + 255)
    }

    func testAMillionCharacterTextIsCutToTheLimit() {
        let text = String(repeating: "a", count: 1_000_000)

        XCTAssertEqual(FrameCodec.truncate(text), String(repeating: "a", count: FrameCodec.maxTextBytes))
    }

    func testPadStateKeepsSignedSticksSignedAndUnsignedFieldsUnsigned() throws {
        // Invisible in hex: a stick read as unsigned comes back 32768 and not -32768, a trigger read as signed
        // comes back -1 and not 255, and a button mask read as signed turns Y, the top bit, negative.
        let frame = Frame.padState(
            buttons: 0xF7FF, lt: 255, rt: 255, lx: Int16.min, ly: Int16.max, rx: -1, ry: 1)

        let bytes = FrameCodec.encode(frame)

        XCTAssertEqual(bytes.count, 1 + 12)
        XCTAssertEqual(try FrameCodec.decode(type: bytes[0], payload: Array(bytes.dropFirst())), frame)
    }

    private func parse(_ line: String) throws -> (Frame, [UInt8]) {
        let halves = line.split(separator: "=", omittingEmptySubsequences: false)
        guard halves.count == 2 else {
            throw FixtureError.malformed(line)
        }
        let fields = halves[0].split(separator: " ").map(String.init)
        let bytes = try halves[1].split(separator: " ").map { (hex: Substring) -> UInt8 in
            guard let byte = UInt8(hex, radix: 16) else {
                throw FixtureError.malformed(line)
            }
            return byte
        }

        func field(_ index: Int) throws -> Int64 {
            guard index < fields.count, let value = Int64(fields[index]) else {
                throw FixtureError.malformed(line)
            }
            return value
        }

        switch fields[0] {
        case "HELLO":
            return (.hello(version: UInt8(truncatingIfNeeded: try field(1))), bytes)
        case "HELLO_ACK":
            return (.helloAck(version: UInt8(truncatingIfNeeded: try field(1))), bytes)
        case "MOVE":
            let dx = Int16(truncatingIfNeeded: try field(1))
            let dy = Int16(truncatingIfNeeded: try field(2))
            return (.move(dx: dx, dy: dy), bytes)
        case "BUTTON":
            return (.pointerButton(id: UInt8(truncatingIfNeeded: try field(1)), down: try field(2) == 1), bytes)
        case "SCROLL":
            let dx = Int16(truncatingIfNeeded: try field(1))
            let dy = Int16(truncatingIfNeeded: try field(2))
            return (.scroll(dx: dx, dy: dy), bytes)
        case "ZOOM":
            return (.zoom(delta: Int16(truncatingIfNeeded: try field(1))), bytes)
        case "ACTION":
            return (.runAction(id: UInt8(truncatingIfNeeded: try field(1))), bytes)
        case "SET":
            let control = UInt8(truncatingIfNeeded: try field(1))
            let value = UInt8(truncatingIfNeeded: try field(2))
            return (.setValue(control: control, value: value), bytes)
        case "PING":
            return (.ping(time: try field(1)), bytes)
        case "PONG":
            return (.pong(time: try field(1)), bytes)
        case "STATE":
            let control = UInt8(truncatingIfNeeded: try field(1))
            let value = UInt8(truncatingIfNeeded: try field(2))
            let flags = UInt8(truncatingIfNeeded: try field(3))
            return (.state(control: control, value: value, flags: flags), bytes)
        case "TEXT":
            let value = fields.count > 2 ? fields[2] : ""
            return (.text(kind: UInt8(truncatingIfNeeded: try field(1)), value: value), bytes)
        case "KEY":
            return (.key(code: UInt16(truncatingIfNeeded: try field(1)), down: try field(2) == 1), bytes)
        case "PAD_STATE":
            let frame = Frame.padState(
                buttons: UInt16(truncatingIfNeeded: try field(1)),
                lt: UInt8(truncatingIfNeeded: try field(2)),
                rt: UInt8(truncatingIfNeeded: try field(3)),
                lx: Int16(truncatingIfNeeded: try field(4)),
                ly: Int16(truncatingIfNeeded: try field(5)),
                rx: Int16(truncatingIfNeeded: try field(6)),
                ry: Int16(truncatingIfNeeded: try field(7))
            )
            return (frame, bytes)
        default:
            throw FixtureError.malformed(line)
        }
    }
}

private enum FixtureError: Error {
    case malformed(String)
}
