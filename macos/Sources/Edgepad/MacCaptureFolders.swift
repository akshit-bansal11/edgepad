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
