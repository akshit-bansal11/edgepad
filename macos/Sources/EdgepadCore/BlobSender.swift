import CryptoKit
import Foundation

/// Sends blobs to the phone as BLOB_BEGIN, BLOB_CHUNKs and BLOB_END, one at a time, because the far end holds one
/// transfer and a second BEGIN would abort the first: a blob sent while another is on its way waits for it. One per
/// session. The Swift mirror of the shared core's `BlobSender`, on the main thread like everything in the session, so
/// it schedules its ticks where the shared core blocks a thread of its own.
///
/// Paced exactly as a capture is (`CaptureToPhone`): each chunk is its own send, so a PONG or a STATE gets between any
/// two, and a tick's budget drops to `busyChunksPerTick` while the phone is sending input.
public final class BlobSender {
    public static let tick = CaptureToPhone.tick
    public static let bluetoothChunksPerTick = CaptureToPhone.chunksPerTick
    public static let networkChunksPerTick = 128
    public static let busyChunksPerTick = CaptureToPhone.busyChunksPerTick
    public static let inputQuiet = CaptureToPhone.inputQuiet

    /// Where frames go; false when the link is dead. Set by the session once it has a link to write to.
    public var output: ((Frame) -> Bool)?

    private struct Job {
        let kind: BlobKind
        let label: String
        let data: [UInt8]
        let abandoned: () -> Bool
        let finished: (Bool) -> Void
    }

    private struct Transfer {
        let id: Int
        let job: Job
        var offset = 0
        var seq: UInt8 = 0
    }

    private let link: LinkKind
    private let clock: () -> TimeInterval
    private let schedule: (TimeInterval, @escaping () -> Void) -> Void
    private let lastInput: () -> TimeInterval

    private var ended = false
    private var current: Transfer?
    private var transfers = 0
    private var queue: [Job] = []

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

    /// Sends one blob. `finished` is told true once BLOB_END went, and false when the link died, the session ended or
    /// `abandoned` said so part way: no BLOB_END goes then, and the next BLOB_BEGIN clears what the far end was
    /// holding.
    public func send(
        _ kind: BlobKind,
        label: String,
        data: [UInt8],
        abandoned: @escaping () -> Bool = { false },
        finished: @escaping (Bool) -> Void = { _ in }
    ) {
        guard !ended else {
            finished(false)
            return
        }
        queue.append(Job(kind: kind, label: label, data: data, abandoned: abandoned, finished: finished))
        next()
    }

    /// The session ended. What is on its way stops where it is, and what was waiting never starts.
    public func end() {
        ended = true
        output = nil
        let waiting = queue
        queue.removeAll()
        stop(false)
        for job in waiting {
            job.finished(false)
        }
    }

    /// Starts the next blob when nothing is on its way.
    private func next() {
        guard current == nil, !ended, !queue.isEmpty else { return }
        let job = queue.removeFirst()
        transfers += 1
        let id = transfers
        current = Transfer(id: id, job: job)
        guard emit(.blobBegin(kind: job.kind.rawValue, size: UInt32(clamping: job.data.count), label: job.label))
        else { return }
        pump(id)
    }

    /// One tick's worth of chunks, then the next tick; BLOB_END at the end.
    private func pump(_ id: Int) {
        var sent = 0
        while let transfer = current, transfer.id == id, transfer.offset < transfer.job.data.count {
            if transfer.job.abandoned() {
                stop(false)
                return
            }
            let data = transfer.job.data
            let end = min(transfer.offset + FrameCodec.maxChunkBytes, data.count)
            current?.offset = end
            current?.seq &+= 1
            guard emit(.blobChunk(seq: transfer.seq, data: Array(data[transfer.offset..<end]))) else { return }
            sent += 1
            if sent >= budget(), end < data.count {
                schedule(BlobSender.tick) { [weak self] in self?.pump(id) }
                return
            }
        }
        guard let transfer = current, transfer.id == id else { return }
        guard emit(.blobEnd(sha256: Array(SHA256.hash(data: transfer.job.data)))) else { return }
        stop(true)
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

    /// False when the link is dead, which ends the transfer.
    private func emit(_ frame: Frame) -> Bool {
        guard let output, output(frame) else {
            stop(false)
            return false
        }
        return true
    }

    /// A dead link can reach here twice, from `emit` and from the session's `end()` it set off; the first one counts.
    private func stop(_ sent: Bool) {
        guard let stopped = current else { return }
        current = nil
        stopped.job.finished(sent)
        next()
    }
}
