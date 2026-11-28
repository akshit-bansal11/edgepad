import Foundation

/// Which streaming service a browser tab or a desktop app is, for the media card's logo. The same rows, in the same
/// order, as `protocol/media-services.txt`, which a test compares: the first row that matches wins. Only the service's
/// name ever leaves this Mac, never a title or an address.
public enum MediaServices {
    static let table: [(name: String, matches: [String])] = [
        ("YouTube Music", ["title:YouTube Music", "host:music.youtube.com"]),
        ("Apple Music", ["title:Apple Music", "host:music.apple.com", "app:applemusic", "app:itunes"]),
        ("Apple TV+", ["title:Apple TV+"]),
        ("Apple TV", ["title:Apple TV", "host:tv.apple.com", "app:appletv"]),
        ("Prime Video", ["title:Prime Video", "host:primevideo.com", "app:primevideo", "app:amazonvideo"]),
        ("HBO Max", ["title:HBO Max", "host:max.com", "host:hbomax.com"]),
        ("Paramount+", ["title:Paramount+", "host:paramountplus.com"]),
        ("Disney+", ["title:Disney+", "host:disneyplus.com", "app:disney"]),
        ("Netflix", ["title:Netflix", "host:netflix.com", "app:netflix"]),
        ("YouTube", ["title:YouTube", "host:youtube.com", "host:youtu.be"]),
        ("Spotify", ["title:Spotify", "host:spotify.com", "app:spotify"]),
        ("Hulu", ["title:Hulu", "host:hulu.com", "app:hulu"]),
        ("Crunchyroll", ["title:Crunchyroll", "host:crunchyroll.com", "app:crunchyroll"]),
        ("Peacock", ["title:Peacock", "host:peacocktv.com"]),
        ("Instagram", ["title:Instagram", "host:instagram.com", "app:instagram"]),
        ("Facebook", ["title:Facebook", "host:facebook.com", "app:facebook"]),
        ("LinkedIn", ["title:LinkedIn", "host:linkedin.com"]),
        ("X", ["title:Twitter", "host:x.com", "host:twitter.com"]),
        ("Sky", ["title:Sky Go", "title:NOW TV", "host:sky.com"]),
        ("SoundCloud", ["title:SoundCloud", "host:soundcloud.com"]),
        ("Twitch", ["title:Twitch", "host:twitch.tv"]),
        ("Plex", ["title:Plex", "host:plex.tv", "app:plex"]),
        ("JioHotstar", ["title:JioHotstar", "title:Hotstar", "host:hotstar.com"]),
        ("JioCinema", ["title:JioCinema", "host:jiocinema.com"]),
        ("SonyLIV", ["title:SonyLIV", "host:sonyliv.com"]),
    ]

    /// The service a tab is, from its page address's host and its title; nil for none.
    public static func name(host: String = "", title: String = "") -> String? {
        let host = host.lowercased()
        return table.first { row in
            row.matches.contains { match in
                if match.hasPrefix("host:") {
                    let wanted = String(match.dropFirst(5))
                    return host == wanted || host.hasSuffix("." + wanted)
                }
                return match.hasPrefix("title:") && hasWord(String(match.dropFirst(6)), in: title)
            }
        }?.name
    }

    /// The service a desktop app is, from its bundle id ("com.apple.Music" is "comapplemusic" here, so a token such as
    /// `applemusic` holds in it); nil for none.
    public static func name(bundleId: String) -> String? {
        let id = bundleId.lowercased().filter { $0 != "." && $0 != "-" }
        return table.first { row in
            row.matches.contains { $0.hasPrefix("app:") && id.contains($0.dropFirst(4)) }
        }?.name
    }

    /// True when `word` stands in `text` with nothing that is a letter or digit right before or after it.
    private static func hasWord(_ word: String, in text: String) -> Bool {
        var from = text.startIndex
        while let found = text.range(of: word, options: .caseInsensitive, range: from..<text.endIndex) {
            let before = found.lowerBound > text.startIndex ? text[text.index(before: found.lowerBound)] : nil
            let after = found.upperBound < text.endIndex ? text[found.upperBound] : nil
            if !(before?.isLetterOrDigit ?? false) && !(after?.isLetterOrDigit ?? false) { return true }
            from = found.upperBound
        }
        return false
    }
}

extension Character {
    fileprivate var isLetterOrDigit: Bool { isLetter || isNumber }
}
