import AppKit
import EdgepadCore

/// Opens an address a git host's page named in the default browser. Only `GitOpenGate` ever hands it one: an https
/// address on the host's own domain that this Mac fetched itself.
final class MacUrlOpener: UrlOpening {
    func open(_ url: URL) -> Bool {
        guard url.scheme == "https", url.host?.isEmpty == false else { return false }
        DispatchQueue.main.async { _ = NSWorkspace.shared.open(url) }
        return true
    }
}
