import AppKit
import EdgepadCore

/// The picture BLOB 4 carries for the app playing: its own icon, 64 px, from the same place Finder gets it. The
/// laptop never fetches a web image, and browsers are not followed on the Mac, so only a known player has one. Main
/// thread only: it draws with AppKit.
enum MediaIcon {
    static let side = 64

    static func png(forApp app: String) -> [UInt8]? {
        guard let player = PlayerApp(rawValue: app),
            let url = NSWorkspace.shared.urlForApplication(withBundleIdentifier: player.bundleId)
        else { return nil }
        return WorkspaceMacroIcons.encode(NSWorkspace.shared.icon(forFile: url.path), side: side)
    }
}
