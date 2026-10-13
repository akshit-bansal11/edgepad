import Foundation

/// Reads the shared files under protocol/, which the other apps' suites read too.
enum Fixtures {
    static func lines(_ name: String) throws -> [String] {
        let text = try String(contentsOf: find(name), encoding: .utf8)
        return text.split(whereSeparator: \.isNewline)
            .map { $0.trimmingCharacters(in: .whitespaces) }
            .filter { !$0.isEmpty && !$0.hasPrefix("#") }
    }

    private static func find(_ name: String) throws -> URL {
        var directory = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
        while true {
            let candidate = directory.appendingPathComponent("protocol").appendingPathComponent(name)
            if FileManager.default.fileExists(atPath: candidate.path) {
                return candidate
            }
            let parent = directory.deletingLastPathComponent()
            if parent.path == directory.path {
                throw CocoaError(.fileNoSuchFile)
            }
            directory = parent
        }
    }
}
