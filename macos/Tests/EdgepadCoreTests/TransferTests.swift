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

    private var receiverTimers: [(delay: TimeInterval, work: () -> Void)] = []
    private var receiverClock: TimeInterval = 0

    private func receiver(
        freeSpace: ((String) -> Int64)? = nil, now: Date = Date()
    ) -> TransferReceiver {
        let made = TransferReceiver(
            destination: destination, partialFolder: partial, arrived: { [unowned self] in self.arrived.append($0) },
            freeSpace: freeSpace, clock: { [unowned self] in self.receiverClock },
            schedule: { [unowned self] delay, work in self.receiverTimers.append((delay, work)) }, now: now)
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

    func testTheFileIsMarkedWhileStillAPartBeforeItIsMovedIntoDownloads() throws {
        var seen: [(path: String, inDownloads: Bool)] = []
        let incoming = TransferReceiver(
            destination: destination, partialFolder: partial,
            mark: { [unowned self] path in
                let there = FileManager.default.fileExists(atPath: self.destination + "/a.txt")
                seen.append((path, there))
            })
        incoming.reply = { _ in }
        _ = incoming.take(begin: 1, size: 1, name: "a.txt")
        _ = incoming.take(chunk: 1, offset: 0, data: [7])
        _ = incoming.take(end: 1, sha256: sha([7]))
        XCTAssertEqual(seen.map(\.path), [partial + "/a.txt.1.part"])
        XCTAssertEqual(seen.map(\.inDownloads), [false])
        XCTAssertTrue(FileManager.default.fileExists(atPath: destination + "/a.txt"))
    }

    func testAnUnreadableFreeSpaceRefusesTheFile() {
        XCTAssertLessThan(TransferReceiver.freeSpaceOf(root.path + "/missing/nowhere"), 0)
        XCTAssertGreaterThan(TransferReceiver.freeSpaceOf(root.path), 0)
        let incoming = receiver(freeSpace: { _ in -1 })
        _ = incoming.take(begin: 1, size: 1, name: "a.txt")
        XCTAssertEqual(replies, [status(1, .rejected)])
    }

    func testATransferIdleForThirtySecondsIsDroppedAndItsPartKept() throws {
        let incoming = receiver()
        _ = incoming.take(begin: 1, size: 9, name: "a.txt")
        _ = incoming.take(chunk: 1, offset: 0, data: [1, 2])
        XCTAssertEqual(receiverTimers.map(\.delay), [TransferLimits.idleTimeout])
        receiverClock = 20
        _ = incoming.take(chunk: 1, offset: 2, data: [3])
        receiverClock = 30
        receiverTimers.removeFirst().work()
        // A chunk came 10 s ago: still going, and the timer is set again for the 20 s left.
        XCTAssertTrue(incoming.busy)
        XCTAssertEqual(receiverTimers.map(\.delay), [20])
        receiverClock = 50
        receiverTimers.removeFirst().work()
        XCTAssertFalse(incoming.busy)
        XCTAssertEqual(replies.last, status(1, .cancelled))
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath: partial), ["a.txt.9.part"])
        // The next file is not turned away as busy.
        _ = incoming.take(begin: 2, size: 1, name: "b.txt")
        XCTAssertEqual(replies.last, status(2, .ready))
    }

    func testAChunkThatCannotBePlacedStillCountsAsActivity() throws {
        let incoming = receiver()
        _ = incoming.take(begin: 1, size: 9, name: "a.txt")
        receiverClock = 25
        // Out of order: answered with the bytes held, but the sender is alive.
        _ = incoming.take(chunk: 1, offset: 4, data: [1])
        receiverClock = 40
        receiverTimers.removeFirst().work()
        XCTAssertTrue(incoming.busy)
        XCTAssertEqual(receiverTimers.map(\.delay), [15])
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

    func testATimeoutTellsThePhoneCancelled() throws {
        let outgoing = sender()
        outgoing.send(path: try file([1])) { [unowned self] in self.results.append($0) }
        timers.removeFirst().work()
        XCTAssertEqual(sent.last, status(1, .cancelled))
        XCTAssertEqual(results, [.timedOut])
    }

    func testAShortAnswerToEndSendsAgainFromThereAndHashesTheWholeFile() throws {
        let body = (0..<600).map { UInt8($0 % 251) }
        let outgoing = sender()
        outgoing.send(path: try file(body)) { [unowned self] in self.results.append($0) }
        _ = outgoing.take(status: XferCode.ready.rawValue, id: 1, offset: 0)
        runTicks()
        let size = FrameCodec.maxXferChunkBytes
        sent = []
        XCTAssertTrue(outgoing.take(status: XferCode.progress.rawValue, id: 1, offset: UInt64(size)))
        runTicks()
        XCTAssertEqual(
            sent,
            [
                .xferChunk(id: 1, offset: UInt64(size), data: Array(body[size..<(2 * size)])),
                .xferChunk(id: 1, offset: UInt64(2 * size), data: Array(body[(2 * size)...])),
                .xferEnd(id: 1, sha256: sha(body)),
            ])
        _ = outgoing.take(status: XferCode.saved.rawValue, id: 1, offset: 600)
        XCTAssertEqual(results, [.saved])
    }

    func testAStaleProgressAfterARewindDoesNotRewindAgain() throws {
        let size = FrameCodec.maxXferChunkBytes
        let body = (0..<(5 * size)).map { UInt8($0 % 251) }
        // Busy input: two chunks a tick, so the rewind stops part way and the phase stays sending.
        let outgoing = TransferSender(
            link: .network, schedule: { [unowned self] delay, work in self.timers.append((delay, work)) },
            lastInput: { ProcessInfo.processInfo.systemUptime })
        outgoing.output = { [unowned self] frame in
            self.sent.append(frame)
            return true
        }
        outgoing.send(path: try file(body)) { [unowned self] in self.results.append($0) }
        _ = outgoing.take(status: XferCode.ready.rawValue, id: 1, offset: 0)
        runTicks()
        XCTAssertEqual(sent.last, .xferEnd(id: 1, sha256: sha(body)))
        sent = []
        _ = outgoing.take(status: XferCode.progress.rawValue, id: 1, offset: UInt64(size))
        // Another PROGRESS for the same offset, already in flight when the rewind happened.
        _ = outgoing.take(status: XferCode.progress.rawValue, id: 1, offset: UInt64(size))
        let again = sent.filter {
            if case .xferChunk(_, let offset, _) = $0 { return offset == UInt64(size) }
            return false
        }
        XCTAssertEqual(again.count, 1)
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

    // MARK: Pause and resume

    private var senderHeld: [Bool] = []
    private var receiverHeld: [Bool] = []

    /// A sender that has begun a 500 byte file at `offset` with the link backed up, so no chunk has gone yet.
    private func heldBack(from offset: UInt64 = 0) throws -> (TransferSender, [UInt8]) {
        let body = (0..<500).map { UInt8($0 % 241) }
        let outgoing = sender()
        outgoing.pausedChanged = { [unowned self] in self.senderHeld.append($0) }
        outgoing.send(path: try file(body)) { [unowned self] in self.results.append($0) }
        backlog = BlobSender.maxBacklog
        _ = outgoing.take(status: XferCode.ready.rawValue, id: 1, offset: offset)
        return (outgoing, body)
    }

    private func chunkOffsets() -> [UInt64] {
        sent.compactMap {
            if case .xferChunk(_, let offset, _) = $0 { return offset }
            return nil
        }
    }

    func testTheLocalOnlyCodesAreOutOfTheWireRange() {
        XCTAssertEqual(XferCode.paused.rawValue, 8)
        XCTAssertEqual(XferCode.resume.rawValue, 9)
        XCTAssertEqual(XferCode.timedOut.rawValue, 0xF0)
        XCTAssertEqual(XferCode.sessionEnded.rawValue, 0xF1)
        XCTAssertEqual(TransferLimits.pauseCap, 600)
    }

    func testASenderPauseHoldsTheFileAndResumeFinishesItWithTheSameHash() throws {
        let (outgoing, body) = try heldBack()
        XCTAssertTrue(outgoing.pause())
        XCTAssertEqual(sent.last, status(1, .paused, 0))
        XCTAssertEqual(senderHeld, [true])
        backlog = 0
        runTicks()
        XCTAssertTrue(chunkOffsets().isEmpty)
        XCTAssertTrue(outgoing.resume())
        XCTAssertEqual(sent.last, status(1, .resume, 0))
        // Nothing goes until the receiver names where it stands.
        runTicks()
        XCTAssertTrue(chunkOffsets().isEmpty)
        XCTAssertTrue(outgoing.take(status: XferCode.ready.rawValue, id: 1, offset: 0))
        runTicks()
        XCTAssertEqual(sent.last, .xferEnd(id: 1, sha256: sha(body)))
        XCTAssertTrue(outgoing.take(status: XferCode.saved.rawValue, id: 1, offset: 500))
        XCTAssertEqual(results, [.saved])
        XCTAssertEqual(senderHeld, [true, false])
    }

    func testAReadyBelowWhatWasSentRewindsTheSenderAndTheHashStaysWhole() throws {
        let (outgoing, body) = try heldBack(from: 300)
        XCTAssertTrue(outgoing.pause())
        XCTAssertTrue(outgoing.resume())
        backlog = 0
        XCTAssertTrue(outgoing.take(status: XferCode.ready.rawValue, id: 1, offset: 100))
        runTicks()
        XCTAssertEqual(chunkOffsets().first, 100)
        XCTAssertEqual(sent.last, .xferEnd(id: 1, sha256: sha(body)))
    }

    func testTheReceiversPauseHoldsTheSenderUntilItsResumeAndReady() throws {
        let (outgoing, _) = try heldBack()
        XCTAssertTrue(outgoing.take(status: XferCode.paused.rawValue, id: 1, offset: 0))
        XCTAssertEqual(senderHeld, [true])
        backlog = 0
        runTicks()
        XCTAssertTrue(chunkOffsets().isEmpty)
        XCTAssertTrue(outgoing.take(status: XferCode.resume.rawValue, id: 1, offset: 0))
        XCTAssertEqual(senderHeld, [true, false])
        runTicks()
        XCTAssertTrue(chunkOffsets().isEmpty)
        XCTAssertTrue(outgoing.take(status: XferCode.ready.rawValue, id: 1, offset: 0))
        runTicks()
        XCTAssertEqual(chunkOffsets().first, 0)
    }

    func testWhenBothEndsPauseEachLetsGoOnItsOwnAndNeitherStaysStuck() throws {
        for senderFirst in [true, false] {
            sent = []
            timers = []
            results = []
            senderHeld = []
            let (outgoing, _) = try heldBack()
            XCTAssertTrue(outgoing.pause())
            XCTAssertTrue(outgoing.take(status: XferCode.paused.rawValue, id: 1, offset: 0))
            backlog = 0
            if senderFirst {
                XCTAssertTrue(outgoing.resume())
                XCTAssertEqual(senderHeld, [true])
                XCTAssertTrue(outgoing.take(status: XferCode.resume.rawValue, id: 1, offset: 0))
            } else {
                XCTAssertTrue(outgoing.take(status: XferCode.resume.rawValue, id: 1, offset: 0))
                XCTAssertEqual(senderHeld, [true])
                XCTAssertTrue(outgoing.resume())
            }
            XCTAssertEqual(senderHeld, [true, false])
            XCTAssertTrue(outgoing.take(status: XferCode.ready.rawValue, id: 1, offset: 0))
            runTicks()
            XCTAssertEqual(chunkOffsets().first, 0)
            XCTAssertTrue(outgoing.cancel())
            backlog = BlobSender.maxBacklog
        }
    }

    func testNoAnswerTimeoutRunsWhileHeldAndItRunsAgainAfterTheRelease() throws {
        let (outgoing, _) = try heldBack()
        XCTAssertTrue(outgoing.pause())
        while let index = timers.firstIndex(where: { $0.delay == TransferLimits.answerTimeout }) {
            timers.remove(at: index).work()
        }
        XCTAssertTrue(results.isEmpty)
        XCTAssertTrue(outgoing.resume())
        guard let index = timers.lastIndex(where: { $0.delay == TransferLimits.answerTimeout }) else {
            return XCTFail("No answer timeout armed after the release")
        }
        timers[index].work()
        XCTAssertEqual(results, [.timedOut])
    }

    func testAHoldOlderThanTenMinutesEndsItCancelledWithoutTellingThePhone() throws {
        for peer in [false, true] {
            sent = []
            timers = []
            results = []
            senderHeld = []
            let (outgoing, _) = try heldBack()
            if peer {
                XCTAssertTrue(outgoing.take(status: XferCode.paused.rawValue, id: 1, offset: 0))
            } else {
                XCTAssertTrue(outgoing.pause())
            }
            guard let index = timers.firstIndex(where: { $0.delay == TransferLimits.pauseCap }) else {
                return XCTFail("No cap armed")
            }
            sent = []
            timers.remove(at: index).work()
            XCTAssertEqual(results, [.cancelled])
            XCTAssertTrue(sent.isEmpty)
            XCTAssertEqual(senderHeld, [true, false])
        }
    }

    func testACapArmedForAnEarlierHoldDoesNotEndALaterOne() throws {
        let (outgoing, _) = try heldBack()
        XCTAssertTrue(outgoing.pause())
        guard let index = timers.firstIndex(where: { $0.delay == TransferLimits.pauseCap }) else {
            return XCTFail("No cap armed")
        }
        let first = timers.remove(at: index)
        XCTAssertTrue(outgoing.resume())
        XCTAssertTrue(outgoing.pause())
        first.work()
        XCTAssertTrue(results.isEmpty)
    }

    func testTheSendersCancelWhilePausedTellsThePhoneAndEndsItCancelled() throws {
        let (outgoing, _) = try heldBack()
        XCTAssertTrue(outgoing.pause())
        XCTAssertTrue(outgoing.cancel())
        XCTAssertEqual(sent.last, status(1, .cancelled))
        XCTAssertEqual(results, [.cancelled])
        XCTAssertEqual(senderHeld, [true, false])
    }

    func testAPauseOrResumeThatMeansNothingIsRefused() throws {
        let idle = sender()
        XCTAssertFalse(idle.pause())
        XCTAssertFalse(idle.resume())
        let (outgoing, _) = try heldBack()
        XCTAssertFalse(outgoing.resume())
        XCTAssertTrue(outgoing.pause())
        XCTAssertFalse(outgoing.pause())
        XCTAssertTrue(outgoing.resume())
        XCTAssertFalse(outgoing.resume())
        // After END the answer is only waited for.
        backlog = 0
        XCTAssertTrue(outgoing.take(status: XferCode.ready.rawValue, id: 1, offset: 0))
        runTicks()
        XCTAssertFalse(outgoing.pause())
        // A status for another file changes nothing, and neither does a local-only code arriving on the wire.
        XCTAssertFalse(outgoing.take(status: XferCode.paused.rawValue, id: 2, offset: 0))
        XCTAssertFalse(outgoing.take(status: XferCode.timedOut.rawValue, id: 1, offset: 0))
        XCTAssertTrue(results.isEmpty)
    }

    func testTheReceiversPauseTellsTheSenderStillTakesTheChunkInFlightAndResumeAnswersReady() throws {
        var held: [Bool] = []
        let incoming = receiver()
        incoming.pausedChanged = { held.append($0) }
        _ = incoming.take(begin: 1, size: 6, name: "a.txt")
        _ = incoming.take(chunk: 1, offset: 0, data: [1, 2])
        XCTAssertTrue(incoming.pause())
        XCTAssertEqual(replies.last, status(1, .paused, 2))
        XCTAssertFalse(incoming.pause())
        _ = incoming.take(chunk: 1, offset: 2, data: [3])
        XCTAssertTrue(incoming.resume())
        XCTAssertEqual(Array(replies.suffix(2)), [status(1, .resume, 3), status(1, .ready, 3)])
        XCTAssertEqual(held, [true, false])
        XCTAssertFalse(incoming.resume())
    }

    func testARemoteResumeIsAnsweredWithReadyAtTheHeldOffsetAndNotBeforeTheOwnHoldEnds() {
        var held: [Bool] = []
        let incoming = receiver()
        incoming.pausedChanged = { held.append($0) }
        _ = incoming.take(begin: 1, size: 6, name: "a.txt")
        _ = incoming.take(chunk: 1, offset: 0, data: [1, 2])
        XCTAssertTrue(incoming.take(status: XferCode.paused.rawValue, id: 1))
        XCTAssertEqual(held, [true])
        XCTAssertTrue(incoming.take(status: XferCode.resume.rawValue, id: 1))
        XCTAssertEqual(replies.last, status(1, .ready, 2))
        XCTAssertEqual(held, [true, false])

        // Both hold: the peer's resume alone is not answered; this end's own resume then sends both.
        replies = []
        XCTAssertTrue(incoming.pause())
        XCTAssertTrue(incoming.take(status: XferCode.paused.rawValue, id: 1))
        XCTAssertTrue(incoming.take(status: XferCode.resume.rawValue, id: 1))
        XCTAssertEqual(replies, [status(1, .paused, 2)])
        XCTAssertTrue(incoming.resume())
        XCTAssertEqual(Array(replies.suffix(2)), [status(1, .resume, 2), status(1, .ready, 2)])

        // Both hold, this end lets go first: only RESUME goes, and the peer's resume is answered with READY.
        replies = []
        XCTAssertTrue(incoming.pause())
        XCTAssertTrue(incoming.take(status: XferCode.paused.rawValue, id: 1))
        XCTAssertTrue(incoming.resume())
        XCTAssertEqual(replies, [status(1, .paused, 2), status(1, .resume, 2)])
        XCTAssertTrue(incoming.take(status: XferCode.resume.rawValue, id: 1))
        XCTAssertEqual(replies.last, status(1, .ready, 2))
    }

    func testNoIdleDropRunsWhilePausedAndTenMinutesEndsItCancelledWithThePartKept() throws {
        var held: [Bool] = []
        let incoming = receiver()
        incoming.pausedChanged = { held.append($0) }
        _ = incoming.take(begin: 1, size: 9, name: "a.txt")
        _ = incoming.take(chunk: 1, offset: 0, data: [1, 2])
        XCTAssertTrue(incoming.pause())
        receiverClock = 100
        receiverTimers.removeFirst().work()
        XCTAssertTrue(incoming.busy)
        XCTAssertEqual(receiverTimers.map(\.delay), [TransferLimits.idleTimeout])
        receiverClock = TransferLimits.pauseCap + 1
        receiverTimers.removeFirst().work()
        XCTAssertFalse(incoming.busy)
        XCTAssertEqual(replies.last, status(1, .cancelled))
        XCTAssertEqual(held, [true, false])
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath: partial), ["a.txt.9.part"])
    }

    func testTheSenderPausingHoldsTheReceiverToo() throws {
        var held: [Bool] = []
        let incoming = receiver()
        incoming.pausedChanged = { held.append($0) }
        _ = incoming.take(begin: 1, size: 9, name: "a.txt")
        XCTAssertTrue(incoming.take(status: XferCode.paused.rawValue, id: 1))
        // A status for another file is not this transfer's.
        XCTAssertFalse(incoming.take(status: XferCode.paused.rawValue, id: 7))
        receiverClock = 100
        receiverTimers.removeFirst().work()
        XCTAssertTrue(incoming.busy)
        // Ending the session while held lets the owner know.
        incoming.end()
        XCTAssertEqual(held, [true, false])
        XCTAssertFalse(incoming.pause())
    }

    func testABeginForTheSameNameAndSizeReplacesAHeldFileAndResumesFromItsPart() {
        var held: [Bool] = []
        let incoming = receiver()
        incoming.pausedChanged = { held.append($0) }
        _ = incoming.take(begin: 1, size: 6, name: "a.txt")
        _ = incoming.take(chunk: 1, offset: 0, data: [1, 2])
        XCTAssertTrue(incoming.pause())
        XCTAssertTrue(incoming.take(begin: 2, size: 6, name: "a.txt"))
        XCTAssertEqual(replies.last, status(2, .ready, 2))
        XCTAssertEqual(held, [true, false])
        // Another file, or a file not held, is still busy.
        _ = incoming.take(begin: 3, size: 6, name: "b.txt")
        XCTAssertEqual(replies.last, status(3, .busy))
    }

    func testAReceiverHeldOnlyByTheSenderEndsCancelledAtTheCapWithThePartKept() throws {
        let incoming = receiver()
        _ = incoming.take(begin: 1, size: 9, name: "a.txt")
        _ = incoming.take(chunk: 1, offset: 0, data: [1, 2])
        XCTAssertTrue(incoming.take(status: XferCode.paused.rawValue, id: 1))
        receiverClock = TransferLimits.pauseCap + 1
        receiverTimers.removeFirst().work()
        XCTAssertFalse(incoming.busy)
        XCTAssertEqual(replies.last, status(1, .cancelled))
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath: partial), ["a.txt.9.part"])
    }

    func testAPauseDuringTheReadyWaitThatOutlastsTheCapEndsCancelledWithoutTellingThePhone() throws {
        let outgoing = sender()
        outgoing.pausedChanged = { [unowned self] in self.senderHeld.append($0) }
        senderHeld = []
        outgoing.send(path: try file([1, 2, 3])) { [unowned self] in self.results.append($0) }
        XCTAssertTrue(outgoing.pause())
        guard let index = timers.firstIndex(where: { $0.delay == TransferLimits.pauseCap }) else {
            return XCTFail("No cap armed")
        }
        sent = []
        timers.remove(at: index).work()
        XCTAssertEqual(results, [.cancelled])
        XCTAssertTrue(sent.isEmpty)
        XCTAssertEqual(senderHeld, [true, false])
    }

    func testTheSessionEndingWhileTheSenderIsHeldEndsItSessionEndedAndLetsTheOwnerKnow() throws {
        for peer in [false, true] {
            results = []
            senderHeld = []
            let (outgoing, _) = try heldBack()
            if peer {
                XCTAssertTrue(outgoing.take(status: XferCode.paused.rawValue, id: 1, offset: 0))
            } else {
                XCTAssertTrue(outgoing.pause())
            }
            outgoing.end()
            XCTAssertEqual(results, [.sessionEnded])
            XCTAssertEqual(senderHeld, [true, false])
            XCTAssertFalse(outgoing.pause())
        }
    }

    func testADeadLinkWhileTheSenderPausesOrResumesEndsItSessionEnded() throws {
        let (outgoing, _) = try heldBack()
        alive = false
        XCTAssertFalse(outgoing.pause())
        XCTAssertEqual(results, [.sessionEnded])
    }

    func testTheSessionEndingWhileTheReceiverIsHeldKeepsThePart() throws {
        for own in [true, false] {
            var held: [Bool] = []
            let incoming = receiver()
            incoming.pausedChanged = { held.append($0) }
            _ = incoming.take(begin: 1, size: 9, name: "a.txt")
            _ = incoming.take(chunk: 1, offset: 0, data: [1, 2])
            if own {
                XCTAssertTrue(incoming.pause())
            } else {
                XCTAssertTrue(incoming.take(status: XferCode.paused.rawValue, id: 1))
            }
            incoming.end()
            XCTAssertFalse(incoming.busy)
            XCTAssertEqual(held, [true, false])
            XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath: partial), ["a.txt.9.part"])
        }
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
