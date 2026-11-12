import Foundation

/// When each trusted phone last connected, one `address<TAB>ISO-8601 UTC` line each, in a file beside the trust file.
/// A file of its own rather than a field on the trust file's lines: an older build reads those lines as addresses, and
/// would stop recognising every phone. An older build never opens this one. Addresses are kept lowercased, since macOS
/// and the phone may write them differently.
/// Sendable by hand: `path` never changes and every file access is under `gate`.
public final class LastConnected: @unchecked Sendable {
    private let path: String
    private let gate = NSLock()

    public init(path: String) {
        self.path = path
    }

    public static func forCurrentUser() -> LastConnected {
        let support =
            FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first?.path
            ?? NSHomeDirectory() + "/Library/Application Support"
        return LastConnected(path: support + "/Edgepad/last-connected.txt")
    }

    /// Writes `now` for `address`. A failed write is logged and changes nothing else: it never blocks a connection.
    public func record(_ address: String, at now: Date = Date()) {
        locked {
            // An unreadable file is left as it is: writing what could not be read would wipe every other phone's time.
            guard var all = read() else { return }
            all[address.lowercased()] = now
            write(all)
        }
    }

    /// When `address` last connected, or nil when that is not known (never recorded, or an unreadable file).
    public func date(for address: String) -> Date? {
        snapshot()(address)
    }

    /// Every time, read once, for a list that shows many phones: one file read per list, not per row.
    public func snapshot() -> (String) -> Date? {
        let all = locked { read() ?? [:] }
        return { all[$0.lowercased()] }
    }

    public func forget(_ address: String) {
        locked {
            guard var all = read(), all.removeValue(forKey: address.lowercased()) != nil else { return }
            write(all)
        }
    }

    public func forgetAll() {
        locked {
            do {
                try FileManager.default.removeItem(atPath: path)
            } catch {
                if !isAbsent(error, at: path) {
                    Log.write(
                        "The last-connected times could not be removed at \(path): "
                            + error.localizedDescription)
                }
            }
        }
    }

    /// "NAME · last connected 8 Nov", with the year when it is not this year's; "NAME · not connected since this
    /// update" when the time is not known. The local calendar, in English as the rest of the menu.
    public static func rowText(
        name: String, last: Date?, now: Date = Date(), calendar: Calendar = .current
    ) -> String {
        guard let last else { return "\(name) · not connected since this update" }
        let sameYear = calendar.component(.year, from: last) == calendar.component(.year, from: now)
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.calendar = calendar
        formatter.timeZone = calendar.timeZone
        formatter.dateFormat = sameYear ? "d MMM" : "d MMM yyyy"
        return "\(name) · last connected \(formatter.string(from: last))"
    }

    /// Every stored time; empty when there is no file yet, nil when the file is there but cannot be read.
    private func read() -> [String: Date]? {
        let text: String
        do {
            text = try String(contentsOfFile: path, encoding: .utf8)
        } catch {
            if isAbsent(error, at: path) {
                return [:]
            }
            Log.write("The last-connected times could not be read from \(path): \(error.localizedDescription)")
            return nil
        }
        let parser = ISO8601DateFormatter()
        var all: [String: Date] = [:]
        for line in text.split(whereSeparator: \.isNewline) {
            let parts = line.split(separator: "\t", omittingEmptySubsequences: false)
            if parts.count == 2, let date = parser.date(from: String(parts[1])) {
                all[String(parts[0])] = date
            }
        }
        return all
    }

    private func write(_ all: [String: Date]) {
        let formatter = ISO8601DateFormatter()
        let text = all.sorted { $0.key < $1.key }
            .map { "\($0.key)\t\(formatter.string(from: $0.value))\n" }.joined()
        if let failure = writePrivately(text, to: path) {
            Log.write("The last-connected times could not be saved to \(path): \(failure)")
        }
    }

    private func locked<T>(_ body: () -> T) -> T {
        gate.lock()
        defer { gate.unlock() }
        return body()
    }
}
