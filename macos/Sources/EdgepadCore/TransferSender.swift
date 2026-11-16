import CryptoKit
import Foundation

/// Sends files to the phone as XFER_BEGIN, XFER_CHUNKs and XFER_END, one at a time, and tells the caller how each
/// ended. The statuses that answer come in through `take`. One per session: a second file waits for the first. The
/// Swift mirror of the shared core's `TransferSender`, on the main thread like everything in the session, so it
/// schedules its ticks where the shared core blocks a thread of its own.
///
/// It starts where the phone says (READY's offset), hashing the bytes before that from disk without sending them.
/// Pacing is `BlobSender`'s: each chunk is its own send, so a PONG gets between any two, a tick's budget by link kind
/// and the smaller busy budget while the phone is sending input, and no chunk goes while the link holds
/// `maxBacklog`. A file sent again after a cut link resumes.
public final class TransferSender {
    /// Where frames go; false when the link is dead. Set by the session once it has a link to write to.
    public var output: ((Frame) -> Bool)?
    /// What the link still holds. Set by the session along with `output`.
    public var backlog: () -> Int = { 0 }

    private static let rehashBytesPerTick = 4 * 1024 * 1024

    private enum Phase {
        case awaitingReady
        case sending
        case awaitingFinal
    }

    private final class Run {
        let id: UInt8
        let size: UInt64
        let file: FileHandle
        let finished: (XferCode) -> Void
        var phase = Phase.awaitingReady
        var hash = SHA256()
        /// Where this run starts sending, and how much of what is before it has been hashed.
        var start: UInt64 = 0
        var hashed: UInt64 = 0
        var at: UInt64 = 0
        /// The last offset a PROGRESS named, to tell a receiver repeating itself (a gap) from one keeping up.
        var lastProgress: UInt64?
        /// Moves on at a rewind, so ticks scheduled before it do not run a second chain of chunks.
        var epoch = 0

        init(id: UInt8, size: UInt64, file: FileHandle, finished: @escaping (XferCode) -> Void) {
            self.id = id
            self.size = size
            self.file = file
            self.finished = finished
        }
    }

    private let link: LinkKind
    private let clock: () -> TimeInterval
    private let schedule: (TimeInterval, @escaping () -> Void) -> Void
    private let lastInput: () -> TimeInterval

    private var ended = false
    private var current: Run?
    private var queue: [(path: String, finished: (XferCode) -> Void)] = []
    private var lastId: UInt8 = 0
    /// Moves on whenever a timeout is armed or disarmed, so a timeout armed for an earlier wait cannot end a later
    /// one.
    private var generation = 0

    public init(
        link: LinkKind,
        clock: @escaping () -> TimeInterval = { ProcessInfo.processInfo.systemUptime },
        schedule: @escaping (TimeInterval, @escaping () -> Void) -> Void = { delay, work in
            DispatchQueue.main.asyncAfter(deadline: .now() + delay, execute: work)
        },
        lastInput: @escaping () -> TimeInterval
    ) {
        self.link = link
        self.clock = clock
        self.schedule = schedule
        self.lastInput = lastInput
    }

    /// Sends the file at `path`. `finished` is told `.saved` when the phone has it whole and checked; any other
    /// code is how it ended: the phone's own answer, `.cancelled` when `cancel` was called, `.timedOut`,
    /// `.sessionEnded`, or `.writeFailed` for a file that cannot be read.
    public func send(path: String, finished: @escaping (XferCode) -> Void) {
        guard !ended else {
            finished(.sessionEnded)
            return
        }
        queue.append((path, finished))
        next()
    }

    /// The phone's answer to the file being sent. False when it is not about that file, or not a status this build
    /// knows.
    public func take(status code: UInt8, id: UInt8, offset: UInt64) -> Bool {
        guard let run = current, run.id == id, let known = XferCode(rawValue: code), code <= XferCode.busy.rawValue
        else { return false }
        switch known {
        case .ready:
            if run.phase == .awaitingReady {
                begin(run, at: offset)
            }
        case .progress:
            // A PROGRESS trailing what was sent is normal, the chunks are still in flight. The same offset twice
            // (the receiver answers every chunk it cannot place), or any short answer to END, means a chunk was
            // lost: resend from where the receiver is.
            let repeated = run.lastProgress == offset
            run.lastProgress = offset
            if run.phase != .awaitingReady, offset < run.at, repeated || run.phase == .awaitingFinal {
                rewind(run, to: offset)
            }
        case .saved, .badHash, .rejected, .writeFailed, .cancelled, .busy, .timedOut, .sessionEnded:
            finish(known)
        }
        return true
    }

    /// Stops what is on its way, telling the phone, and drops what was waiting. False when nothing was on its way.
    @discardableResult
    public func cancel() -> Bool {
        let waiting = queue
        queue.removeAll()
        for job in waiting {
            job.finished(.cancelled)
        }
        guard let run = current else { return false }
        _ = output?(.xferStatus(id: run.id, status: XferCode.cancelled.rawValue, offset: run.at))
        finish(.cancelled)
        return true
    }

    /// The session ended. What is on its way stops where it is, and what was waiting never starts.
    public func end() {
        ended = true
        output = nil
        let waiting = queue
        queue.removeAll()
        finish(.sessionEnded)
        for job in waiting {
            job.finished(.sessionEnded)
        }
    }

    private func next() {
        guard current == nil, !ended, !queue.isEmpty else { return }
        let job = queue.removeFirst()
        guard let file = try? FileHandle(forReadingFrom: URL(fileURLWithPath: job.path)),
            let size = try? file.seekToEnd(), (try? file.seek(toOffset: 0)) != nil
        else {
            job.finished(.writeFailed)
            next()
            return
        }
        guard size <= TransferLimits.maxBytes else {
            try? file.close()
            job.finished(.rejected)
            next()
            return
        }
        lastId = lastId % 255 + 1
        let run = Run(id: lastId, size: size, file: file, finished: job.finished)
        current = run
        let name = URL(fileURLWithPath: job.path).lastPathComponent
        guard emit(.xferBegin(id: run.id, size: size, name: name), run) else { return }
        armTimeout(run)
    }

    private func begin(_ run: Run, at offset: UInt64) {
        generation += 1
        guard offset <= run.size else {
            _ = output?(.xferStatus(id: run.id, status: XferCode.cancelled.rawValue, offset: 0))
            finish(.rejected)
            return
        }
        run.phase = .sending
        run.start = offset
        run.at = offset
        step(run)
    }

    /// Sends again from `offset`: the digest starts over and the bytes before it are hashed from disk, as at a resume.
    private func rewind(_ run: Run, to offset: UInt64) {
        generation += 1
        run.epoch += 1
        guard (try? run.file.seek(toOffset: 0)) != nil else {
            finish(.writeFailed)
            return
        }
        run.hash = SHA256()
        run.hashed = 0
        run.start = offset
        run.at = offset
        run.phase = .sending
        step(run)
    }

    private func later(_ run: Run, _ delay: TimeInterval) {
        let epoch = run.epoch
        schedule(delay) { [weak self] in
            guard run.epoch == epoch else { return }
            self?.step(run)
        }
    }

    /// One turn: the rest of the bytes before the start are hashed a few MiB at a time, then one tick's worth of
    /// chunks, then the next turn; XFER_END at the end.
    private func step(_ run: Run) {
        guard current === run, run.phase == .sending else { return }
        var rehashed = 0
        while run.hashed < run.start {
            let count = Int(min(UInt64(64 * 1024), run.start - run.hashed))
            guard let block = try? run.file.read(upToCount: count), !block.isEmpty else {
                finish(.writeFailed)
                return
            }
            run.hash.update(data: block)
            run.hashed += UInt64(block.count)
            rehashed += block.count
            if rehashed >= TransferSender.rehashBytesPerTick, run.hashed < run.start {
                later(run, 0)
                return
            }
        }
        var sent = 0
        while run.at < run.size {
            if backlog() >= BlobSender.maxBacklog {
                later(run, BlobSender.tick)
                return
            }
            let count = Int(min(UInt64(FrameCodec.maxXferChunkBytes), run.size - run.at))
            guard let block = try? run.file.read(upToCount: count), !block.isEmpty else {
                // The file shrank under the transfer.
                _ = output?(.xferStatus(id: run.id, status: XferCode.cancelled.rawValue, offset: run.at))
                finish(.writeFailed)
                return
            }
            run.hash.update(data: block)
            guard emit(.xferChunk(id: run.id, offset: run.at, data: Array(block)), run) else { return }
            run.at += UInt64(block.count)
            sent += 1
            if sent >= budget(), run.at < run.size {
                later(run, BlobSender.tick)
                return
            }
        }
        guard emit(.xferEnd(id: run.id, sha256: Array(run.hash.finalize())), run) else { return }
        run.phase = .awaitingFinal
        armTimeout(run)
    }

    private func budget() -> Int {
        if clock() - lastInput() < BlobSender.inputQuiet {
            return BlobSender.busyChunksPerTick
        }
        switch link {
        case .bluetooth:
            return BlobSender.bluetoothChunksPerTick
        case .network:
            return BlobSender.networkChunksPerTick
        }
    }

    /// The phone has `answerTimeout` to answer READY, and again to answer END.
    private func armTimeout(_ run: Run) {
        generation += 1
        let armed = generation
        schedule(TransferLimits.answerTimeout) { [weak self] in
            guard let self, self.generation == armed, self.current === run else { return }
            // Tell the phone, or it holds the file and answers BUSY until the link is made again.
            _ = self.output?(.xferStatus(id: run.id, status: XferCode.cancelled.rawValue, offset: run.at))
            self.finish(.timedOut)
        }
    }

    /// False when the link is dead, which ends the transfer.
    private func emit(_ frame: Frame, _ run: Run) -> Bool {
        guard let output, output(frame) else {
            if current === run {
                finish(.sessionEnded)
            }
            return false
        }
        return true
    }

    private func finish(_ code: XferCode) {
        guard let run = current else { return }
        current = nil
        generation += 1
        try? run.file.close()
        run.finished(code)
        next()
    }
}
