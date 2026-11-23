import AppKit
import CoreServices
import EdgepadCore
import Foundation

/// Where macOS's screenshot tool saves, screenshots and recordings alike: the folder `defaults read
/// com.apple.screencapture location` names, else the Desktop. Read again for every capture, so a change made in the
/// Screenshot app's Options menu is followed. Treated as shared, since the Desktop is everyone's: only a file named like
/// a capture is taken there.
final class MacCaptureFolders: CaptureFolders {
    func folders(for capture: ActionId) -> [CaptureFolder] {
        let chosen = UserDefaults(suiteName: "com.apple.screencapture")?.string(forKey: "location")
        let desktop = (NSHomeDirectory() as NSString).appendingPathComponent("Desktop")
        let folder = chosen.map { ($0 as NSString).expandingTildeInPath } ?? desktop
        return [CaptureFolder(path: folder, shared: true)]
    }

    private let surface = PasteboardSurface()

    /// The pasteboard's change count, only while the owner shares the clipboard: with sharing off the pasteboard is
    /// never touched on a capture's account.
    func clipboardStamp() -> Int? {
        ClipboardSetting.enabled ? NSPasteboard.general.changeCount : nil
    }

    /// The picture on the pasteboard as PNG, read through the clipboard sync's own rules so a password manager's
    /// sensitive flag still keeps it out. Nil when sharing is off or there is no picture.
    func clipboardPNG() -> Data? {
        guard ClipboardSetting.enabled, let content = surface.read(), content.kind == .clipImage,
            let png = surface.encode(content)
        else { return nil }
        return Data(png.data)
    }

    /// Spotlight's kMDItemIsScreenCapture, which macOS sets on its screenshots and recordings whatever the language
    /// names them in. False for a file Spotlight has not indexed yet, or when Spotlight is off: the English names
    /// still count then.
    func isScreenCapture(_ path: String) -> Bool {
        guard let item = MDItemCreate(kCFAllocatorDefault, path as CFString),
            let value = MDItemCopyAttribute(item, "kMDItemIsScreenCapture" as CFString)
        else { return false }
        return (value as? Bool) ?? false
    }
}
