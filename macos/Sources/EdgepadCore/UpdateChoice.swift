import Foundation

/// `update-choice.txt`: what the owner said to an offered update. Lines `skip=<version>`, `later=<unix seconds>`
/// and `notified=<version>`. Read once and kept; an unreadable or garbled file reads as "nothing chosen", so a
/// damaged file never hides an update for good.
public final class UpdateChoice {
    private static let laterSeconds: Int64 = 24 * 60 * 60

    private let path: String
    private let gate = NSLock()
    private var skipped: Version?
    private var laterAt: Int64 = 0
    private var notified: Version?

    public init(path: String) {
        self.path = path
        load()
    }

    /// False when `version` was skipped, or "later" was said within a day of `now`. A "later" further in the
    /// future than that is ignored, so a damaged file cannot hide updates for good.
    public func shouldShow(_ version: Version, now: Date) -> Bool {
        gate.lock()
        defer { gate.unlock() }
        let at = Int64(now.timeIntervalSince1970)
        let waiting = laterAt > at - Self.laterSeconds && laterAt <= at + Self.laterSeconds
        return skipped != version && !waiting
    }

    @discardableResult
    public func skip(_ version: Version) -> Bool {
        gate.lock()
        defer { gate.unlock() }
        skipped = version
        return save()
    }

    @discardableResult
    public func later(now: Date) -> Bool {
        gate.lock()
        defer { gate.unlock() }
        laterAt = Int64(now.timeIntervalSince1970)
        return save()
    }

    @discardableResult
    public func markNotified(_ version: Version) -> Bool {
        gate.lock()
        defer { gate.unlock() }
        notified = version
        return save()
    }

    public func wasNotified(_ version: Version) -> Bool {
        gate.lock()
        defer { gate.unlock() }
        return notified == version
    }

    private func load() {
        guard let text = try? String(contentsOfFile: path, encoding: .utf8) else {
            return
        }
        for line in text.split(whereSeparator: \.isNewline) {
            guard let split = line.firstIndex(of: "=") else {
                continue
            }
            let key = line[..<split].trimmingCharacters(in: .whitespaces)
            let value = line[line.index(after: split)...].trimmingCharacters(in: .whitespaces)
            switch key {
            case "skip": skipped = Version(value) ?? skipped
            case "notified": notified = Version(value) ?? notified
            case "later" where !value.isEmpty && value.utf8.allSatisfy({ $0 >= 48 && $0 <= 57 }):
                laterAt = Int64(value) ?? laterAt
            default: break
            }
        }
    }

    private func save() -> Bool {
        var lines: [String] = []
        if let skipped {
            lines.append("skip=\(skipped)")
        }
        if laterAt > 0 {
            lines.append("later=\(laterAt)")
        }
        if let notified {
            lines.append("notified=\(notified)")
        }
        if let reason = writePrivately(lines.map { $0 + "\n" }.joined(), to: path) {
            Log.write("The update choice could not be saved to \(path): \(reason)")
            return false
        }
        return true
    }
}
