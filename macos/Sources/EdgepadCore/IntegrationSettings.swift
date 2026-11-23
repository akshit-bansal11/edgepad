import Foundation

/// `integrations.txt`: which dashboard integrations are on and what Edgepad remembers about them, one `key=value` per
/// line. Everything is off until the owner turns it on: a file that is missing, unreadable, too large or not written by
/// Edgepad reads as all off, and only the value `1` is "on". No secret is ever stored here (the GitHub token lives in
/// the Keychain). A change takes effect at once whatever the disk says; the return value says only whether it will
/// survive a restart. Keys this version does not know are kept when the file is written, so a newer build's settings
/// are not lost. The twin of the laptop core's IntegrationSettings.
public final class IntegrationSettings {
    public static let fileName = "integrations.txt"
    public static let maxCityLength = 60

    public static let stats = "stats"
    public static let weather = "weather"
    public static let claude = "claude"
    public static let codex = "codex"
    public static let gitHub = "github"
    public static let city = "city"
    public static let lat = "lat"
    public static let lon = "lon"
    public static let place = "place"
    public static let gitHubLogin = "githubLogin"
    public static let codexTrusted = "codexTrusted"
    public static let developer = "developer"

    private static let maxFileBytes = 64 * 1024
    private static let maxValueLength = 300
    private static let maxKeyLength = 40

    private let path: String
    private let gate = NSLock()
    private var keys: [String] = []
    private var values: [String: String] = [:]

    public init(path: String) {
        self.path = path
        load()
    }

    public static func pluginVersionKey(_ agent: String) -> String {
        "pluginVersion." + agent
    }

    public static func pluginNodeKey(_ agent: String) -> String {
        "pluginNode." + agent
    }

    /// The app executable last stamped as `__APP__` for the agent.
    public static func pluginAppKey(_ agent: String) -> String {
        "pluginApp." + agent
    }

    /// True only when the stored value is exactly `1`.
    public func flag(_ key: String) -> Bool {
        gate.lock()
        defer { gate.unlock() }
        return values[key] == "1"
    }

    /// The keys that need developer mode: every AI tool and git host of `DevProviders`.
    public static let developerKeys: Set<String> = Set(DevProviders.all.map(\.id))

    /// Whether `key` is on and in force: every developer key (`developerKeys`) also needs developer mode; every other
    /// key is its flag.
    public func active(_ key: String) -> Bool {
        flag(key) && (!Self.developerKeys.contains(key) || flag(Self.developer))
    }

    /// The stored text, or empty.
    public func text(_ key: String) -> String {
        gate.lock()
        defer { gate.unlock() }
        return values[key] ?? ""
    }

    @discardableResult
    public func setFlag(_ key: String, _ on: Bool) -> Bool {
        set(key, on ? "1" : "0")
    }

    /// Stores `value` with control characters turned to spaces, trimmed and cut (the city to 60 code points, anything
    /// else to 300). An empty value removes the key. A key that is not letters, digits, dot, dash or underscore is
    /// refused.
    @discardableResult
    public func set(_ key: String, _ value: String) -> Bool {
        guard Self.validKey(key) else { return false }
        let clean = Self.cut(Self.flatten(value), key == Self.city ? Self.maxCityLength : Self.maxValueLength)
        gate.lock()
        defer { gate.unlock() }
        if clean.isEmpty {
            values[key] = nil
            keys.removeAll { $0 == key }
        } else {
            if values[key] == nil { keys.append(key) }
            values[key] = clean
        }
        // Written under the lock, so two changes at once cannot land on disk out of order.
        let text = keys.compactMap { k in values[k].map { "\(k)=\($0)\n" } }.joined()
        if let failure = writePrivately(text, to: path) {
            Log.write("The integration settings could not be saved: \(failure)")
            return false
        }
        return true
    }

    static func validKey(_ key: String) -> Bool {
        (1...maxKeyLength).contains(key.unicodeScalars.count)
            && key.unicodeScalars.allSatisfy { scalar in
                ("a"..."z").contains(scalar) || ("A"..."Z").contains(scalar) || ("0"..."9").contains(scalar)
                    || scalar == "." || scalar == "-" || scalar == "_"
            }
    }

    private static func flatten(_ value: String) -> String {
        var flat = String.UnicodeScalarView()
        for scalar in value.unicodeScalars {
            flat.append(scalar.properties.generalCategory == .control ? " " : scalar)
        }
        return String(flat).trimmingCharacters(in: .whitespacesAndNewlines)
    }

    private static func cut(_ text: String, _ max: Int) -> String {
        var kept = String.UnicodeScalarView(text.unicodeScalars.prefix(max))
        while let last = kept.last, last.properties.isWhitespace {
            kept.removeLast()
        }
        return String(kept)
    }

    private func load() {
        let manager = FileManager.default
        guard manager.fileExists(atPath: path) else { return }
        guard let size = (try? manager.attributesOfItem(atPath: path))?[.size] as? NSNumber,
            size.intValue <= Self.maxFileBytes
        else {
            Log.write("The integration settings file is too large or unreadable, so every integration is off.")
            return
        }
        guard let data = manager.contents(atPath: path), let text = String(data: data, encoding: .utf8) else {
            Log.write("The integration settings could not be read, so every integration is off.")
            return
        }
        for raw in text.unicodeScalars.split(separator: "\n", omittingEmptySubsequences: false) {
            let line = String(String.UnicodeScalarView(raw)).trimmingCharacters(in: .whitespacesAndNewlines)
            guard let at = line.firstIndex(of: "="), at != line.startIndex, !line.hasPrefix("#") else { continue }
            let key = line[..<at].trimmingCharacters(in: .whitespacesAndNewlines)
            let value = line[line.index(after: at)...].trimmingCharacters(in: .whitespacesAndNewlines)
            if Self.validKey(key), !value.isEmpty, value.utf16.count <= Self.maxValueLength {
                if values[key] == nil { keys.append(key) }
                values[key] = value
            }
        }
    }
}
