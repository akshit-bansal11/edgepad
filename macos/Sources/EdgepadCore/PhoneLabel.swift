import Foundation

/// What the trust question calls a phone. The Bluetooth name is chosen by the remote device, so it can hold
/// newlines, run to hundreds of characters or read like part of the dialog; it is cleaned and shown quoted, and
/// the address, the one part this Mac vouches for, is always shown beside it.
public enum PhoneLabel {
    public static let maxNameLength = 40

    private static let allowedPunctuation = CharacterSet(charactersIn: "'-_.()")

    /// `"<name>" (<address>)`, or just the address when the phone gave no usable name.
    public static func describe(name: String?, address: String) -> String {
        let kept = String(
            String.UnicodeScalarView(
                (name ?? "").unicodeScalars.filter {
                    CharacterSet.letters.contains($0) || CharacterSet.decimalDigits.contains($0) || $0 == " "
                        || allowedPunctuation.contains($0)
                }))
        let collapsed = kept.split(separator: " ", omittingEmptySubsequences: true).joined(separator: " ")
        let clean = String(collapsed.prefix(maxNameLength)).trimmingCharacters(in: .whitespaces)
        return clean.isEmpty ? address : "\"\(clean)\" (\(address))"
    }
}
