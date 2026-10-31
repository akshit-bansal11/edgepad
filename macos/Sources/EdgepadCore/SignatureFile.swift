import Foundation

/// Where a saved signature goes. Foundation only, so a test can drive it; the PNG itself is rendered by the app.
public enum SignatureFile {
    /// Writes `png` as `stem.png` in `folder`, or `stem-2.png`, `stem-3.png` and on when that is taken, and returns
    /// the URL. Never over an existing file: two saves in one second share a stem, and overwriting would lose the
    /// first signature. `.withoutOverwriting` fails when the file exists, so a file that appears between a check
    /// and the write cannot be overwritten either.
    public static func writeNew(folder: URL, stem: String, png: Data) throws -> URL {
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        var number = 1
        while true {
            let name = number == 1 ? "\(stem).png" : "\(stem)-\(number).png"
            let url = folder.appendingPathComponent(name)
            do {
                try png.write(to: url, options: .withoutOverwriting)
                return url
            } catch let error as CocoaError where error.code == .fileWriteFileExists {
                // Taken: try the next number. Any other failure (a full disk) still fails the save.
                number += 1
            }
        }
    }
}
