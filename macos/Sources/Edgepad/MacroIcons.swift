import AppKit
import EdgepadCore

/// The picture on a macro button, from the same place Finder gets it. The twin of the Windows `MacroIcons`: the
/// override path if the macro has one, else the target, and nothing for a URL.
/// Main thread only: it draws with AppKit.
final class WorkspaceMacroIcons: MacroIconSource {
    /// The requested square, then each smaller tier when it encodes past that side's wire cap (a photograph will), so
    /// one oversized source cannot cost the whole grid.
    func png(for macro: Macro, size: Int) -> [UInt8]? {
        let source = macro.icon.flatMap { $0.isEmpty ? nil : $0 } ?? macro.target
        guard let path = WorkspaceMacroIcons.filePath(source) else {
            return nil
        }
        let image = NSWorkspace.shared.icon(forFile: path)
        for side in MacroIconChunks.fallbackChain(size: size) {
            guard let png = WorkspaceMacroIcons.encode(image, side: side) else { return nil }
            if png.count <= MacroIconChunks.maxBytes(size: side) {
                return png
            }
        }
        Log.write("Macro icon from \(path) is too large even at 64 px and was not sent")
        return nil
    }

    /// An existing path, or nil for a URL or something that is not there: `icon(forFile:)` would hand back a generic
    /// document picture for a missing file, which says nothing about the macro.
    private static func filePath(_ text: String) -> String? {
        if !text.hasPrefix("/"), !text.hasPrefix("~"), URL(string: text)?.scheme != nil {
            return nil
        }
        let path = (text as NSString).expandingTildeInPath
        return FileManager.default.fileExists(atPath: path) ? path : nil
    }

    /// Exactly `side` pixels square whatever the display's scale, which a plain `lockFocus` image would not be on a
    /// Retina Mac.
    private static func encode(_ image: NSImage, side: Int) -> [UInt8]? {
        guard
            let bitmap = NSBitmapImageRep(
                bitmapDataPlanes: nil, pixelsWide: side, pixelsHigh: side, bitsPerSample: 8, samplesPerPixel: 4,
                hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0),
            let context = NSGraphicsContext(bitmapImageRep: bitmap)
        else { return nil }
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = context
        context.imageInterpolation = .high
        let frame = NSRect(x: 0, y: 0, width: side, height: side)
        image.draw(in: frame, from: .zero, operation: .copy, fraction: 1)
        NSGraphicsContext.restoreGraphicsState()
        guard let data = bitmap.representation(using: .png, properties: [:]) else { return nil }
        return [UInt8](data)
    }
}
