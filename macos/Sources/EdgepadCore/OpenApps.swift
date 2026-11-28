import Foundation

/// The body of `TextKind.openApps`: one "token TAB name" line per app, the apps open now first and then the apps already
/// added as macros, deduplicated by token and capped. The twin of the shared core's `OpenApps`. Each entry is a TEXT 21
/// payload, "token/name", as `ForegroundApp.encode` writes it, so the token is exactly what the foreground reporter says.
public enum OpenApps {
    public static let maxLines = 40

    /// Entries with no token (an empty payload) are skipped. A name has no tab or line break, which `encode` turned to spaces.
    public static func body(_ entries: [String]) -> String {
        var seen = Set<String>()
        var lines: [String] = []
        for entry in entries where !entry.isEmpty && lines.count < maxLines {
            let token = String(entry.prefix { $0 != "/" })
            guard seen.insert(token).inserted else { continue }
            lines.append(token + "\t" + entry.dropFirst(token.count + 1))
        }
        return lines.joined(separator: "\n")
    }
}
