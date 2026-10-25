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
}
