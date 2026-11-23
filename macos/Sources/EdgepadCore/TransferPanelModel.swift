import Foundation

/// What the Mac's transfer panel shows about the file in flight and the batch it belongs to: pure state, so the panel
/// only draws it. Fed by `TransferSender.changed` / `TransferReceiver.changed` and their `pausedChanged`.
public struct TransferPanelModel: Equatable {
    public private(set) var sending = true
    public private(set) var name = ""
    public private(set) var done: UInt64 = 0
    public private(set) var size: UInt64 = 0
    /// Either end holds the file.
    public private(set) var held = false
    /// A file is in flight now.
    public private(set) var running = false
    /// Which file of the batch this is (1-based) and how many the batch has. A received batch's size is unknown, so
    /// its total is only what has arrived.
    public private(set) var number = 0
    public private(set) var total = 0
    /// Smoothed; 0 until the meter has one, and while held.
    public private(set) var bytesPerSecond: Double = 0

    private var sampleAt: TimeInterval?
    private var sampleDone: UInt64 = 0

    /// A speed needs at least this long a window.
    private static let sampleWindow: TimeInterval = 0.5

    public init() {}

    /// Share of the file moved, 0...1.
    public var fraction: Double {
        size == 0 ? 0 : min(1, Double(done) / Double(size))
    }

    /// `count` more files are on their way to the phone. A batch that is over starts a new one.
    public mutating func expect(files count: Int) {
        if !running, number >= total {
            number = 0
            total = 0
        }
        total += max(0, count)
    }

    /// One tick from the sender or receiver. `active` false is the file ending, however it ended.
    public mutating func update(
        sending isSending: Bool, name fileName: String, done moved: UInt64, size fileSize: UInt64, active: Bool,
        now: TimeInterval
    ) {
        guard active else {
            running = false
            held = false
            bytesPerSecond = 0
            sampleAt = nil
            return
        }
        if !running || fileName != name || isSending != sending {
            if !running, number >= total {
                number = 0
                total = 0
            }
            number += 1
            total = max(total, number)
            bytesPerSecond = 0
            sampleAt = now
            sampleDone = moved
            held = false
        }
        running = true
        sending = isSending
        name = fileName
        done = moved
        size = fileSize
        measure(now)
    }

    /// Either end started or stopped holding the file.
    public mutating func setHeld(_ value: Bool, now: TimeInterval) {
        held = value
        bytesPerSecond = 0
        sampleAt = now
        sampleDone = done
    }

    private mutating func measure(_ now: TimeInterval) {
        guard !held, let at = sampleAt else { return }
        let elapsed = now - at
        guard elapsed >= Self.sampleWindow else { return }
        if done >= sampleDone {
            let instant = Double(done - sampleDone) / elapsed
            bytesPerSecond = bytesPerSecond == 0 ? instant : 0.7 * bytesPerSecond + 0.3 * instant
        }
        sampleAt = now
        sampleDone = done
    }
}

/// Why a file from the phone was not kept, in the owner's terms.
public enum TransferFailure: Equatable {
    case damaged
    case writeFailed
    case rejected
    case cancelled
    case timedOut
    case linkLost

    /// The reason for a receive that ended with `code`; nil for `.saved` and for codes that end nothing.
    public static func receive(_ code: XferCode) -> TransferFailure? {
        switch code {
        case .badHash: return .damaged
        case .writeFailed: return .writeFailed
        case .rejected: return .rejected
        case .cancelled: return .cancelled
        case .timedOut: return .timedOut
        case .sessionEnded: return .linkLost
        case .saved, .ready, .progress, .busy, .paused, .resume: return nil
        }
    }
}
