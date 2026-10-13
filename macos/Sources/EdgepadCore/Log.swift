import Foundation

/// Appends lifecycle events to ~/Library/Logs/Edgepad/edgepad.log, the only error channel a menu bar app
/// has. Never called per input frame: file I/O on the input path would cost latency.
public enum Log {
    private static let maxBytes: Int64 = 1024 * 1024
    private static let gate = NSLock()

    private static let directory: String = {
        let library =
            FileManager.default.urls(for: .libraryDirectory, in: .userDomainMask).first?.path
            ?? NSHomeDirectory() + "/Library"
        return library + "/Logs/Edgepad"
    }()

    public static let filePath: String = directory + "/edgepad.log"

    /// Appends a line, or gives up quietly. This must not throw or crash, whatever the disk does: almost
    /// every caller is inside a catch block, often on a background queue, and losing the app and the user's
    /// session to a failed log line would be the error channel costing more than every error it reported.
    /// There is nowhere to report this failure: this is the place errors get reported to.
    public static func write(_ message: String) {
        gate.lock()
        defer { gate.unlock() }
        do {
            try append(message)
        } catch {
            return
        }
    }

    private static func append(_ message: String) throws {
        let manager = FileManager.default
        try manager.createDirectory(atPath: directory, withIntermediateDirectories: true)
        let old = filePath + ".old"
        if let size = (try? manager.attributesOfItem(atPath: filePath))?[.size] as? NSNumber,
            size.int64Value > maxBytes
        {
            try? manager.removeItem(atPath: old)
            try manager.moveItem(atPath: filePath, toPath: old)
        }

        let line = Data("\(ISO8601DateFormatter().string(from: Date())) \(message)\n".utf8)
        if !manager.fileExists(atPath: filePath) {
            guard manager.createFile(atPath: filePath, contents: line) else {
                throw CocoaError(.fileWriteUnknown)
            }
            return
        }
        let handle = try FileHandle(forWritingTo: URL(fileURLWithPath: filePath))
        defer { try? handle.close() }
        try handle.seekToEnd()
        try handle.write(contentsOf: line)
    }
}
