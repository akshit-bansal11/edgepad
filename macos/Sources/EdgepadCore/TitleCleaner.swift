import Foundation

/// The track title as the phone shows it: brackets, "feat." tails and a repeated artist are cut. Pure, and called once
/// per track change. The twin of the shared core's `TitleCleaner`; both read protocol/titles.txt, so they cannot drift.
public enum TitleCleaner {
    private static let separators = CharacterSet(charactersIn: " -\u{2013}\u{2014}|:")
    private static let dashes: Set<Character> = ["-", "\u{2013}", "\u{2014}"]

    private static let featured = regex(#"(?<![\p{L}\p{N}])(?:featuring|feat|ft|prod)(?![\p{L}\p{N}])"#)
    private static let joiner = regex(#"(?<![\p{L}\p{N}])(?:with|and|x)(?![\p{L}\p{N}])|[&,]"#)
    private static let nameSplit = regex(#"\s*[,&]\s*|(?<![\p{L}\p{N}])(?:and|x|with)(?![\p{L}\p{N}])"#)
    private static let whitespace = regex(#"\s+"#)

    private static func regex(_ pattern: String) -> NSRegularExpression {
        // A literal pattern above, so this cannot fail at run time.
        guard let compiled = try? NSRegularExpression(pattern: pattern, options: [.caseInsensitive]) else {
            preconditionFailure("TitleCleaner pattern does not compile: \(pattern)")
        }
        return compiled
    }

    /// "Title - Artist", with the title cleaned.
    public static func nowPlaying(title: String, artist: String) -> String {
        artist.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            ? clean(title: title, artist: artist) : clean(title: title, artist: artist) + " - " + artist
    }

    /// `title` without brackets, featured-artist tails and a repeated leading artist. `artist` may be a joined list
    /// "A, B". An empty result returns the original title.
    public static func clean(title: String, artist: String) -> String {
        var text = stripBrackets(title)

        if let feat = featured.firstMatch(in: text, range: NSRange(location: 0, length: text.utf16.count)) {
            text = (text as NSString).substring(to: feat.range.location)
        }

        let whole = name(artist)
        let listed = artist.split(separator: ",", omittingEmptySubsequences: false)
            .map { name(String($0)) }.filter { !$0.isEmpty }
        var names = Set(listed.map { $0.lowercased() })
        for item in listed {
            for part in split(item, by: nameSplit).map(name) where !part.isEmpty {
                names.insert(part.lowercased())
            }
        }

        if !names.isEmpty {
            text = cutArtistTail(text, names)
            text = dropLeadingArtist(text, whole, listed)
        }

        text = whitespace.stringByReplacingMatches(
            in: text, range: NSRange(location: 0, length: text.utf16.count), withTemplate: " ")
        text = text.trimmingCharacters(in: separators)
        return text.isEmpty ? title : text
    }

    private static func stripBrackets(_ text: String) -> String {
        let scalars = Array(text.unicodeScalars)
        var kept = String.UnicodeScalarView()
        var i = 0
        while i < scalars.count {
            let end = isOpener(scalars[i]) ? closingIndex(scalars, i) : -1
            if end < 0 {
                kept.append(scalars[i])
                i += 1
            } else {
                i = end + 1
            }
        }
        return String(kept)
    }

    private static func isOpener(_ c: Unicode.Scalar) -> Bool {
        c == "(" || c == "[" || c == "{"
    }

    /// The index of the bracket closing the one at `start`; -1 when unbalanced or crossed.
    private static func closingIndex(_ text: [Unicode.Scalar], _ start: Int) -> Int {
        var expected: [Unicode.Scalar] = []
        for i in start..<text.count {
            let c = text[i]
            if isOpener(c) {
                expected.append(c == "(" ? ")" : c == "[" ? "]" : "}")
            } else if c == ")" || c == "]" || c == "}" {
                guard expected.popLast() == c else { return -1 }
                if expected.isEmpty { return i }
            }
        }
        return -1
    }

    private static func cutArtistTail(_ text: String, _ names: Set<String>) -> String {
        let ns = text as NSString
        for match in joiner.matches(in: text, range: NSRange(location: 0, length: ns.length)) {
            let rest = ns.substring(from: match.range.location + match.range.length)
            let pieces = split(rest, by: nameSplit).map(name).filter { !$0.isEmpty }
            if !pieces.isEmpty && pieces.allSatisfy({ names.contains($0.lowercased()) }) {
                return ns.substring(to: match.range.location)
            }
        }
        return text
    }

    private static func dropLeadingArtist(_ text: String, _ whole: String, _ listed: [String]) -> String {
        let trimmed = String(text.drop(while: { $0.isWhitespace }))
        for item in [whole] + listed where !item.isEmpty {
            guard let range = trimmed.range(of: item, options: [.caseInsensitive, .anchored]) else { continue }
            let rest = trimmed[range.upperBound...].drop(while: { $0.isWhitespace })
            if let first = rest.first, dashes.contains(first) {
                return String(rest.dropFirst())
            }
        }
        return text
    }

    /// An artist or a name in a tail, without the " - Topic" or "VEVO" a channel adds.
    private static func name(_ name: String) -> String {
        var clean = name.trimmingCharacters(in: .whitespacesAndNewlines)
        for suffix in [" - Topic", "VEVO"] {
            if let range = clean.range(of: suffix, options: [.caseInsensitive, .backwards, .anchored]) {
                clean = String(clean[..<range.lowerBound])
                while let last = clean.last, last.isWhitespace { clean.removeLast() }
            }
        }
        return clean
    }

    /// What lies between the matches of `pattern`, empty pieces included, as .NET `Regex.Split` returns it.
    private static func split(_ text: String, by pattern: NSRegularExpression) -> [String] {
        let ns = text as NSString
        var pieces: [String] = []
        var from = 0
        for match in pattern.matches(in: text, range: NSRange(location: 0, length: ns.length)) {
            pieces.append(ns.substring(with: NSRange(location: from, length: match.range.location - from)))
            from = match.range.location + match.range.length
        }
        pieces.append(ns.substring(from: from))
        return pieces
    }
}
