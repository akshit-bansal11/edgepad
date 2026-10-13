import Foundation

/// What the trust question calls a phone. The Bluetooth name is chosen by the remote device, so it can hold
/// newlines, run to hundreds of characters or read like part of the dialog; it is cleaned and shown quoted, and
/// the address, the one part this Mac vouches for, is always shown beside it. Everything is counted in Unicode
/// scalars, never Characters, because one Character can carry unlimited combining marks; those are dropped too.
public enum PhoneLabel {
    public static let maxNameLength = 40
    /// A Bluetooth name cannot legitimately exceed 248 bytes, so nothing past this is even looked at.
    public static let maxInputScalars = 256

    private static let allowedPunctuation = CharacterSet(charactersIn: "'-_.()")

    /// `"<name>" (<address>)`, or just the address when the phone gave no usable name.
    public static func describe(name: String?, address: String) -> String {
        var kept = String.UnicodeScalarView()
        kept.append(
            contentsOf: (name ?? "").unicodeScalars.prefix(maxInputScalars).filter {
                !CharacterSet.nonBaseCharacters.contains($0)
                    && (CharacterSet.letters.contains($0) || CharacterSet.decimalDigits.contains($0) || $0 == " "
                        || allowedPunctuation.contains($0))
            })
        let collapsed = String(kept).split(separator: " ", omittingEmptySubsequences: true).joined(separator: " ")
        var cut = String.UnicodeScalarView()
        cut.append(contentsOf: collapsed.unicodeScalars.prefix(maxNameLength))
        let clean = String(cut).trimmingCharacters(in: .whitespaces)
        return clean.isEmpty ? address : "\"\(clean)\" (\(address))"
    }
}
