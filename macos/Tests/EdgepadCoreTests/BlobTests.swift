import CryptoKit
import Foundation
import XCTest

@testable import EdgepadCore

extension Frame {
    fileprivate var isBlobChunk: Bool {
        if case .blobChunk = self {
            return true
        }
        return false
    }

    fileprivate var isBlobEnd: Bool {
        if case .blobEnd = self {
            return true
        }
        return false
    }

    fileprivate var blobSize: UInt32? {
        if case .blobBegin(_, let size, _) = self {
            return size
        }
        return nil
    }
}

/// A clipboard in memory, which notes what it was asked to write.
final class FakeClipboard: ClipboardSurface {
    var current: ClipboardContent?
    var fails = false
    private(set) var writes: [ClipboardContent] = []

    func read() -> ClipboardContent? {
        fails ? nil : current
    }

    func write(_ content: ClipboardContent) -> Bool {
        guard !fails else { return false }
        writes.append(content)
        current = content
        return true
    }
}

/// The blob lane and the clipboard's rules, on a fake clock and a fake clipboard, so nothing here sleeps or needs a
/// desktop. Everything a phone sends is untrusted: each refusal must leave the receiver able to take the next blob. The
/// same cases and bytes as the shared core's BlobTests.
final class BlobTests: XCTestCase {
    private static let a = Array("copied on the phone".utf8)
    private static let b = Array("copied on the laptop".utf8)
    private static let wrongHash = [UInt8](repeating: 0, count: FrameCodec.sha256Bytes)

    private var wire: [Frame] = []
    private var received: [(kind: UInt8, data: [UInt8])] = []
    private var pending: [() -> Void] = []
    private let surface = FakeClipboard()
    private var now: TimeInterval = 1000
    private var lastInput: TimeInterval = 0
    private var toggle = true

    private lazy var receiver = BlobReceiver(
        cap: { kind in kind <= BlobKind.clipImage.rawValue ? ClipboardSync.maxTextBytes : nil },
        completed: { [unowned self] kind, data, sha256 in
            XCTAssertEqual(BlobTests.hash(data), sha256)
            self.received.append((kind, data))
            return true
        }
    )
    private lazy var sync = syncOver(sender())

    private static func hash(_ data: [UInt8]) -> [UInt8] {
        Array(SHA256.hash(data: data))
    }

    private func payload(_ length: Int) -> [UInt8] {
        (0..<length).map { UInt8(truncatingIfNeeded: $0 &* 31) }
    }

    /// A PNG as far as its header: the signature, IHDR's length and name, then the two sizes.
    private func png(_ width: UInt32, _ height: UInt32) -> [UInt8] {
        var bytes: [UInt8] = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]
        bytes += [0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52]
        for value in [width, height] {
            bytes += withUnsafeBytes(of: value.bigEndian) { Array($0) }
        }
        return bytes
    }

    /// `pause` runs where the shared core's sender waits for a tick, and the tick then follows at once.
    private func sender(network: Bool = false, pause: @escaping (TimeInterval) -> Void = { _ in }) -> BlobSender {
        let sender = BlobSender(
            link: network ? .network : .bluetooth,
            clock: { [unowned self] in self.now },
            schedule: { delay, work in
                pause(delay)
                work()
            },
            lastInput: { [unowned self] in self.lastInput }
        )
        sender.output = { [unowned self] frame in
            self.wire.append(frame)
            return true
        }
        return sender
    }

    private func syncOver(_ sender: BlobSender) -> ClipboardSync {
        ClipboardSync(
            surface: surface,
            sender: sender,
            allowed: { [unowned self] in self.toggle },
            clock: { [unowned self] in self.now },
            schedule: { [unowned self] _, work in self.pending.append(work) }
        )
    }

    /// Sends a blob, and answers what its `finished` said, or nil if it has not been called.
    @discardableResult
    private func transfer(
        _ sender: BlobSender,
        _ data: [UInt8],
        kind: BlobKind = .clipText,
        abandoned: @escaping () -> Bool = { false }
    ) -> Bool? {
        var result: Bool?
        sender.send(kind, label: "", data: data, abandoned: abandoned, finished: { result = $0 })
        return result
    }

    private func feed(_ frame: Frame) -> Bool {
        switch frame {
        case .blobBegin(let kind, let size, let label):
            return receiver.take(kind: kind, size: size, label: label)
        case .blobChunk(let seq, let data):
            return receiver.take(seq: seq, data: data)
        case .blobEnd(let sha256):
            return receiver.take(sha256: sha256)
        default:
            return false
        }
    }

    private func assertStillTakesABlob(line: UInt = #line) {
        XCTAssertTrue(received.isEmpty, line: line)
        let data = payload(300)

        XCTAssertEqual(transfer(sender(), data, kind: .clipImage), true, line: line)

        XCTAssertTrue(wire.allSatisfy { feed($0) }, line: line)
        XCTAssertEqual(received.map { $0.data }, [data], line: line)
    }

    /// Time for a debounce to pass, then everything the sync put off, as its scheduler would run it.
    private func settle() {
        now += ClipboardSync.debounce
        let due = pending
        pending.removeAll()
        for work in due {
            work()
        }
    }

    private func remote(_ data: [UInt8]) {
        XCTAssertTrue(remoteLastFrame(.clipText, data))
    }

    /// Plays a blob into the sync as the phone would, and answers what it said to the BLOB_END.
    private func remoteLastFrame(_ kind: BlobKind, _ data: [UInt8]) -> Bool {
        var inbound: [Frame] = []
        let phone = BlobSender(link: .network, schedule: { _, work in work() }, lastInput: { -.infinity })
        phone.output = { frame in
            inbound.append(frame)
            return true
        }
        phone.send(kind, label: "", data: data)

        var answer = false
        for frame in inbound {
            switch frame {
            case .blobBegin(let kind, let size, let label):
                answer = sync.take(kind: kind, size: size, label: label)
            case .blobChunk(let seq, let data):
                answer = sync.take(seq: seq, data: data)
            case .blobEnd(let sha256):
                answer = sync.take(sha256: sha256)
            default:
                answer = false
            }
        }
        return answer
    }

    private func text(_ data: [UInt8], sensitive: Bool = false) -> ClipboardContent {
        ClipboardContent(kind: .clipText, data: data, sensitive: sensitive)
    }

    func testABlobCrossesWhole() {
        for length in [0, 1, 255, 256, 1024 * 1024] {
            wire.removeAll()
            received.removeAll()
            let data = payload(length)

            XCTAssertEqual(transfer(sender(network: true), data), true)

            let chunks = (length + FrameCodec.maxChunkBytes - 1) / FrameCodec.maxChunkBytes
            XCTAssertEqual(wire.filter(\.isBlobChunk).count, chunks)
            XCTAssertTrue(wire.allSatisfy { feed($0) })
            XCTAssertEqual(received.count, 1)
            XCTAssertEqual(received.first?.kind, BlobKind.clipText.rawValue)
            XCTAssertEqual(received.first?.data, data)
        }
    }

    func testAWrongHashIsRefused() {
        XCTAssertTrue(receiver.take(kind: 0, size: 10, label: ""))
        XCTAssertTrue(receiver.take(seq: 0, data: payload(10)))
        XCTAssertFalse(receiver.take(sha256: BlobTests.wrongHash))

        assertStillTakesABlob()
    }

    func testAChunkOutOfOrderDropsTheBlobAndItsTail() {
        XCTAssertTrue(receiver.take(kind: 0, size: 20, label: ""))
        XCTAssertFalse(receiver.take(seq: 1, data: payload(10)))
        XCTAssertTrue(receiver.take(seq: 0, data: payload(10)))
        XCTAssertTrue(receiver.take(sha256: BlobTests.hash(payload(20))))

        assertStillTakesABlob()
    }

    func testTheSequenceWrapsAt256() {
        let data = payload(300 * FrameCodec.maxChunkBytes)

        XCTAssertEqual(transfer(sender(network: true), data), true)

        XCTAssertTrue(wire.allSatisfy { feed($0) })
        XCTAssertEqual(received.first?.data, data)
    }

    func testFewerBytesThanThePromisedSizeAreRefused() {
        let data = payload(10)

        XCTAssertTrue(receiver.take(kind: 0, size: 11, label: ""))
        XCTAssertTrue(receiver.take(seq: 0, data: data))
        XCTAssertFalse(receiver.take(sha256: BlobTests.hash(data)))

        assertStillTakesABlob()
    }

    func testMoreBytesThanThePromisedSizeAreRefused() {
        XCTAssertTrue(receiver.take(kind: 0, size: 9, label: ""))
        XCTAssertFalse(receiver.take(seq: 0, data: payload(10)))
        XCTAssertTrue(receiver.take(sha256: BlobTests.hash(payload(10))))

        assertStillTakesABlob()
    }

    func testAnEmptyChunkIsRefused() {
        XCTAssertTrue(receiver.take(kind: 0, size: 1, label: ""))
        XCTAssertFalse(receiver.take(seq: 0, data: []))

        assertStillTakesABlob()
    }

    func testASizeOverTheCapIsRefusedBeforeAByteIsKept() {
        for size in [UInt32(ClipboardSync.maxTextBytes + 1), UInt32.max] {
            XCTAssertFalse(receiver.take(kind: 0, size: size, label: ""))
            XCTAssertTrue(receiver.take(seq: 0, data: payload(10)))
            XCTAssertTrue(receiver.take(sha256: BlobTests.wrongHash))

            wire.removeAll()
            received.removeAll()
            assertStillTakesABlob()
            wire.removeAll()
            received.removeAll()
        }
    }

    func testALabelWithControlCharactersIsRefused() {
        XCTAssertFalse(receiver.take(kind: 0, size: 1, label: "a\nb"))

        assertStillTakesABlob()
    }

    func testASecondBeginAbortsTheFirst() {
        let data = payload(10)

        XCTAssertTrue(receiver.take(kind: 0, size: 20, label: ""))
        XCTAssertTrue(receiver.take(seq: 0, data: payload(5)))
        XCTAssertTrue(receiver.take(kind: 1, size: 10, label: ""))
        XCTAssertTrue(receiver.take(seq: 0, data: data))
        XCTAssertTrue(receiver.take(sha256: BlobTests.hash(data)))

        XCTAssertEqual(received.count, 1)
        XCTAssertEqual(received.first?.kind, 1)
        XCTAssertEqual(received.first?.data, data)
    }

    func testAnUnknownKindIsCountedOnceAndReadToItsEnd() {
        XCTAssertFalse(receiver.take(kind: 200, size: 10, label: ""))
        XCTAssertTrue(receiver.take(seq: 0, data: payload(10)))
        XCTAssertTrue(receiver.take(sha256: BlobTests.hash(payload(10))))

        XCTAssertTrue(received.isEmpty)
        assertStillTakesABlob()
    }

    func testFramesWithNoBlobOpenAreRefused() {
        XCTAssertFalse(receiver.take(seq: 0, data: payload(1)))
        XCTAssertFalse(receiver.take(sha256: BlobTests.hash(payload(1))))

        assertStillTakesABlob()
    }

    func testAConnectionHasAByteBudget() {
        let chunk = [UInt8](repeating: 0, count: FrameCodec.maxChunkBytes)
        _ = receiver.take(kind: 200, size: 0, label: "")
        for _ in 0..<(BlobReceiver.maxConnectionBytes / FrameCodec.maxChunkBytes) {
            _ = receiver.take(seq: 0, data: chunk)
        }

        let left = UInt32(BlobReceiver.maxConnectionBytes % FrameCodec.maxChunkBytes)
        XCTAssertFalse(receiver.take(kind: 0, size: left + 1, label: ""))
        XCTAssertTrue(receiver.take(kind: 0, size: left, label: ""))
    }

    func testAQuietLinkSendsTheBudgetPerTick() {
        for (network, budget) in [
            (false, BlobSender.bluetoothChunksPerTick), (true, BlobSender.networkChunksPerTick),
        ] {
            wire.removeAll()
            var chunksAtEachTick: [Int] = []
            let blobs = sender(network: network) { delay in
                XCTAssertEqual(delay, BlobSender.tick)
                chunksAtEachTick.append(self.wire.filter(\.isBlobChunk).count)
            }

            XCTAssertEqual(transfer(blobs, payload((3 * budget + 1) * FrameCodec.maxChunkBytes)), true)

            XCTAssertEqual(chunksAtEachTick, [budget, 2 * budget, 3 * budget])
        }
    }

    func testInputDropsTheNextTickToTheBusyBudget() {
        var chunksAtEachTick: [Int] = []
        let blobs = sender { _ in
            chunksAtEachTick.append(self.wire.filter(\.isBlobChunk).count)
            self.lastInput = self.now
        }

        XCTAssertEqual(transfer(blobs, payload(40 * FrameCodec.maxChunkBytes)), true)

        XCTAssertEqual(chunksAtEachTick[0], BlobSender.bluetoothChunksPerTick)
        XCTAssertEqual(chunksAtEachTick[1], BlobSender.bluetoothChunksPerTick + BlobSender.busyChunksPerTick)
    }

    func testInputThatHasGoneQuietRestoresTheBudget() {
        var chunksAtEachTick: [Int] = []
        lastInput = now
        let blobs = sender { _ in
            chunksAtEachTick.append(self.wire.filter(\.isBlobChunk).count)
            self.now += BlobSender.inputQuiet
        }

        XCTAssertEqual(transfer(blobs, payload(40 * FrameCodec.maxChunkBytes)), true)

        XCTAssertEqual(chunksAtEachTick[0], BlobSender.busyChunksPerTick)
        XCTAssertEqual(chunksAtEachTick[1], BlobSender.busyChunksPerTick + BlobSender.bluetoothChunksPerTick)
    }

    func testAFastLinkNeverQueuesPastTheBacklog() {
        var ticks: [() -> Void] = []
        var backlog = 0
        var highest = 0
        let blobs = BlobSender(
            link: .network,
            clock: { 0 },
            schedule: { _, work in ticks.append(work) },
            lastInput: { -.infinity }
        )
        blobs.output = { [unowned self] frame in
            self.wire.append(frame)
            backlog += FrameCodec.encode(frame).count
            highest = max(highest, backlog)
            return true
        }
        blobs.backlog = { backlog }
        let data = payload(4 * BlobSender.networkChunksPerTick * FrameCodec.maxChunkBytes)

        blobs.send(.clipText, label: "", data: data)
        // Half a drain each tick: a whole one would stay under the bound on the tick budget alone.
        while !ticks.isEmpty {
            backlog /= 2
            ticks.removeFirst()()
        }

        XCTAssertLessThan(highest, BlobSender.maxBacklog + 2 * FrameCodec.maxChunkBytes)
        XCTAssertTrue(wire.allSatisfy { feed($0) })
        XCTAssertEqual(received.map { $0.data }, [data])
    }

    func testAnEndedSessionStopsATransferWithNoEnd() {
        var ending: BlobSender?
        let blobs = sender { _ in ending?.end() }
        ending = blobs

        XCTAssertEqual(transfer(blobs, payload(100 * FrameCodec.maxChunkBytes)), false)

        XCTAssertFalse(wire.contains(where: \.isBlobEnd))
        XCTAssertEqual(wire.filter(\.isBlobChunk).count, BlobSender.bluetoothChunksPerTick)
    }

    func testAnEndedSenderSendsNothing() {
        let blobs = sender()
        blobs.end()

        XCTAssertEqual(transfer(blobs, payload(10)), false)

        XCTAssertTrue(wire.isEmpty)
    }

    func testAnAbandonedTransferStopsWithNoEnd() {
        XCTAssertEqual(transfer(sender(), payload(10), abandoned: { true }), false)

        XCTAssertEqual(wire.count, 1)
        XCTAssertEqual(wire.first?.blobSize, 10)
    }

    func testASecondBlobWaitsForTheFirstToFinish() {
        var ticks: [() -> Void] = []
        let blobs = BlobSender(
            link: .bluetooth,
            clock: { 0 },
            schedule: { _, work in ticks.append(work) },
            lastInput: { -.infinity }
        )
        blobs.output = { [unowned self] frame in
            self.wire.append(frame)
            return true
        }
        let first = payload(BlobSender.bluetoothChunksPerTick * FrameCodec.maxChunkBytes + 1)

        blobs.send(.clipText, label: "", data: first)
        blobs.send(.clipText, label: "", data: payload(7))
        XCTAssertEqual(wire.compactMap(\.blobSize), [UInt32(first.count)])

        for tick in ticks {
            tick()
        }

        XCTAssertEqual(wire.compactMap(\.blobSize), [UInt32(first.count), 7])
        XCTAssertEqual(wire.filter(\.isBlobEnd).count, 2)
        XCTAssertTrue(wire.allSatisfy { feed($0) })
        XCTAssertEqual(received.map { $0.data }, [first, payload(7)])
    }

    func testEndingStopsABlobStillWaiting() {
        var ticks: [() -> Void] = []
        var finished: [Bool] = []
        let blobs = BlobSender(
            link: .bluetooth,
            clock: { 0 },
            schedule: { _, work in ticks.append(work) },
            lastInput: { -.infinity }
        )
        blobs.output = { _ in true }
        let first = payload(BlobSender.bluetoothChunksPerTick * FrameCodec.maxChunkBytes + 1)
        blobs.send(.clipText, label: "", data: first, finished: { finished.append($0) })
        blobs.send(.clipText, label: "", data: payload(7), finished: { finished.append($0) })

        blobs.end()

        XCTAssertEqual(finished, [false, false])
    }

    func testABlobFromThePhoneIsNotSentBack() {
        _ = sync.setShared(1)

        remote(BlobTests.a)
        settle()
        XCTAssertEqual(surface.writes.map(\.data), [BlobTests.a])

        now += ClipboardSync.writeQuiet
        sync.localChanged()
        settle()
        XCTAssertTrue(wire.isEmpty)
    }

    func testALocalCopyGoesAndTheOldContentCopiedAgainGoesToo() {
        _ = sync.setShared(1)
        remote(BlobTests.a)
        settle()
        now += ClipboardSync.writeQuiet

        surface.current = text(BlobTests.b)
        sync.localChanged()
        settle()
        XCTAssertEqual(wire.compactMap(\.blobSize), [UInt32(BlobTests.b.count)])

        wire.removeAll()
        surface.current = text(BlobTests.a)
        sync.localChanged()
        settle()
        XCTAssertEqual(wire.compactMap(\.blobSize), [UInt32(BlobTests.a.count)])
    }

    func testTheSameLocalCopyTwiceGoesOnce() {
        _ = sync.setShared(1)
        surface.current = text(BlobTests.b)

        sync.localChanged()
        settle()
        sync.localChanged()
        settle()

        XCTAssertEqual(wire.compactMap(\.blobSize).count, 1)
    }

    func testAnEchoWithDifferentBytesRightAfterAWriteIsIgnored() {
        // A platform can hand an image back re-encoded, so the hash cannot be the only thing that catches it.
        _ = sync.setShared(1)
        remote(BlobTests.a)
        settle()

        surface.current = text(BlobTests.b)
        sync.localChanged()
        settle()

        XCTAssertTrue(wire.isEmpty)
    }

    func testABurstOfChangesIsOneSend() {
        _ = sync.setShared(1)
        surface.current = text(BlobTests.b)

        sync.localChanged()
        sync.localChanged()
        sync.localChanged()
        settle()

        XCTAssertEqual(wire.compactMap(\.blobSize).count, 1)
    }

    func testSensitiveContentIsNeverSent() {
        _ = sync.setShared(1)
        surface.current = text(BlobTests.b, sensitive: true)

        sync.localChanged()
        settle()

        XCTAssertTrue(wire.isEmpty)
    }

    func testNothingMovesUntilThePhoneTurnsItOn() {
        surface.current = text(BlobTests.b)

        sync.localChanged()
        settle()
        XCTAssertFalse(sync.take(kind: 0, size: 1, label: ""))

        XCTAssertTrue(wire.isEmpty)
        XCTAssertTrue(surface.writes.isEmpty)
    }

    func testTurningItOffStopsBothDirections() {
        _ = sync.setShared(1)
        XCTAssertTrue(sync.take(kind: 0, size: UInt32(BlobTests.a.count), label: ""))
        XCTAssertTrue(sync.setShared(0))
        surface.current = text(BlobTests.b)

        XCTAssertFalse(sync.take(seq: 0, data: BlobTests.a))
        XCTAssertFalse(sync.take(sha256: BlobTests.hash(BlobTests.a)))
        sync.localChanged()
        settle()

        XCTAssertTrue(wire.isEmpty)
        XCTAssertTrue(surface.writes.isEmpty)
    }

    func testTheOwnersToggleOutranksThePhone() {
        toggle = false
        XCTAssertFalse(sync.setShared(1))

        toggle = true
        XCTAssertTrue(sync.setShared(1))
        toggle = false
        surface.current = text(BlobTests.b)
        sync.localChanged()
        settle()

        XCTAssertTrue(wire.isEmpty)
        XCTAssertFalse(sync.take(kind: 0, size: 1, label: ""))
    }

    func testAValueThatIsNeitherOnNorOffIsRefused() {
        XCTAssertFalse(sync.setShared(2))

        sync.localChanged()
        XCTAssertTrue(pending.isEmpty)
    }

    func testTurningItOnSendsNothingUntilTheNextCopy() {
        surface.current = text(BlobTests.b)

        _ = sync.setShared(1)
        settle()

        XCTAssertTrue(wire.isEmpty)
    }

    func testACopyOverTheCapStaysHere() {
        _ = sync.setShared(1)
        surface.current = text([UInt8](repeating: 0, count: ClipboardSync.maxTextBytes + 1))

        sync.localChanged()
        settle()

        XCTAssertTrue(wire.isEmpty)
    }

    func testTextThatIsNotUtf8IsDropped() {
        _ = sync.setShared(1)

        XCTAssertFalse(remoteLastFrame(.clipText, [0xC3, 0x28]))

        XCTAssertTrue(surface.writes.isEmpty)
    }

    func testAnEmptyBlobIsDropped() {
        _ = sync.setShared(1)

        XCTAssertFalse(remoteLastFrame(.clipText, []))

        XCTAssertTrue(surface.writes.isEmpty)
    }

    func testAKindTheClipboardDoesNotTakeIsDropped() {
        _ = sync.setShared(1)

        XCTAssertFalse(sync.take(kind: BlobKind.appList.rawValue, size: 1, label: ""))
    }

    func testAPngIsWritten() {
        let image = png(7680, 4320)
        _ = sync.setShared(1)

        XCTAssertTrue(remoteLastFrame(.clipImage, image))
        settle()

        XCTAssertEqual(surface.writes, [ClipboardContent(kind: .clipImage, data: image)])
    }

    func testAPngThatWouldDecodeToTooMuchIsDropped() {
        _ = sync.setShared(1)

        let sizes: [(UInt32, UInt32)] = [(.max, .max), (100_000, 100_000), (0, 100)]
        for (width, height) in sizes {
            XCTAssertFalse(remoteLastFrame(.clipImage, png(width, height)))
        }

        XCTAssertTrue(surface.writes.isEmpty)
    }

    func testAnImageThatIsNotAPngIsDropped() {
        _ = sync.setShared(1)

        XCTAssertFalse(remoteLastFrame(.clipImage, payload(100)))

        XCTAssertTrue(surface.writes.isEmpty)
    }

    func testAClipboardThatFailsEndsNothing() {
        _ = sync.setShared(1)
        surface.fails = true

        remote(BlobTests.a)
        settle()
        now += ClipboardSync.writeQuiet
        sync.localChanged()
        settle()

        XCTAssertTrue(wire.isEmpty)
    }

    func testASendThatFailedIsTriedAgainOnTheNextCopy() {
        var fail = true
        let blobs = sender()
        blobs.output = { [unowned self] frame in
            guard !fail else { return false }
            self.wire.append(frame)
            return true
        }
        let failing = syncOver(blobs)
        _ = failing.setShared(1)
        surface.current = text(BlobTests.b)

        failing.localChanged()
        settle()
        fail = false
        failing.localChanged()
        settle()

        XCTAssertEqual(wire.filter(\.isBlobEnd).count, 1)
    }

    func testTurningItOffPartWayStopsTheTransfer() {
        var stopping: ClipboardSync?
        let blobs = sender { _ in _ = stopping?.setShared(0) }
        let partWay = syncOver(blobs)
        stopping = partWay
        _ = partWay.setShared(1)
        surface.current = text(payload(100 * FrameCodec.maxChunkBytes))

        partWay.localChanged()
        settle()

        XCTAssertEqual(wire.filter(\.isBlobChunk).count, BlobSender.bluetoothChunksPerTick)
        XCTAssertFalse(wire.contains(where: \.isBlobEnd))
    }

    func testTheSameContentGoesAgainOnceSharingWasTurnedOffAndOn() {
        // The phone may have been on another laptop since, so what this one remembers sending proves nothing.
        _ = sync.setShared(1)
        surface.current = text(BlobTests.b)
        sync.localChanged()
        settle()

        _ = sync.setShared(0)
        _ = sync.setShared(1)
        sync.localChanged()
        settle()

        XCTAssertEqual(wire.compactMap(\.blobSize).count, 2)
    }

    func testAnEndedSyncTakesNothing() {
        _ = sync.setShared(1)
        sync.end()
        surface.current = text(BlobTests.b)

        sync.localChanged()
        settle()

        XCTAssertFalse(sync.take(kind: 0, size: 1, label: ""))
        XCTAssertTrue(wire.isEmpty)
    }
}
