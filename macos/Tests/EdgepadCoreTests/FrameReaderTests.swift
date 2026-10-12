import XCTest

@testable import EdgepadCore

final class FrameReaderTests: XCTestCase {
    func testAFrameSplitAcrossTwoAppendsIsReturnedByTheSecond() throws {
        var reader = FrameReader()
        let bytes = FrameCodec.encode(.move(dx: 5, dy: -3))

        let first = try reader.append(Array(bytes[0..<2]))
        let second = try reader.append(Array(bytes[2...]))

        XCTAssertEqual(first, [])
        XCTAssertEqual(second, [.move(dx: 5, dy: -3)])
    }

    func testTwoFramesInOneAppendComeBackInOrder() throws {
        var reader = FrameReader()
        let bytes = FrameCodec.encode(.runAction(id: 3)) + FrameCodec.encode(.zoom(delta: 240))

        let frames = try reader.append(bytes)

        XCTAssertEqual(frames, [.runAction(id: 3), .zoom(delta: 240)])
    }

    func testATextFrameDeliveredOneByteAtATimeArrivesWhole() throws {
        var reader = FrameReader()
        var frames: [Frame] = []

        for byte in FrameCodec.encode(.text(kind: 1, value: "Song")) {
            frames += try reader.append([byte])
        }

        XCTAssertEqual(frames, [.text(kind: 1, value: "Song")])
    }

    func testAnUnknownTypeThrows() {
        var reader = FrameReader()

        XCTAssertThrowsError(try reader.append([0x7F])) { error in
            XCTAssertEqual(error as? ProtocolError, .unknownType(0x7F))
        }
    }

    func testAnIncompleteTailReturnsNothing() throws {
        var reader = FrameReader()

        let frames = try reader.append([0x10, 0x05])

        XCTAssertEqual(frames, [])
    }

    func testACompleteFrameFollowedByAnIncompleteOneKeepsTheTail() throws {
        var reader = FrameReader()
        let next = FrameCodec.encode(.ping(time: 7))

        let first = try reader.append(FrameCodec.encode(.helloAck(version: 4)) + Array(next[0..<4]))
        let second = try reader.append(Array(next[4...]))

        XCTAssertEqual(first, [.helloAck(version: 4)])
        XCTAssertEqual(second, [.ping(time: 7)])
    }
}
