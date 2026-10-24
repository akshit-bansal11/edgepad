import AppKit
import EdgepadCore

/// Opens a macro's target through `NSWorkspace`. Never a shell: a target is opened as a file, a folder, a URL or an
/// application bundle, so nothing in it can be read as a command line. The target is whatever the owner typed into
/// the list on this Mac; the phone only ever names a slot, and the store turns that into a target before getting here.
/// Main thread only, like the rest of the AppKit calls.
final class WorkspaceMacroLauncher: MacroLauncher {
    func launch(target: String, arguments: String?) -> Bool {
        let workspace = NSWorkspace.shared
        if let url = WorkspaceMacroLauncher.webURL(target) {
            return workspace.open(url)
        }
        let path = (target as NSString).expandingTildeInPath
        var isFolder: ObjCBool = false
        guard FileManager.default.fileExists(atPath: path, isDirectory: &isFolder) else {
            return false
        }
        let url = URL(fileURLWithPath: path)
        guard isFolder.boolValue, url.pathExtension.lowercased() == "app" else {
            // Arguments only mean something to a program; a document or folder opens in its own app without them.
            return workspace.open(url)
        }
        let configuration = NSWorkspace.OpenConfiguration()
        // Split on whitespace and no further: with no shell there is no quoting to honour, and a word is passed to
        // the program as one argument exactly as written.
        configuration.arguments = (arguments ?? "").split(whereSeparator: { $0.isWhitespace }).map(String.init)
        workspace.openApplication(at: url, configuration: configuration) { _, error in
            // The bundle exists, so true was already answered; a refusal after that (a damaged app, a
            // Gatekeeper block) can only be logged.
            if let error {
                Log.write("Macro target \(path) did not start: \(error.localizedDescription)")
            }
        }
        return true
    }

    /// The target as a URL when it has a scheme, such as https: or mailto:. A path never does: it starts with a slash
    /// or a tilde, and a relative one has no scheme to find.
    private static func webURL(_ target: String) -> URL? {
        guard !target.hasPrefix("/"), !target.hasPrefix("~"),
            let url = URL(string: target), let scheme = url.scheme, !scheme.isEmpty
        else { return nil }
        return url
    }
}
