import AppKit
import EdgepadCore
import Foundation

/// The owner's switch for the shared clipboard: on unless they turned it off in the menu. Read every time, so turning
/// it off stops every read, send and write at once, whatever the phone thinks.
enum ClipboardSetting {
    private static let key = "clipboardSync"

    static var enabled: Bool {
        get { UserDefaults.standard.object(forKey: key) as? Bool ?? true }
        set { UserDefaults.standard.set(newValue, forKey: key) }
    }
}

/// This Mac's general pasteboard. macOS has no change notification, so the host asks `changed()` on its 0.5 s timer.
/// A read that finds nothing shareable, or is denied, is nil. Main thread only.
final class PasteboardSurface: ClipboardSurface {
    private let board = NSPasteboard.general
    private var seen: Int

    init() {
        seen = board.changeCount
    }

    /// True once per change, other than one this class made itself.
    func changed() -> Bool {
        let now = board.changeCount
        defer { seen = now }
        return now != seen
    }

    func read() -> ClipboardContent? {
        switch PasteboardTypes.pick((board.types ?? []).map(\.rawValue)) {
        case .sensitive, .nothing:
            return nil
        case .text:
            guard let text = board.string(forType: .string), !text.isEmpty else { return nil }
            return ClipboardContent(kind: .clipText, data: Array(text.utf8))
        case .png:
            return board.data(forType: .png).map { ClipboardContent(kind: .clipImage, data: [UInt8]($0)) }
        case .tiff:
            guard let tiff = board.data(forType: .tiff), let image = NSBitmapImageRep(data: tiff),
                UInt64(max(image.pixelsWide, 0)) * UInt64(max(image.pixelsHigh, 0)) <= ClipboardSync.maxImagePixels,
                let png = image.representation(using: .png, properties: [:])
            else { return nil }
            return ClipboardContent(kind: .clipImage, data: [UInt8](png))
        }
    }

    func write(_ content: ClipboardContent) -> Bool {
        board.clearContents()
        let done: Bool
        switch content.kind {
        case .clipText:
            done = String(bytes: content.data, encoding: .utf8).map { board.setString($0, forType: .string) } ?? false
        case .clipImage:
            done = board.setData(Data(content.data), forType: .png)
        case .appList, .appIcon:
            done = false
        }
        seen = board.changeCount
        return done
    }
}
