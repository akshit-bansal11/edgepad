import Foundation

/// Turns a name a phone sent into a file name this Mac will create, or refuses it. The pairs in
/// protocol/filenames.txt pin it, and every app's sanitiser reads the same file. Works on Unicode scalars, as the
/// shared core works on UTF-16 units, so a combining mark after a dot cannot hide the dot.
public enum SafeFileName {
    private static let invalid = Set("<>:\"/\\|?*".unicodeScalars)
    private static let reserved: Set<String> = [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ]

    /// The last path segment, with control characters dropped and `< > : " / \ | ? *` replaced by an underscore,
    /// trailing dots and spaces trimmed, cut to `TransferLimits.maxNameBytes` UTF-8 bytes keeping the extension.
    /// Nil when nothing usable is left, or when the name is a Windows device name with or without an extension.
    public static func clean(_ raw: String?) -> String? {
        guard let raw, !raw.isEmpty else { return nil }
        var scalars = Array(raw.unicodeScalars)
        if let cut = scalars.lastIndex(where: { $0 == "/" || $0 == "\\" }) {
            scalars = Array(scalars[(cut + 1)...])
        }
        var kept: [Unicode.Scalar] = []
        for scalar in scalars where !dropped(scalar) {
            kept.append(invalid.contains(scalar) ? "_" : scalar)
        }
        let name = trimEnd(cutToBytes(trimEnd(kept)))
        guard !name.isEmpty else { return nil }
        let stem = trimEnd(Array(name[..<(name.firstIndex(of: ".") ?? name.endIndex)]), dots: false)
        return reserved.contains(String(String.UnicodeScalarView(stem)).uppercased()) ? nil : string(name)
    }

    /// `name` if nothing by that name exists, otherwise `name (1).ext`, `name (2).ext` and so on: a transfer never
    /// overwrites.
    public static func unique(_ name: String, exists: (String) -> Bool) -> String {
        if !exists(name) {
            return name
        }
        let scalars = Array(name.unicodeScalars)
        let dot = scalars.lastIndex(of: ".")
        let split = dot.flatMap { $0 > 0 ? $0 : nil } ?? scalars.count
        let stem = string(Array(scalars[..<split]))
        let ext = string(Array(scalars[split...]))
        var n = 1
        while true {
            let candidate = "\(stem) (\(n))\(ext)"
            if !exists(candidate) {
                return candidate
            }
            n += 1
        }
    }

    /// Control (Cc) and format (Cf) characters. The latter include the bidi overrides that make `fdp.exe` read as
    /// `exe.pdf`.
    private static func dropped(_ scalar: Unicode.Scalar) -> Bool {
        switch scalar.properties.generalCategory {
        case .control, .format:
            return true
        default:
            return false
        }
    }

    private static func string(_ scalars: [Unicode.Scalar]) -> String {
        String(String.UnicodeScalarView(scalars))
    }

    private static func trimEnd(_ scalars: [Unicode.Scalar], dots: Bool = true) -> [Unicode.Scalar] {
        var result = scalars
        while let last = result.last, last == " " || (dots && last == ".") {
            result.removeLast()
        }
        return result
    }

    private static func bytes(_ scalars: [Unicode.Scalar]) -> Int {
        scalars.reduce(0) { $0 + UTF8.width($1) }
    }

    private static func cutToBytes(_ name: [Unicode.Scalar]) -> [Unicode.Scalar] {
        if bytes(name) <= TransferLimits.maxNameBytes {
            return name
        }
        var ext: [Unicode.Scalar] = []
        if let dot = name.lastIndex(of: "."), dot > 0 {
            ext = Array(name[dot...])
        }
        if bytes(ext) > TransferLimits.maxExtensionBytes {
            ext = []
        }
        var budget = TransferLimits.maxNameBytes - bytes(ext)
        var kept: [Unicode.Scalar] = []
        for scalar in name[..<(name.count - ext.count)] {
            budget -= UTF8.width(scalar)
            if budget < 0 {
                break
            }
            kept.append(scalar)
        }
        return trimEnd(kept) + ext
    }
}
