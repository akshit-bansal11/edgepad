import Foundation

/// The phones paired over the network link, each with the 32-byte key its pairing left both ends, one per line:
/// `idHex<TAB>base64(key)<TAB>name`, the twin of the C# `PairedLinks`. A phone that resumes proves it holds its key;
/// one not listed has to pair again, which needs the owner at the Mac.
///
/// The file is read the way `TrustStore` reads its own, for the same reason: a file that is there and cannot be read,
/// or holds a line this class would not have written, refuses every phone rather than being taken for an empty list.
/// Forget trusted phones clears it with the trusted phones. Main thread only.
public final class PairedLinks {
    private static let keyLength = 32

    private let path: String

    /// Set when `forget()` could not delete or empty the file: every phone is refused until it can, as `TrustStore`
    /// does.
    private var forgetFailed = false

    /// Moved on by every `forget()`. A pairing reads it when it starts and hands it to `add`, so a Forget chosen while
    /// the phone was pairing is the later word.
    public private(set) var generation = 0

    public init(path: String) {
        self.path = path
    }

    public static func forCurrentUser() -> PairedLinks {
        let support =
            FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first?.path
            ?? NSHomeDirectory() + "/Library/Application Support"
        return PairedLinks(path: support + "/Edgepad/paired-links.txt")
    }

    /// The folder the file is in, for a message that sends the owner there.
    public var folder: String { (path as NSString).deletingLastPathComponent }

    /// The key and name a phone was paired with; nil for a phone not paired, and for every phone while the file
    /// cannot be read.
    func find(_ idHex: String) -> (key: [UInt8], name: String)? {
        guard let links = tryRead() else { return nil }
        return links.first { $0.id.caseInsensitiveCompare(idHex) == .orderedSame }.map { (key: $0.key, name: $0.name) }
    }

    /// Records a phone's pairing, replacing an older one for the same id. False when it could not be written, the file
    /// could not be read (written over, it would lose the phones the read could not see), or the pairings were
    /// forgotten since `generation` was read; the caller tells the last from the others by `generation`.
    func add(_ idHex: String, key: [UInt8], name: String, since generation: Int) -> Bool {
        precondition(key.count == Self.keyLength, "A pairing key is \(Self.keyLength) bytes")
        guard self.generation == generation, var links = tryRead() else { return false }
        links.removeAll { $0.id.caseInsensitiveCompare(idHex) == .orderedSame }
        // The name is the phone's own say, shown only; a tab or line break in it would end its line early.
        let clean = String(name.map { $0.isNewline || $0 == "\t" ? " " : $0 })
        links.append(Link(id: idHex, key: key, name: clean))
        let text = links.map { "\($0.id)\t\(Data($0.key).base64EncodedString())\t\($0.name)\n" }.joined()
        if let failure = writePrivately(text, to: path) {
            Log.write("A paired phone could not be saved to \(path): \(failure)")
            return false
        }
        return true
    }

    /// True when there is something for Forget to clear: a paired phone, or a file that cannot be read.
    public var canForget: Bool {
        tryRead().map { !$0.isEmpty } ?? true
    }

    /// True when every pairing is gone: the file deleted or, failing that, emptied. False when neither worked; every
    /// phone is then refused for this run, as for an unreadable file.
    @discardableResult
    public func forget() -> Bool {
        generation += 1
        do {
            try FileManager.default.removeItem(atPath: path)
        } catch let deleteError {
            if !isAbsent(deleteError, at: path) {
                do {
                    guard let handle = FileHandle(forWritingAtPath: path) else {
                        throw CocoaError(.fileWriteUnknown)
                    }
                    try handle.truncate(atOffset: 0)
                    try handle.close()
                    Log.write(
                        "The paired phones' file could not be deleted at \(path) and was emptied instead: "
                            + deleteError.localizedDescription)
                } catch {
                    Log.write(
                        "The paired phones could not be forgotten at \(path): " + deleteError.localizedDescription)
                    forgetFailed = true
                    return false
                }
            }
        }
        forgetFailed = false
        return true
    }

    /// Nil when the file is there and cannot be read, holds a line this class would not have written, or a Forget
    /// failed; the caller refuses. An absent or blank file is no phone paired.
    private func tryRead() -> [Link]? {
        if forgetFailed { return nil }
        let text: String
        do {
            text = try String(contentsOfFile: path, encoding: .utf8)
        } catch {
            if isAbsent(error, at: path) {
                return []
            }
            Log.write(
                "The paired phones could not be read from \(path), so every network phone is refused: "
                    + error.localizedDescription)
            return nil
        }
        var links: [Link] = []
        for line in text.split(whereSeparator: \.isNewline) {
            guard let link = Link(line: String(line)) else {
                // The line itself is never logged: it holds a key.
                Log.write(
                    "The paired phones in \(path) are not in a form Edgepad writes, so every network phone is refused "
                        + "until they are forgotten")
                return nil
            }
            links.append(link)
        }
        return links
    }

    private struct Link {
        let id: String
        let key: [UInt8]
        let name: String

        init(id: String, key: [UInt8], name: String) {
            self.id = id
            self.key = key
            self.name = name
        }

        init?(line: String) {
            let parts = line.split(separator: "\t", maxSplits: 2, omittingEmptySubsequences: false)
            guard parts.count == 3, parts[0].utf8.count == 32, parts[0].utf8.allSatisfy(Hex.isDigit),
                let key = Data(base64Encoded: String(parts[1])), key.count == PairedLinks.keyLength
            else { return nil }
            self.init(id: String(parts[0]), key: Array(key), name: String(parts[2]))
        }
    }
}
