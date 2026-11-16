import XCTest

@testable import EdgepadCore

final class NowPlayingParseTests: XCTestCase {
    func testMusicPlayingGivesTitleArtistAndLengthButNoPosition() {
        let info: [AnyHashable: Any] = [
            "Player State": "Playing", "Name": "Song", "Artist": "Artist", "Total Time": NSNumber(value: 227_400),
        ]
        let report = NowPlayingParse.report(app: .music, userInfo: info)

        XCTAssertEqual(
            report,
            PlayerReport(
                app: .music, title: "Song", artist: "Artist", playing: true, active: true, durationSeconds: 227,
                positionSeconds: nil))
    }

    func testSpotifyPausedGivesThePositionInSecondsAndTheLengthInMilliseconds() {
        let info: [AnyHashable: Any] = [
            "Player State": "Paused", "Name": "Song", "Artist": "A, B", "Duration": NSNumber(value: 180_000),
            "Playback Position": NSNumber(value: 84.6),
        ]
        let report = NowPlayingParse.report(app: .spotify, userInfo: info)

        XCTAssertEqual(report?.playing, false)
        XCTAssertEqual(report?.active, true)
        XCTAssertEqual(report?.durationSeconds, 180)
        XCTAssertEqual(report?.positionSeconds, 85)
    }

    func testAPositionPastTheLengthIsHeldToIt() {
        let info: [AnyHashable: Any] = [
            "Player State": "Playing", "Duration": NSNumber(value: 10_000), "Playback Position": NSNumber(value: 99),
        ]

        XCTAssertEqual(NowPlayingParse.report(app: .spotify, userInfo: info)?.positionSeconds, 10)
    }

    func testStoppedAndUnknownStatesAreNotActive() {
        XCTAssertEqual(NowPlayingParse.report(app: .music, userInfo: ["Player State": "Stopped"])?.active, false)
        XCTAssertEqual(NowPlayingParse.report(app: .music, userInfo: ["Player State": "Rewinding"])?.active, false)
    }

    func testADictionaryWithoutAStateIsRefused() {
        XCTAssertNil(NowPlayingParse.report(app: .music, userInfo: ["Name": "Song"]))
    }

    func testWrongTypesAndNegativeNumbersAreIgnored() {
        let info: [AnyHashable: Any] = [
            "Player State": "Playing", "Name": 5, "Artist": NSNull(), "Total Time": "long",
            "Playback Position": NSNumber(value: -4),
        ]
        let report = NowPlayingParse.report(app: .spotify, userInfo: info)

        XCTAssertEqual(report?.title, "")
        XCTAssertEqual(report?.artist, "")
        XCTAssertEqual(report?.durationSeconds, 0)
        XCTAssertNil(report?.positionSeconds)
    }

    func testTheScriptsPositionIsReadWithEitherDecimalMark() {
        XCTAssertEqual(NowPlayingParse.position(fromScript: "84.5\n"), 84)
        XCTAssertEqual(NowPlayingParse.position(fromScript: "84,5\n"), 84)
        XCTAssertNil(NowPlayingParse.position(fromScript: "missing value"))
        XCTAssertNil(NowPlayingParse.position(fromScript: "-3"))
    }

    func testEachPlayerNamesItsNotificationAndBundle() {
        XCTAssertEqual(PlayerApp.music.notification, "com.apple.Music.playerInfo")
        XCTAssertEqual(PlayerApp.spotify.notification, "com.spotify.client.PlaybackStateChanged")
        XCTAssertEqual(PlayerApp.spotify.bundleId, "com.spotify.client")
    }
}
