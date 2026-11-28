import XCTest

@testable import EdgepadCore

final class OpenAppsTests: XCTestCase {
    func testOpenAppsComeFirstThenMacroAppsNotAlreadyListed() {
        let body = OpenApps.body(["com.apple.Notes/Notes", "com.spotify.client/Spotify", "com.spotify.client/Spotify again"])

        XCTAssertEqual(body, "com.apple.Notes\tNotes\ncom.spotify.client\tSpotify")
    }

    func testAnEmptyEntryIsSkippedAndNothingIsEmptyText() {
        XCTAssertEqual(OpenApps.body(["", "a.b/A"]), "a.b\tA")
        XCTAssertEqual(OpenApps.body([]), "")
    }

    func testTheListStopsAtFortyLines() {
        let many = (0..<60).map { "app.n\($0)/App \($0)" }

        XCTAssertEqual(OpenApps.body(many).split(separator: "\n").count, OpenApps.maxLines)
    }

    func testTheOpenAppsAskIsDroppedWithoutAnAnswererAndAnsweredWithOne() {
        let dispatcher = Dispatcher(
            input: FakeInput(),
            speakers: FakeLevel(level: Level(percent: 50, muted: false)),
            microphone: FakeLevel(level: Level(percent: 50, muted: false)),
            brightness: FakeBrightness(level: 50),
            display: FakeDisplay(rates: [60, 120], current: 0)
        )
        dispatcher.handle(.runAction(id: ActionId.openAppsList.rawValue))
        XCTAssertEqual(dispatcher.dropped, 1)

        var asked = 0
        dispatcher.openApps = {
            asked += 1
            return true
        }
        dispatcher.handle(.runAction(id: ActionId.openAppsList.rawValue))

        XCTAssertEqual(asked, 1)
        XCTAssertEqual(dispatcher.dropped, 1)
    }
}
