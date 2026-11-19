import Foundation

/// The discovery file the hook client reads: `{"v":1,"endpoint","token","pid"}`, 0600 in a 0700 folder. The token is 32
/// random bytes in base64url, made new by every `HookEndpoint` and by `rotate` (an integration was switched on).
/// `dispose` deletes the file, so a client finds nothing to talk to once the app is gone. The twin of the laptop core's
/// HookEndpoint.
public final class HookEndpoint {
    public static let fileName = "hook.json"

    public let file: String
    private let endpoint: String
    private let gate = NSLock()
    private var current: String
    private var disposed = false

    /// Writes `file` naming `endpoint`. Throws `HookSocketError` when it cannot.
    public init(file: String, endpoint: String) throws {
        self.file = file
        self.endpoint = endpoint
        current = Self.newToken()
        if let failure = write(current) {
            throw HookSocketError("hook.json could not be written: " + failure)
        }
    }

    /// The current token. The server reads it on every request, so a rotation takes effect at once.
    public var token: String {
        gate.lock()
        defer { gate.unlock() }
        return current
    }

    /// `~/Library/Application Support/Edgepad/hook.json`, where the hook client looks.
    public static func defaultFile() -> String {
        let support =
            FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first?.path
            ?? NSHomeDirectory() + "/Library/Application Support"
        return support + "/Edgepad/" + fileName
    }

    /// `$TMPDIR/edgepad`: the folder of hook.sock, inside the per-user temporary folder macOS gives every account.
    public static func socketDirectory() -> String {
        (NSTemporaryDirectory() as NSString).appendingPathComponent("edgepad")
    }

    /// A new token, written to the file before it is used. False (logged) when the file could not be written; the old
    /// one then stays.
    @discardableResult
    public func rotate() -> Bool {
        gate.lock()
        defer { gate.unlock() }
        guard !disposed else { return false }
        let next = Self.newToken()
        if let failure = write(next) {
            Log.write("The hook file could not be rewritten: \(failure)")
            return false
        }
        current = next
        return true
    }

    public func dispose() {
        gate.lock()
        defer { gate.unlock() }
        guard !disposed else { return }
        disposed = true
        if unlink(file) != 0 && errno != ENOENT {
            Log.write("The hook file could not be removed")
        }
    }

    /// 32 bytes from the system's cryptographic generator, base64url without padding.
    static func newToken() -> String {
        var generator = SystemRandomNumberGenerator()
        let bytes = (0..<32).map { _ in UInt8.random(in: 0...255, using: &generator) }
        return Data(bytes).base64EncodedString()
            .replacingOccurrences(of: "+", with: "-")
            .replacingOccurrences(of: "/", with: "_")
            .replacingOccurrences(of: "=", with: "")
    }

    private func write(_ token: String) -> String? {
        let object: [String: Any] = ["v": 1, "endpoint": endpoint, "token": token, "pid": Int(getpid())]
        guard let data = try? JSONSerialization.data(withJSONObject: object, options: [.withoutEscapingSlashes]) else {
            return "it could not be encoded"
        }
        return writePrivately(String(decoding: data, as: UTF8.self), to: file)
    }
}
