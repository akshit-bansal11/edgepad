import Foundation

/// `network.txt`: `enabled=0|1` and `port=`, the twin of the C# `NetworkSettings`. Off until the owner turns it on, so
/// a Bluetooth-only Mac never listens on the network. Read strictly, like `security.txt`: a file that cannot be read,
/// or holds anything Edgepad would not have written, leaves the network link off (fail closed) and is never changed
/// here, so the owner can see what is wrong. Bluetooth does not read this file.
public final class NetworkSettings {
    /// Chosen, not checked against any registry.
    public static let defaultPort = 47321

    private let path: String

    public init(path: String) {
        self.path = path
    }

    public static func forCurrentUser() -> NetworkSettings {
        let support =
            FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first?.path
            ?? NSHomeDirectory() + "/Library/Application Support"
        return NetworkSettings(path: support + "/Edgepad/network.txt")
    }

    public func load() -> (enabled: Bool, port: Int) {
        let off = (enabled: false, port: Self.defaultPort)
        let text: String
        do {
            text = try String(contentsOfFile: path, encoding: .utf8)
        } catch {
            if !isAbsent(error, at: path) {
                Log.write(
                    "The network settings could not be read from \(path), so Wi-Fi and USB phones are refused: "
                        + error.localizedDescription)
            }
            return off
        }
        var enabled = false
        var port = Self.defaultPort
        for line in text.split(whereSeparator: \.isNewline) {
            let pair = line.split(separator: "=", maxSplits: 1, omittingEmptySubsequences: false)
                .map { $0.trimmingCharacters(in: .whitespaces) }
            let known: Bool
            switch pair.count == 2 ? pair[0] : "" {
            case "enabled":
                enabled = pair[1] == "1"
                known = pair[1] == "1" || pair[1] == "0"
            case "port":
                let value = pair[1].utf8.allSatisfy { (48...57).contains($0) } ? Int(pair[1]) : nil
                port = value ?? 0
                known = value.map { (1...65535).contains($0) } ?? false
            default:
                known = line.allSatisfy(\.isWhitespace)
            }
            if !known {
                Log.write(
                    "The network settings in \(path) hold something Edgepad did not write, so Wi-Fi and USB phones "
                        + "are refused")
                return off
            }
        }
        return (enabled, port)
    }

    /// Turns the link on or off, keeping the port. False, logged, when the file cannot be written.
    public func setEnabled(_ enabled: Bool) -> Bool {
        if let failure = writePrivately("enabled=\(enabled ? 1 : 0)\nport=\(load().port)\n", to: path) {
            Log.write("The network settings could not be saved to \(path): \(failure)")
            return false
        }
        return true
    }
}
