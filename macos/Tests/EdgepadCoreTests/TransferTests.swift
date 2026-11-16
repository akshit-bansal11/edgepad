import CryptoKit
import Foundation
import XCTest

@testable import EdgepadCore

/// The file transfer lanes on real temporary folders and a fake scheduler, so nothing sleeps. The same cases as the
/// shared core's TransferTests where the two sides do the same thing.
final class TransferTests: XCTestCase {
    private var root = URL(fileURLWithPath: "/")
    private var destination: String { root.appendingPathComponent("Downloads").path }
    private var partial: String { root.appendingPathComponent("Partial").path }

    private var replies: [Frame] = []
    private var arrived: [String] = []

    override func setUpWithError() throws {
        root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
    }

    override func tearDownWithError() throws {
        try FileManager.default.removeItem(at: root)
    }

    private func sha(_ bytes: [UInt8]) -> [UInt8] {
        Array(SHA256.hash(data: bytes))
    }

    private func status(_ id: UInt8, _ code: XferCode, _ offset: UInt64 = 0) -> Frame {
        .xferStatus(id: id, status: code.rawValue, offset: offset)
    }

    private func receiver(
        freeSpace: ((String) -> Int64)? = nil, now: Date = Date()
    ) -> TransferReceiver {
        let made = TransferReceiver(
            destination: destination, partialFolder: partial, arrived: { [unowned self] in self.arrived.append($0) },
            freeSpace: freeSpace, now: now)
        made.reply = { [unowned self] in self.replies.append($0) }
        return made
    }

    // MARK: Receiving

    func testAFileArrivesWholeIsCheckedAndSavedUnderItsOwnName() throws {
        let body = Array("hello".utf8)
        let incoming = receiver()
        XCTAssertTrue(incoming.take(begin: 1, size: 5, name: "a.txt"))
        XCTAssertTrue(incoming.take(chunk: 1, offset: 0, data: body))
        XCTAssertTrue(incoming.take(end: 1, sha256: sha(body)))
        XCTAssertEqual(replies, [status(1, .ready), status(1, .saved, 5)])
        XCTAssertEqual(arrived, [destination + "/a.txt"])
        XCTAssertEqual(try Data(contentsOf: URL(fileURLWithPath: destination + "/a.txt")), Data(body))
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath: partial), [])
        XCTAssertFalse(incoming.busy)
    }

    func testAFileIsNeverSavedOverAnother() throws {
        try FileManager.default.createDirectory(atPath: destination, withIntermediateDirectories: true)
        try Data("old".utf8).write(to: URL(fileURLWithPath: destination + "/a.txt"))
        let body = Array("new".utf8)
        let incoming = receiver()
        _ = incoming.take(begin: 1, size: 3, name: "a.txt")
        _ = incoming.take(chunk: 1, offset: 0, data: body)
        _ = incoming.take(end: 1, sha256: sha(body))
        XCTAssertEqual(arrived, [destination + "/a (1).txt"])
        XCTAssertEqual(try Data(contentsOf: URL(fileURLWithPath: destination + "/a.txt")), Data("old".utf8))
    }

    func testAWrongHashDeletesThePartAndSaysSo() throws {
        let incoming = receiver()
        _ = incoming.take(begin: 1, size: 3, name: "a.txt")
        _ = incoming.take(chunk: 1, offset: 0, data: Array("abc".utf8))
        _ = incoming.take(end: 1, sha256: sha(Array("xyz".utf8)))
        XCTAssertEqual(replies.last, status(1, .badHash))
        XCTAssertTrue(arrived.isEmpty)
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath: partial), [])
    }

    func testAChunkOutOfOrderIsAnsweredWithTheBytesHeld() {
        let incoming = receiver()
        _ = incoming.take(begin: 1, size: 6, name: "a.txt")
        _ = incoming.take(chunk: 1, offset: 0, data: [1, 2])
        _ = incoming.take(chunk: 1, offset: 4, data: [5, 6])
        XCTAssertEqual(replies.last, status(1, .progress, 2))
    }

    func testAChunkPastTheSizeIsRefusedAndTheFileDropped() {
        let incoming = receiver()
        _ = incoming.take(begin: 1, size: 2, name: "a.txt")
        _ = incoming.take(chunk: 1, offset: 0, data: [1, 2, 3])
        XCTAssertEqual(replies.last, status(1, .rejected))
        XCTAssertFalse(incoming.busy)
    }

    func testAnEndShortOfTheSizeIsNotABadFile() {
        let incoming = receiver()
        _ = incoming.take(begin: 1, size: 4, name: "a.txt")
        _ = incoming.take(chunk: 1, offset: 0, data: [1, 2])
        _ = incoming.take(end: 1, sha256: sha([1, 2]))
        XCTAssertEqual(replies.last, status(1, .progress, 2))
        XCTAssertTrue(incoming.busy)
    }

    func testAnEndedSessionKeepsThePartAndTheNextBeginResumesIt() throws {
        let body = Array("hello world".utf8)
        let first = receiver()
        _ = first.take(begin: 1, size: 11, name: "w.txt")
        _ = first.take(chunk: 1, offset: 0, data: Array(body[..<5]))
        first.end()
        replies = []

        let second = receiver()
        _ = second.take(begin: 2, size: 11, name: "w.txt")
        XCTAssertEqual(replies, [status(2, .ready, 5)])
        _ = second.take(chunk: 2, offset: 5, data: Array(body[5...]))
        // The digest is of the whole file, so the held bytes had to be hashed again from disk.
        _ = second.take(end: 2, sha256: sha(body))
        XCTAssertEqual(replies.last, status(2, .saved, 11))
        XCTAssertEqual(try Data(contentsOf: URL(fileURLWithPath: destination + "/w.txt")), Data(body))
    }

    func testAnUnusableNameATooBigFileAndAFullDiskAreRefusedAndASecondFileIsBusy() {
        let incoming = receiver(freeSpace: { _ in 10 })
        _ = incoming.take(begin: 1, size: 1, name: "CON")
        _ = incoming.take(begin: 2, size: TransferLimits.maxBytes + 1, name: "big.bin")
        _ = incoming.take(begin: 3, size: 1, name: "a.txt")
        XCTAssertEqual(replies, [status(1, .rejected), status(2, .rejected), status(3, .rejected)])

        replies = []
        let roomy = receiver()
        _ = roomy.take(begin: 4, size: 1, name: "a.txt")
        _ = roomy.take(begin: 5, size: 1, name: "b.txt")
        XCTAssertEqual(replies, [status(4, .ready), status(5, .busy)])
    }

    func testCancelDeletesThePartAndTellsTheSender() throws {
        let incoming = receiver()
        _ = incoming.take(begin: 1, size: 3, name: "a.txt")
        _ = incoming.take(chunk: 1, offset: 0, data: [1])
        XCTAssertTrue(incoming.cancel())
        XCTAssertEqual(replies.last, status(1, .cancelled))
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath: partial), [])
        XCTAssertFalse(incoming.cancel())
    }

    func testThePhonesCancelledDropsTheFileAndNothingElseDoes() {
        let incoming = receiver()
        _ = incoming.take(begin: 1, size: 3, name: "a.txt")
        XCTAssertFalse(incoming.take(status: XferCode.saved.rawValue, id: 1))
        XCTAssertFalse(incoming.take(status: XferCode.cancelled.rawValue, id: 9))
        XCTAssertTrue(incoming.take(status: XferCode.cancelled.rawValue, id: 1))
        XCTAssertFalse(incoming.busy)
    }

    func testAPartOlderThanAWeekIsDeletedWhenTheReceiverIsMade() throws {
        try FileManager.default.createDirectory(atPath: partial, withIntermediateDirectories: true)
        let old = partial + "/old.5.part"
        try Data("12345".utf8).write(to: URL(fileURLWithPath: old))
        let nextMonth = Date().addingTimeInterval(30 * 24 * 60 * 60)
        _ = receiver(now: nextMonth)
        XCTAssertFalse(FileManager.default.fileExists(atPath: old))
    }

    // MARK: Sending

    private var sent: [Frame] = []
    private var timers: [(delay: TimeInterval, work: () -> Void)] = []
    private var results: [XferCode] = []
    private var alive = true
    private var backlog = 0

    private func sender() -> TransferSender {
        let made = TransferSender(
            link: .network, schedule: { [unowned self] delay, work in self.timers.append((delay, work)) },
            lastInput: { -.infinity })
        made.output = { [unowned self] frame in
            guard self.alive else { return false }
            self.sent.append(frame)
            return true
        }
        made.backlog = { [unowned self] in self.backlog }
        return made
    }

    private func file(_ bytes: [UInt8], named name: String = "out.bin") throws -> String {
        let path = root.appendingPathComponent(name).path
        try Data(bytes).write(to: URL(fileURLWithPath: path))
        return path
    }

    /// Fires every tick that is waiting, but not the 30 s timeouts.
    private func runTicks() {
        while let index = timers.firstIndex(where: { $0.delay < 1 }) {
            timers.remove(at: index).work()
        }
    }

    func testAFileGoesAsBeginChunksAndEndAndIsDoneWhenTheyAnswerSaved() throws {
        let body = (0..<600).map { UInt8($0 % 251) }
        let outgoing = sender()
        outgoing.send(path: try file(body)) { [unowned self] in self.results.append($0) }
        XCTAssertEqual(sent, [.xferBegin(id: 1, size: 600, name: "out.bin")])
        XCTAssertTrue(outgoing.take(status: XferCode.ready.rawValue, id: 1, offset: 0))
        runTicks()
        let size = FrameCodec.maxXferChunkBytes
        XCTAssertEqual(
            Array(sent.dropFirst()),
            [
                .xferChunk(id: 1, offset: 0, data: Array(body[..<size])),
                .xferChunk(id: 1, offset: UInt64(size), data: Array(body[size..<(2 * size)])),
                .xferChunk(id: 1, offset: UInt64(2 * size), data: Array(body[(2 * size)...])),
                .xferEnd(id: 1, sha256: sha(body)),
            ])
        XCTAssertTrue(results.isEmpty)
        XCTAssertTrue(outgoing.take(status: XferCode.saved.rawValue, id: 1, offset: 600))
        XCTAssertEqual(results, [.saved])
    }

    func testReadyAtAnOffsetResumesThereAndStillHashesTheWholeFile() throws {
        let body = (0..<500).map { UInt8($0 % 241) }
        let outgoing = sender()
        outgoing.send(path: try file(body)) { [unowned self] in self.results.append($0) }
        _ = outgoing.take(status: XferCode.ready.rawValue, id: 1, offset: 300)
        runTicks()
        guard case .xferChunk(_, let first, _)? = sent.dropFirst().first else { return XCTFail("No chunk") }
        XCTAssertEqual(first, 300)
        XCTAssertEqual(sent.last, .xferEnd(id: 1, sha256: sha(body)))
    }

    func testAnAnswerForAnotherFileIsNotTaken() throws {
        let outgoing = sender()
        outgoing.send(path: try file([1])) { _ in }
        XCTAssertFalse(outgoing.take(status: XferCode.ready.rawValue, id: 2, offset: 0))
        XCTAssertFalse(outgoing.take(status: 99, id: 1, offset: 0))
    }

    func testNoAnswerInThirtySecondsEndsItTimedOut() throws {
        let outgoing = sender()
        outgoing.send(path: try file([1])) { [unowned self] in self.results.append($0) }
        XCTAssertEqual(timers.count, 1)
        XCTAssertEqual(timers[0].delay, TransferLimits.answerTimeout)
        timers.removeFirst().work()
        XCTAssertEqual(results, [.timedOut])
    }

    func testATimeoutArmedForAnEarlierWaitDoesNotEndALaterOne() throws {
        let outgoing = sender()
        outgoing.send(path: try file([1])) { [unowned self] in self.results.append($0) }
        let stale = timers.removeFirst()
        _ = outgoing.take(status: XferCode.ready.rawValue, id: 1, offset: 0)
        runTicks()
        stale.work()
        XCTAssertTrue(results.isEmpty)
    }

    func testCancelTellsThePhoneAndEndsItCancelled() throws {
        let outgoing = sender()
        outgoing.send(path: try file([1])) { [unowned self] in self.results.append($0) }
        XCTAssertTrue(outgoing.cancel())
        XCTAssertEqual(sent.last, status(1, .cancelled))
        XCTAssertEqual(results, [.cancelled])
        XCTAssertFalse(outgoing.cancel())
    }

    func testADeadLinkEndsItSessionEnded() throws {
        let outgoing = sender()
        alive = false
        outgoing.send(path: try file([1])) { [unowned self] in self.results.append($0) }
        XCTAssertEqual(results, [.sessionEnded])
    }

    func testAFileThatCannotBeReadEndsItWriteFailed() {
        let outgoing = sender()
        outgoing.send(path: root.path + "/missing") { [unowned self] in self.results.append($0) }
        XCTAssertEqual(results, [.writeFailed])
        XCTAssertTrue(sent.isEmpty)
    }

    func testASecondFileWaitsForTheFirstAndTakesTheNextId() throws {
        let outgoing = sender()
        outgoing.send(path: try file([1], named: "one")) { [unowned self] in self.results.append($0) }
        outgoing.send(path: try file([2], named: "two")) { [unowned self] in self.results.append($0) }
        XCTAssertEqual(sent.count, 1)
        _ = outgoing.take(status: XferCode.rejected.rawValue, id: 1, offset: 0)
        XCTAssertEqual(results, [.rejected])
        XCTAssertEqual(sent.last, .xferBegin(id: 2, size: 1, name: "two"))
    }

    func testNoChunkGoesWhileTheLinkIsBackedUp() throws {
        let outgoing = sender()
        outgoing.send(path: try file([1, 2, 3])) { _ in }
        backlog = BlobSender.maxBacklog
        _ = outgoing.take(status: XferCode.ready.rawValue, id: 1, offset: 0)
        XCTAssertEqual(sent.count, 1)
        backlog = 0
        runTicks()
        XCTAssertEqual(sent.count, 3)
    }

    func testTheSessionEndingFinishesWhatWasOnItsWayAndWhatWasWaiting() throws {
        let outgoing = sender()
        outgoing.send(path: try file([1], named: "one")) { [unowned self] in self.results.append($0) }
        outgoing.send(path: try file([2], named: "two")) { [unowned self] in self.results.append($0) }
        outgoing.end()
        XCTAssertEqual(results, [.sessionEnded, .sessionEnded])
        outgoing.send(path: root.path) { [unowned self] in self.results.append($0) }
        XCTAssertEqual(results.count, 3)
    }

    // MARK: The transfer socket

    func testTheFirstFrameTellsATransferSocketFromAHello() {
        let version = ProtocolConstants.version
        XCTAssertEqual(FirstFrame.classify([]), .needMore)
        XCTAssertEqual(FirstFrame.classify(FrameCodec.encode(.hello(version: version))), .other)
        let bulk = FrameCodec.encode(.bulkHello(version: version))
        XCTAssertEqual(FirstFrame.classify(Array(bulk.dropLast())), .needMore)
        XCTAssertEqual(FirstFrame.classify(bulk), .bulk(version: version, rest: []))
        let ping = FrameCodec.encode(.ping(time: 1))
        XCTAssertEqual(FirstFrame.classify(bulk + ping), .bulk(version: version, rest: ping))
        var wrongMagic = bulk
        wrongMagic[1] ^= 0xFF
        XCTAssertEqual(FirstFrame.classify(wrongMagic), .invalid)
    }

    func testOnlyTransferFramesAreLegalOnATransferSocket() {
        XCTAssertTrue(BulkLink.isLegal(.xferBegin(id: 1, size: 1, name: "a")))
        XCTAssertTrue(BulkLink.isLegal(.xferChunk(id: 1, offset: 0, data: [1])))
        XCTAssertTrue(BulkLink.isLegal(.xferEnd(id: 1, sha256: [UInt8](repeating: 0, count: 32))))
        XCTAssertTrue(BulkLink.isLegal(status(1, .ready)))
        XCTAssertFalse(BulkLink.isLegal(.bulkHello(version: ProtocolConstants.version)))
        XCTAssertFalse(BulkLink.isLegal(.hello(version: ProtocolConstants.version)))
        XCTAssertFalse(BulkLink.isLegal(.ping(time: 1)))
        XCTAssertFalse(BulkLink.isLegal(.runAction(id: 1)))
    }

    func testATransferSocketPassesTransferFramesOnAndClosesOnAnythingElseOrSilence() {
        let socket = FakeTransport()
        var seen: [Frame] = []
        var closed = 0
        let link = BulkLink(
            address: "a", transport: socket, onFrame: { seen.append($0) }, onClosed: { closed += 1 })
        XCTAssertTrue(link.start())
        XCTAssertEqual(socket.sent, [FrameCodec.encode(.helloAck(version: ProtocolConstants.version))])
        let begin = Frame.xferBegin(id: 1, size: 1, name: "a")
        link.receive(FrameCodec.encode(begin))
        XCTAssertEqual(seen, [begin])
        link.receive(FrameCodec.encode(.bulkHello(version: ProtocolConstants.version)))
        XCTAssertTrue(socket.closed)
        XCTAssertEqual(closed, 1)
        link.receive(FrameCodec.encode(begin))
        link.close()
        XCTAssertEqual(seen, [begin])
        XCTAssertEqual(closed, 1)
        XCTAssertEqual(socket.closeCount, 1)
    }
}
