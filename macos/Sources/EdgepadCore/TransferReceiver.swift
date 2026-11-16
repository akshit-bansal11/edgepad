import CryptoKit
import Foundation

/// Takes one file at a time from the phone: XFER_BEGIN, XFER_CHUNKs and XFER_END in, XFER_STATUS out. Chunks must
/// arrive contiguously; one that does not is answered with the bytes held, and the sender restarts there. The file
/// grows as `name.size.part` in `partialFolder`, hashed as it arrives, and is only renamed into `destination` (never
/// over a file, never opened) when the phone's SHA-256 matches. A session that ends keeps the part, and a begin for
/// the same name and size resumes it; parts older than `TransferLimits.partialKept` go when this is created. The
/// Swift mirror of the shared core's `TransferReceiver`, on the main thread like everything in the session.
///
/// Marking the file (the quarantine attribute) is the app's, in `arrived`.
public final class TransferReceiver {
    /// Where XFER_STATUS goes. Set by the session once it has a link to write to.
    public var reply: (Frame) -> Void = { _ in }

    private final class Active {
        let id: UInt8
        let size: UInt64
        let name: String
        let partPath: String
        let file: FileHandle
        var hash: SHA256
        var held: UInt64
        var reported: UInt64
        var lastActive: TimeInterval
        var closed = false

        init(
            id: UInt8, size: UInt64, name: String, partPath: String, file: FileHandle, hash: SHA256, held: UInt64,
            lastActive: TimeInterval
        ) {
            self.lastActive = lastActive
            self.id = id
            self.size = size
            self.name = name
            self.partPath = partPath
            self.file = file
            self.hash = hash
            self.held = held
            reported = held
        }

        func close() {
            guard !closed else { return }
            closed = true
            try? file.close()
        }
    }

    private let destination: String
    private let partialFolder: String
    private let arrived: ((String) -> Void)?
    private let mark: ((String) -> Void)?
    private let freeSpace: (String) -> Int64
    private let clock: () -> TimeInterval
    private let schedule: (TimeInterval, @escaping () -> Void) -> Void
    private var active: Active?

    /// - Parameters:
    ///   - destination: Where a saved file lands.
    ///   - partialFolder: Where a file still arriving, or cut short, is kept.
    ///   - arrived: Told the final path of each file saved.
    ///   - mark: Told the path of the part of a file that checked out, before it is moved into `destination`, so the
    ///     quarantine attribute is never missing from a file the owner can see.
    ///   - freeSpace: Free bytes on the volume holding a folder; nil reads the real one.
    ///   - clock: Seconds, for the idle timeout.
    ///   - schedule: Runs work after a delay, for the idle timeout.
    ///   - now: For tests, the time parts are aged against.
    public init(
        destination: String,
        partialFolder: String,
        arrived: ((String) -> Void)? = nil,
        mark: ((String) -> Void)? = nil,
        freeSpace: ((String) -> Int64)? = nil,
        clock: @escaping () -> TimeInterval = { ProcessInfo.processInfo.systemUptime },
        schedule: @escaping (TimeInterval, @escaping () -> Void) -> Void = { delay, work in
            DispatchQueue.main.asyncAfter(deadline: .now() + delay, execute: work)
        },
        now: Date = Date()
    ) {
        self.destination = destination
        self.partialFolder = partialFolder
        self.arrived = arrived
        self.mark = mark
        self.clock = clock
        self.schedule = schedule
        self.freeSpace = freeSpace ?? TransferReceiver.freeSpaceOf
        purge(now)
    }

    /// Whether a file is arriving.
    public var busy: Bool { active != nil }

    public func take(begin id: UInt8, size: UInt64, name raw: String) -> Bool {
        guard active == nil else {
            status(id, .busy, 0)
            return true
        }
        guard let name = SafeFileName.clean(raw), size <= TransferLimits.maxBytes else {
            status(id, .rejected, 0)
            return true
        }
        let folders = FileManager.default
        do {
            try folders.createDirectory(atPath: destination, withIntermediateDirectories: true)
            try folders.createDirectory(
                atPath: partialFolder, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
            guard freeSpace(destination) >= Int64(size) + TransferLimits.freeSpaceMargin else {
                status(id, .rejected, 0)
                return true
            }
            try open(id: id, size: size, name: name)
        } catch {
            Log.write("A file from the phone could not be started: \(error.localizedDescription)")
            status(id, .writeFailed, 0)
        }
        return true
    }

    public func take(chunk id: UInt8, offset: UInt64, data: [UInt8]) -> Bool {
        guard let a = active, a.id == id else { return false }
        guard offset == a.held else {
            status(id, .progress, a.held)
            return true
        }
        guard a.held + UInt64(data.count) <= a.size else {
            finish(deletePart: true)
            status(id, .rejected, 0)
            return true
        }
        do {
            try a.file.write(contentsOf: Data(data))
        } catch {
            finish(deletePart: true)
            status(id, .writeFailed, 0)
            return true
        }
        a.hash.update(data: data)
        a.held += UInt64(data.count)
        a.lastActive = clock()
        if a.held - a.reported >= TransferLimits.progressEveryBytes {
            a.reported = a.held
            status(id, .progress, a.held)
        }
        return true
    }

    public func take(end id: UInt8, sha256: [UInt8]) -> Bool {
        guard let a = active, a.id == id else { return false }
        // Short of the size is not a bad file, it is not all of it yet: say where it stands and let the sender go on.
        guard a.held == a.size else {
            status(id, .progress, a.held)
            return true
        }
        guard Array(a.hash.finalize()) == sha256 else {
            finish(deletePart: true)
            status(id, .badHash, 0)
            return true
        }
        save(a)
        return true
    }

    /// The sender's CANCELLED. Any other status is the sender's to receive, not this side's.
    public func take(status code: UInt8, id: UInt8) -> Bool {
        guard code == XferCode.cancelled.rawValue, let a = active, a.id == id else { return false }
        finish(deletePart: true)
        return true
    }

    /// Stops the file arriving, deletes its part and tells the sender. False when none is.
    @discardableResult
    public func cancel() -> Bool {
        guard let a = active else { return false }
        finish(deletePart: true)
        status(a.id, .cancelled, 0)
        return true
    }

    /// The session is over: the part stays, for the next BEGIN of the same name and size.
    public func end() {
        finish(deletePart: false)
    }

    /// Free bytes on the volume holding `folder`; -1 when that cannot be read, which refuses the file.
    static func freeSpaceOf(_ folder: String) -> Int64 {
        let attributes = try? FileManager.default.attributesOfFileSystem(forPath: folder)
        return (attributes?[.systemFreeSize] as? NSNumber)?.int64Value ?? -1
    }

    /// Drops the file when no chunk has come for `idleTimeout`, keeping its part. One timer per file: when it fires
    /// early because chunks came, it sets itself again for what is left.
    private func watchIdle(_ a: Active, after delay: TimeInterval = TransferLimits.idleTimeout) {
        schedule(delay) { [weak self] in
            guard let self, self.active === a else { return }
            let idle = self.clock() - a.lastActive
            if idle >= TransferLimits.idleTimeout {
                Log.write("A file from the phone went quiet and was dropped; its part is kept.")
                self.finish(deletePart: false)
            } else {
                self.watchIdle(a, after: TransferLimits.idleTimeout - idle)
            }
        }
    }

    private func open(id: UInt8, size: UInt64, name: String) throws {
        let partPath = "\(partialFolder)/\(name).\(size)\(TransferLimits.partialExtension)"
        if !FileManager.default.fileExists(atPath: partPath) {
            let made = FileManager.default.createFile(
                atPath: partPath, contents: nil, attributes: [.posixPermissions: 0o600])
            guard made else { throw CocoaError(.fileWriteUnknown) }
        }
        let file = try FileHandle(forUpdating: URL(fileURLWithPath: partPath))
        do {
            var held = try file.seekToEnd()
            if held > size {
                try file.truncate(atOffset: 0)
                held = 0
            }
            // What is held is hashed again from disk: the digest at the far end is of the whole file, not of this run.
            var hash = SHA256()
            try file.seek(toOffset: 0)
            // read(upToCount:) answers nil at the end of the file, and an empty Data is treated the same.
            while let block = try file.read(upToCount: 64 * 1024), !block.isEmpty {
                hash.update(data: block)
            }
            try file.seek(toOffset: held)
            let made = Active(
                id: id, size: size, name: name, partPath: partPath, file: file, hash: hash, held: held,
                lastActive: clock())
            active = made
            watchIdle(made)
            status(id, .ready, held)
        } catch {
            try? file.close()
            active = nil
            throw error
        }
    }

    private func save(_ a: Active) {
        let manager = FileManager.default
        var finalPath: String?
        do {
            try a.file.synchronize()
            a.close()
            mark?(a.partPath)
            while finalPath == nil {
                let name = SafeFileName.unique(a.name) { manager.fileExists(atPath: "\(destination)/\($0)") }
                let candidate = "\(destination)/\(name)"
                // moveItem will not replace: a file that appeared since the check makes it throw, and the loop names
                // around it.
                do {
                    try manager.moveItem(atPath: a.partPath, toPath: candidate)
                    finalPath = candidate
                } catch {
                    guard manager.fileExists(atPath: candidate) else { throw error }
                }
            }
        } catch {
            Log.write("A file from the phone could not be saved: \(error.localizedDescription)")
            finish(deletePart: false)
            status(a.id, .writeFailed, 0)
            return
        }
        finish(deletePart: false)
        status(a.id, .saved, a.size)
        if let finalPath {
            arrived?(finalPath)
        }
    }

    private func finish(deletePart: Bool) {
        guard let a = active else { return }
        active = nil
        a.close()
        if deletePart {
            // A part that will not delete is purged by age.
            try? FileManager.default.removeItem(atPath: a.partPath)
        }
    }

    private func purge(_ now: Date) {
        let manager = FileManager.default
        guard let names = try? manager.contentsOfDirectory(atPath: partialFolder) else { return }
        for name in names where name.hasSuffix(TransferLimits.partialExtension) {
            let path = "\(partialFolder)/\(name)"
            let modified = (try? manager.attributesOfItem(atPath: path))?[.modificationDate] as? Date
            if let modified, now.timeIntervalSince(modified) > TransferLimits.partialKept {
                try? manager.removeItem(atPath: path)
            }
        }
    }

    private func status(_ id: UInt8, _ code: XferCode, _ offset: UInt64) {
        reply(.xferStatus(id: id, status: code.rawValue, offset: offset))
    }
}
