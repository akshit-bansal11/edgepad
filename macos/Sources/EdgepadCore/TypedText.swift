import Foundation

/// How text from the phone becomes keystrokes. Walked by Character so an emoji or an accented letter goes out whole,
/// but every line ending is a Return: in Swift "\r\n" is one Character, which compared unequal to "\n" and was typed
/// as raw Unicode instead of a Return.
public enum TypedText {
    public enum Stroke: Equatable {
        case delete
        case returnKey
        /// One Character, posted as its UTF-16 units.
        case text(String)
    }

    public static func strokes(_ text: String) -> [Stroke] {
        text.map { character in
            switch character {
            case "\u{8}": return .delete
            case "\n", "\r\n", "\r": return .returnKey
            default: return .text(String(character))
            }
        }
    }
}
