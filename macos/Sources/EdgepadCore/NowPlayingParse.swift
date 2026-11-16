import Foundation

/// A player this Mac can follow through the notifications it posts, with no polling.
public enum PlayerApp: String, CaseIterable {
    case music = "Music"
    case spotify = "Spotify"

    /// The distributed notification the player posts on every change.
    public var notification: String {
        switch self {
        case .music: return "com.apple.Music.playerInfo"
        case .spotify: return "com.spotify.client.PlaybackStateChanged"
        }
    }

    public var bundleId: String {
        switch self {
        case .music: return "com.apple.Music"
        case .spotify: return "com.spotify.client"
        }
    }
}

/// What one notification said. `positionSeconds` is nil when the player does not put it there (Music).
public struct PlayerReport: Equatable {
    public let app: PlayerApp
    public let title: String
    public let artist: String
    public let playing: Bool
    /// False for "Stopped" and for any state this build does not know: nothing is on.
    public let active: Bool
    public let durationSeconds: Int
    public let positionSeconds: Int?
}

/// Pure reading of the notifications' userInfo, so it is tested with sample dictionaries.
public enum NowPlayingParse {
    public static func report(app: PlayerApp, userInfo: [AnyHashable: Any]) -> PlayerReport? {
        guard let state = userInfo["Player State"] as? String else { return nil }
        let playing = state == "Playing"
        let durationKey = app == .music ? "Total Time" : "Duration"
        let duration = seconds(userInfo[durationKey], milliseconds: true) ?? 0
        let position = app == .spotify ? seconds(userInfo["Playback Position"], milliseconds: false) : nil
        return PlayerReport(
            app: app,
            title: userInfo["Name"] as? String ?? "",
            artist: userInfo["Artist"] as? String ?? "",
            playing: playing,
            active: playing || state == "Paused",
            durationSeconds: duration,
            positionSeconds: position.map { duration > 0 ? min($0, duration) : $0 })
    }

    private static func seconds(_ value: Any?, milliseconds: Bool) -> Int? {
        guard let number = value as? NSNumber else { return nil }
        let raw = number.doubleValue
        guard raw.isFinite, raw >= 0, raw < 1e9 else { return nil }
        return Int((milliseconds ? raw / 1000 : raw).rounded())
    }

    /// The seconds in what `osascript` printed for `player position`; a comma may stand for the point.
    public static func position(fromScript output: String) -> Int? {
        let text = output.trimmingCharacters(in: .whitespacesAndNewlines).replacingOccurrences(of: ",", with: ".")
        guard let value = Double(text), value.isFinite, value >= 0, value < 1e9 else { return nil }
        return Int(value)
    }
}
