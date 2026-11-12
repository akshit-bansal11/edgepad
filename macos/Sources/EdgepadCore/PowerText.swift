import Foundation

/// One battery the phone is told about. A device with no level is never one of these: the source leaves it out.
public struct BatteryEntry: Equatable {
    public let kind: BatteryKind
    public let level: Int
    public let charging: Bool
    public let name: String

    public init(kind: BatteryKind, level: Int, charging: Bool, name: String) {
        self.kind = kind
        self.level = level
        self.charging = charging
        self.name = name
    }
}

/// TEXT 19 and TEXT 20, as docs/PROTOCOL.md describes them.
public enum PowerText {
    /// A snapshot is held to this many UTF-8 bytes, the most a TEXT frame carries.
    static let maxBytes = 255
    static let maxBatteryName = 24
    static let maxOutputName = 40

    /// Control characters and "|" become a space, then trim, then cut to `limit` code points (so never inside a
    /// surrogate pair). A name that ends up empty is the kind token.
    static func clean(_ name: String, limit: Int, fallback: String) -> String {
        let spaced = String(
            String.UnicodeScalarView(
                name.unicodeScalars.map {
                    $0.properties.generalCategory == .control || $0 == "|" ? " " : $0
                }))
        let trimmed = spaced.trimmingCharacters(in: .whitespacesAndNewlines)
        let cut = String(String.UnicodeScalarView(trimmed.unicodeScalars.prefix(limit)))
        return cut.isEmpty ? fallback : cut
    }

    /// The whole list as one string. Entries are dropped from the end, never cut, until it fits; the first stays.
    public static func encodeBatteries(_ entries: [BatteryEntry]) -> String {
        var parts = entries.map { entry -> String in
            let level = min(max(entry.level, 0), 100)
            let name = clean(entry.name, limit: maxBatteryName, fallback: entry.kind.rawValue)
            return "\(entry.kind.rawValue):\(level):\(entry.charging ? 1 : 0):\(name)"
        }
        while parts.count > 1, parts.joined(separator: "|").utf8.count > maxBytes {
            parts.removeLast()
        }
        return parts.joined(separator: "|")
    }

    /// "kind/name"; nothing known is the empty string.
    public static func encodeOutput(_ output: (OutputKind, String)?) -> String {
        guard let (kind, name) = output else { return "" }
        return "\(kind.rawValue)/\(clean(name, limit: maxOutputName, fallback: kind.rawValue))"
    }
}
