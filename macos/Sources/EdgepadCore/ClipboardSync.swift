import CryptoKit
import Foundation

/// The shared clipboard's rules, the same on every laptop: nothing moves unless the phone asked on this link and the
/// owner's own toggle is on, sensitive content never leaves, and a copy that came from the phone does not go back. One
/// per session, so what the phone asked for is forgotten with it. The Swift mirror of the shared core's
/// `ClipboardSync`.
///
/// A change here waits `debounce` for the app that made it to finish, then goes through the paced sender. A blob from
/// the phone is checked as it arrives and written on a later turn of the main queue, so frames behind it are not held
/// up by the pasteboard. Main thread only, like everything in the session.
public final class ClipboardSync {
    public static let maxTextBytes = 1024 * 1024
    public static let maxImageBytes = 4 * 1024 * 1024
    /// A small PNG can inflate to any size, and each platform decodes it to write it. 8K is 33 million.
    public static let maxImagePixels: UInt64 = 50_000_000
    public static let debounce: TimeInterval = 0.15
    /// How long after a write the clipboard is not read for sending. The hash cannot catch every echo of our own write:
    /// a platform that hands an image back re-encoded has different bytes.
    public static let writeQuiet: TimeInterval = 0.5

    private static let pngSignature: [UInt8] = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]
    /// IHDR's length and name, which every PNG has straight after the signature.
    private static let pngHeaderStart: [UInt8] = [0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52]
    /// The signature, IHDR's length and name, then its width and height.
    private static let pngHeaderBytes = pngSignature.count + pngHeaderStart.count + 2 * MemoryLayout<UInt32>.size

    /// The session's sender. The session gives it the link, and ends it.
    public let sender: BlobSender

    private let surface: ClipboardSurface
    private let allowed: () -> Bool
    private let clock: () -> TimeInterval
    private let schedule: (TimeInterval, @escaping () -> Void) -> Void
    private lazy var receiver = BlobReceiver(cap: ClipboardSync.cap) { [weak self] kind, data, sha256 in
        self?.received(kind, data, sha256) ?? false
    }

    private var shared = false
    private var ended = false
    private var changes = 0
    /// The SHA-256 of what both ends last agreed on, so neither sends it to the other again.
    private var known: [UInt8]?
    /// The latest blob waiting to be written. A newer one replaces it.
    private var incoming: ClipboardContent?
    private var quietUntil: TimeInterval = -.infinity

    /// - Parameters:
    ///   - surface: This Mac's pasteboard.
    ///   - sender: The session's sender.
    ///   - allowed: The owner's own toggle, read every time, since it can change during a session.
    ///   - clock: Seconds, for tests.
    ///   - schedule: Runs work on a later turn after a delay, for tests.
    public init(
        surface: ClipboardSurface,
        sender: BlobSender,
        allowed: @escaping () -> Bool,
        clock: @escaping () -> TimeInterval = { ProcessInfo.processInfo.systemUptime },
        schedule: @escaping (TimeInterval, @escaping () -> Void) -> Void = { delay, work in
            DispatchQueue.main.asyncAfter(deadline: .now() + delay, execute: work)
        }
    ) {
        self.surface = surface
        self.sender = sender
        self.allowed = allowed
        self.clock = clock
        self.schedule = schedule
    }

    /// Whether the owner's toggle is on now; the session names `clipboard` in FEATURES only then.
    public var isAllowed: Bool { allowed() }

    private var active: Bool { shared && !ended && allowed() }

    /// SET CLIPBOARD. False, dropped and counted, for a value that is neither 0 nor 1, and for a 1 while the owner's
    /// toggle is off. Turning it on sends nothing: only the next copy goes, so a laptop just selected learns nothing
    /// old.
    public func setShared(_ value: Int) -> Bool {
        guard value == 0 || (value == 1 && allowed()) else { return false }
        shared = value == 1
        if !shared {
            receiver.reset()
            incoming = nil
            known = nil
        }
        return true
    }

    /// The platform saw this clipboard change. Returns at once.
    public func localChanged() {
        guard active else { return }
        changes += 1
        let change = changes
        schedule(ClipboardSync.debounce) { [weak self] in self?.flush(change) }
    }

    public func take(kind: UInt8, size: UInt32, label: String) -> Bool {
        active ? receiver.take(kind: kind, size: size, label: label) : refuse()
    }

    public func take(seq: UInt8, data: [UInt8]) -> Bool {
        active ? receiver.take(seq: seq, data: data) : refuse()
    }

    public func take(sha256: [UInt8]) -> Bool {
        active ? receiver.take(sha256: sha256) : refuse()
    }

    /// The session ended. Nothing moves after this, and a transfer on its way stops where it is.
    public func end() {
        ended = true
        receiver.reset()
        incoming = nil
        sender.end()
    }

    private static func cap(_ kind: UInt8) -> Int? {
        guard let known = BlobKind(rawValue: kind) else { return nil }
        switch known {
        case .clipText:
            return maxTextBytes
        case .clipImage:
            return maxImageBytes
        case .appList, .appIcon:
            return nil
        }
    }

    private static func valid(_ kind: BlobKind, _ data: [UInt8]) -> Bool {
        guard !data.isEmpty else { return false }
        switch kind {
        case .clipText:
            return String(bytes: data, encoding: .utf8) != nil
        case .clipImage:
            return isPng(data)
        case .appList, .appIcon:
            return false
        }
    }

    /// A PNG whose header is one this Mac will decode. The rest of the file is the platform decoder's to refuse.
    private static func isPng(_ data: [UInt8]) -> Bool {
        let start = pngSignature + pngHeaderStart
        guard data.count >= pngHeaderBytes, data.starts(with: start) else { return false }
        // Two u32s multiplied cannot overflow a u64.
        let pixels = bigEndian(data, at: start.count) * bigEndian(data, at: start.count + MemoryLayout<UInt32>.size)
        return pixels > 0 && pixels <= maxImagePixels
    }

    private static func bigEndian(_ data: [UInt8], at offset: Int) -> UInt64 {
        data[offset..<(offset + MemoryLayout<UInt32>.size)].reduce(0) { ($0 << 8) | UInt64($1) }
    }

    private func refuse() -> Bool {
        receiver.reset()
        return false
    }

    private func received(_ kind: UInt8, _ data: [UInt8], _ sha256: [UInt8]) -> Bool {
        guard let blob = BlobKind(rawValue: kind), ClipboardSync.valid(blob, data) else { return false }
        known = sha256
        incoming = ClipboardContent(kind: blob, data: data)
        schedule(0) { [weak self] in self?.apply() }
        return true
    }

    private func apply() {
        let content = incoming
        incoming = nil
        guard let content, active else { return }
        guard surface.write(content) else {
            Log.write("Clipboard: this Mac would not take what the phone sent")
            return
        }
        quietUntil = clock() + ClipboardSync.writeQuiet
    }

    private func flush(_ change: Int) {
        guard change == changes, active, clock() >= quietUntil else { return }
        guard let content = surface.read(), !content.sensitive, !content.data.isEmpty else { return }
        guard let cap = ClipboardSync.cap(content.kind.rawValue), content.data.count <= cap else {
            Log.write("Clipboard: \(content.data.count) bytes of \(content.kind) is over the cap, so it stays here")
            return
        }
        let hash = Array(SHA256.hash(data: content.data))
        guard hash != known else { return }
        known = hash

        let abandoned: () -> Bool = { [weak self] in !(self?.active ?? false) }
        let finished: (Bool) -> Void = { [weak self] sent in
            if !sent {
                self?.forget(hash)
            }
        }
        sender.send(content.kind, label: "", data: content.data, abandoned: abandoned, finished: finished)
    }

    /// The phone never got this, so the same content copied again must go.
    private func forget(_ hash: [UInt8]) {
        if known == hash {
            known = nil
        }
    }
}
