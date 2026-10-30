import CryptoKit
import Foundation

/// A folder the system's capture tool saves into. `shared` is one other things save into too (the Desktop), where only
/// a file named like a capture is taken.
public struct CaptureFolder: Equatable {
    public let path: String
    public let shared: Bool

    public init(path: String, shared: Bool) {
        self.path = path
        self.shared = shared
    }
}

/// One file as a poll saw it.
public struct FileStamp: Equatable {
    public let path: String
    public let length: Int
    public let written: Date
    public let shared: Bool

    public init(path: String, length: Int, written: Date, shared: Bool = false) {
        self.path = path
        self.length = length
        self.written = written
        self.shared = shared
    }
}

/// Which file, if any, the capture tool just produced. Pure, so every rule is a test, and every rule fails safe: a
/// wrong guess sends someone else's file to the phone and then deletes it. A file there before the capture is never a
/// candidate, nor one of a type the protocol does not carry, nor a hidden or temporary one, and two at once are one
/// too many to choose between. The same rules as the Windows and Linux apps' `CapturePick`.
public enum CapturePick {
    /// How macOS, Windows, GNOME and KDE start the names of their screenshots and recordings.
    static let captureNames = ["screenshot", "screen shot", "screen recording", "screencast"]

    public static func before(_ files: [FileStamp]) -> Set<String> {
        Set(files.map { $0.path.lowercased() })
    }

    /// The one new capture, or nil. `ambiguous` is true when there is more than one, which ends the watch.
    public static func newFile(before: Set<String>, now: [FileStamp]) -> (file: FileStamp?, ambiguous: Bool) {
        var found: FileStamp?
        for file in now where !before.contains(file.path.lowercased()) && candidate(file.path, shared: file.shared) {
            if found != nil {
                return (nil, true)
            }
            found = file
        }
        return (found, false)
    }

    /// The same file, length and write time as the last poll, and not empty.
    public static func unchanged(_ previous: FileStamp?, _ current: FileStamp) -> Bool {
        previous == current && current.length > 0
    }

    public static func candidate(_ path: String, shared: Bool = false) -> Bool {
        let name = (path as NSString).lastPathComponent
        guard let first = name.first, first != ".", first != "~", FileType(path: name) != nil else { return false }
        let lowered = name.lowercased()
        return !shared || captureNames.contains { lowered.hasPrefix($0) }
    }
}

/// Where the system's capture tool saves. Behind a seam so the watch is testable without a Mac's defaults.
public protocol CaptureFolders: AnyObject {
    func folders(for capture: ActionId) -> [CaptureFolder]
}

/// Sends a capture to the phone when the phone asked for captures to be saved there (SET CAPTURE_TARGET 1), and deletes
/// the copy here only once the phone says it saved it. The Swift mirror of the Windows and Linux apps'
/// `CaptureToPhone`, on the main thread like everything in the session: polls and chunk ticks are scheduled, never
/// slept, so a transfer never holds the input up.
///
/// Every doubt keeps the file: no file, two files, a file that vanishes or is too large, an answer other than SAVED
/// or none at all. Chunks go out `chunksPerTick` every `tick`, only `busyChunksPerTick` while the phone has sent input
/// within `inputQuiet`, and none while the link holds `maxBacklog`.
public final class CaptureToPhone {
    public static let poll: TimeInterval = 0.5
    public static let screenshotWindow: TimeInterval = 120
    public static let recordingWindow: TimeInterval = 3600
    public static let cancelledWindow: TimeInterval = 3
    public static let tick: TimeInterval = 0.02
    public static let chunksPerTick = 16
    public static let busyChunksPerTick = 2
    public static let inputQuiet: TimeInterval = 0.25
    /// What a sender of many frames lets the link hold before it waits a `tick`. Half of the 64 KiB past which a
    /// transport calls the link dead, so a slow link is never mistaken for a dead one and a PONG always fits.
    public static let maxBacklog = 32 * 1024
    public static let answerTimeout: TimeInterval = 30
    static let screenshotSettledPolls = 2
    /// Ten seconds of a recording's file not changing at all: macOS writes it whole when the recording stops.
    static let recordingSettledPolls = 20

    /// Where frames go; false when the link is dead.
    public var send: ((Frame) -> Bool)?
    /// What the link still holds. Set by the session along with `send`.
    public var backlog: () -> Int = { 0 }

    private let folders: CaptureFolders
    private let clock: () -> TimeInterval
    private let schedule: (TimeInterval, @escaping () -> Void) -> Void
    private let lastInput: () -> TimeInterval

    private var toPhone = false
    private var ended = false

    /// Bumped by every new watch, so a poll scheduled for an older one finds it gone.
    private var generation = 0
    private var startedAt: TimeInterval = -.infinity
    private var deadline: TimeInterval = 0

    private struct Transfer {
        let path: String
        let handle: FileHandle
        var hasher = SHA256()
        var seq: UInt8 = 0
        var finished = false
        var id: Int
        /// The file as it was when sending began, and the SHA-256 that went out: the file is deleted only while both
        /// still hold.
        let length: Int
        let written: Date?
        var digest: [UInt8] = []
    }

    private var transfer: Transfer?
    private var transfers = 0
    /// Files found while another was on its way; sent after it.
    private var queue: [String] = []

    public init(
        folders: CaptureFolders,
        clock: @escaping () -> TimeInterval = { ProcessInfo.processInfo.systemUptime },
        schedule: @escaping (TimeInterval, @escaping () -> Void) -> Void = { delay, work in
            DispatchQueue.main.asyncAfter(deadline: .now() + delay, execute: work)
        },
        lastInput: @escaping () -> TimeInterval
    ) {
        self.folders = folders
        self.clock = clock
        self.schedule = schedule
        self.lastInput = lastInput
    }

    /// SET CAPTURE_TARGET. False, dropped and counted, for a value that is neither Laptop nor Phone.
    public func setTarget(_ value: Int) -> Bool {
        guard let target = CaptureTarget(rawValue: UInt8(clamping: value)) else {
            return false
        }
        toPhone = target == .phone
        if !toPhone {
            generation += 1
        }
        return true
    }

    /// Presses a capture shortcut through `press`, and when the phone wants captures, watches for the file it makes.
    /// The folders are listed before the press, so a file the press makes can never count as old.
    public func run(_ capture: ActionId, press: () -> Bool) -> Bool {
        guard toPhone, !ended else { return press() }
        let watched = folders.folders(for: capture)
        let before = CapturePick.before(list(watched))
        guard press() else { return false }
        let recording = capture == .captureRecord
        generation += 1
        startedAt = clock()
        deadline = startedAt + (recording ? Self.recordingWindow : Self.screenshotWindow)
        let settled = recording ? Self.recordingSettledPolls : Self.screenshotSettledPolls
        watch(generation, watched, before, last: nil, still: 0, settled: settled)
        return true
    }

    /// The phone left its capture screen: a watch started while the overlay may still be open is cut short; a
    /// recording under way is left alone.
    public func cancel() {
        if clock() - startedAt < CaptureOverlay.pendingSeconds {
            deadline = min(deadline, clock() + Self.cancelledWindow)
        }
    }

    /// The phone's answer to the file on its way. False, dropped and counted, when none is.
    public func take(status: UInt8) -> Bool {
        guard let current = transfer else { return false }
        transfer = nil
        try? current.handle.close()
        // SAVED counts only after FILE_END, and only for the file as it was sent: one that changed since is kept.
        if current.finished, FileStatus(rawValue: status) == .saved, unchangedSince(current) {
            do {
                try FileManager.default.removeItem(atPath: current.path)
                Log.write("Capture: saved on the phone, and deleted here")
            } catch {
                Log.write("Capture: saved on the phone, and could not be deleted here: \(error.localizedDescription)")
            }
        } else {
            Log.write("Capture: the phone answered \(status), so the file stays here")
        }
        next()
        return true
    }

    /// The session ended. Watches and a transfer stop where they are, and the file stays here.
    public func end() {
        ended = true
        generation += 1
        try? transfer?.handle.close()
        transfer = nil
        queue.removeAll()
        send = nil
    }

    private func watch(
        _ id: Int, _ watched: [CaptureFolder], _ before: Set<String>, last: FileStamp?, still: Int, settled: Int
    ) {
        schedule(Self.poll) { [weak self] in
            guard let self, id == self.generation, !self.ended else { return }
            if self.clock() > self.deadline {
                self.status(.notFound)
                return
            }
            let (found, ambiguous) = CapturePick.newFile(before: before, now: self.list(watched))
            if ambiguous {
                Log.write("Capture: more than one new file appeared, so none was sent")
                self.status(.notFound)
                return
            }
            guard let file = found else {
                if last != nil {
                    Log.write("Capture: the new file went away before it settled, so none was sent")
                    self.status(.notFound)
                } else {
                    self.watch(id, watched, before, last: nil, still: 0, settled: settled)
                }
                return
            }
            let count = CapturePick.unchanged(last, file) ? still + 1 : 0
            if count >= settled {
                self.queue.append(file.path)
                self.next()
            } else {
                self.watch(id, watched, before, last: file, still: count, settled: settled)
            }
        }
    }

    /// Starts the next queued file when nothing is on its way.
    private func next() {
        guard transfer == nil, !ended, !queue.isEmpty else { return }
        let path = queue.removeFirst()
        guard let type = FileType(path: path),
            let size = (try? FileManager.default.attributesOfItem(atPath: path))?[.size] as? Int,
            let handle = FileHandle(forReadingAtPath: path)
        else {
            status(.failed)
            next()
            return
        }
        guard size <= type.limit else {
            try? handle.close()
            status(.tooLarge)
            next()
            return
        }
        transfers += 1
        let written = (try? FileManager.default.attributesOfItem(atPath: path))?[.modificationDate] as? Date
        transfer = Transfer(path: path, handle: handle, id: transfers, length: size, written: written)
        let name = (path as NSString).lastPathComponent
        guard emit(.fileBegin(type: type.rawValue, size: UInt32(size), name: name)) else { return }
        let id = transfers
        schedule(Self.tick) { [weak self] in self?.pump(id) }
    }

    /// One tick's worth of chunks, then the next tick; FILE_END and the wait for the answer at the end.
    private func pump(_ id: Int) {
        guard var current = transfer, current.id == id, !current.finished else { return }
        let budget = clock() - lastInput() < Self.inputQuiet ? Self.busyChunksPerTick : Self.chunksPerTick
        for _ in 0..<budget where backlog() < Self.maxBacklog {
            let data = (try? current.handle.read(upToCount: FrameCodec.maxChunkBytes)) ?? Data()
            if data.isEmpty {
                current.finished = true
                transfer = current
                let digest = Array(current.hasher.finalize())
                current.digest = digest
                transfer = current
                guard emit(.fileEnd(sha256: digest)) else { return }
                schedule(Self.answerTimeout) { [weak self] in
                    guard let self, let waiting = self.transfer, waiting.id == id else { return }
                    Log.write("Capture: the phone did not answer, so the file stays here")
                    try? waiting.handle.close()
                    self.transfer = nil
                    self.next()
                }
                return
            }
            current.hasher.update(data: data)
            let bytes = [UInt8](data)
            let seq = current.seq
            current.seq &+= 1
            transfer = current
            guard emit(.fileChunk(seq: seq, data: bytes)) else { return }
        }
        schedule(Self.tick) { [weak self] in self?.pump(id) }
    }

    private func unchangedSince(_ sent: Transfer) -> Bool {
        guard let attributes = try? FileManager.default.attributesOfItem(atPath: sent.path),
            attributes[.size] as? Int == sent.length,
            attributes[.modificationDate] as? Date == sent.written,
            let data = FileManager.default.contents(atPath: sent.path)
        else {
            Log.write("Capture: the file changed after it was sent, so it stays here")
            return false
        }
        guard Array(SHA256.hash(data: data)) == sent.digest else {
            Log.write("Capture: the file changed after it was sent, so it stays here")
            return false
        }
        return true
    }

    private func status(_ status: CaptureStatus) {
        _ = emit(.text(kind: TextKind.captureStatus.rawValue, value: status.rawValue))
    }

    /// False when the link is dead, which ends the transfer and keeps the file.
    private func emit(_ frame: Frame) -> Bool {
        guard let send, send(frame) else {
            try? transfer?.handle.close()
            transfer = nil
            return false
        }
        return true
    }

    /// The top level of each folder, skipping one that is missing or unreadable.
    private func list(_ watched: [CaptureFolder]) -> [FileStamp] {
        let manager = FileManager.default
        var files: [FileStamp] = []
        for folder in watched {
            guard let names = try? manager.contentsOfDirectory(atPath: folder.path) else { continue }
            for name in names {
                let path = (folder.path as NSString).appendingPathComponent(name)
                guard let attributes = try? manager.attributesOfItem(atPath: path),
                    attributes[.type] as? FileAttributeType == .typeRegular,
                    let length = attributes[.size] as? Int,
                    let written = attributes[.modificationDate] as? Date
                else { continue }
                files.append(FileStamp(path: path, length: length, written: written, shared: folder.shared))
            }
        }
        return files
    }
}
