import XCTest

@testable import EdgepadCore

/// The same table as the shared core's `AppKindsTests`, for this Mac's rows.
final class AppKindsTests: XCTestCase {
    private var sent: [String] = []
    private var kind: ForegroundKind!

    override func setUp() {
        sent = []
        kind = ForegroundKind(
            samePlayer: { $0.caseInsensitiveCompare($1) == .orderedSame },
            send: { [weak self] in self?.sent.append($0) })
    }

    private func rows() throws -> [[String]] {
        try Fixtures.lines("app-kinds.txt").compactMap { line in
            let parts = line.split(whereSeparator: \.isWhitespace).map(String.init)
            return parts.first == "APP_KIND" ? Array(parts.prefix(4)) : nil
        }
    }

    func testTheTableEqualsTheSharedFileForThisMac() throws {
        let all = try rows()
        let tokens = Set(AppKindToken.allCases.map(\.rawValue))
        // Every row of every OS has a good kind, so a typo fails here whichever app it is in.
        for row in all {
            XCTAssertEqual(row.count, 4, row.joined(separator: " "))
            XCTAssertTrue(tokens.contains(row[1]), row.joined(separator: " "))
            XCTAssertTrue(["windows", "linux", "macos"].contains(row[2]), row.joined(separator: " "))
        }

        let file = all.filter { $0[2] == "macos" }.map { "\($0[1]) \($0[3])" }
        let code = AppKinds.table.map { "\($0.kind.rawValue) \($0.token)" }
        XCTAssertEqual(file, code)
    }

    func testATokenIsMatchedIgnoringCase() {
        XCTAssertEqual(AppKinds.of(token: "COM.SPOTIFY.CLIENT"), .media)
        XCTAssertNil(AppKinds.of(token: "com.example.unknown"))
        XCTAssertNil(AppKinds.of(token: ""))
    }

    func testResolveFollowsTheOrderTableThenPlayingThenBrowser() {
        XCTAssertEqual(AppKinds.resolve(tableKind: .timeline, playing: true), "timeline")
        XCTAssertEqual(AppKinds.resolve(tableKind: .media, playing: false), "media")
        XCTAssertEqual(AppKinds.resolve(tableKind: nil, playing: true), "media")
        XCTAssertEqual(AppKinds.resolve(tableKind: .browser, playing: true), "media")
        XCTAssertEqual(AppKinds.resolve(tableKind: .browser, playing: false), "browser")
        XCTAssertEqual(AppKinds.resolve(tableKind: nil, playing: false), "")
    }

    func testAChangeIsSentOnceAndTheSameValueNothing() {
        kind.start()
        kind.setForeground("com.google.Chrome")
        kind.setForeground("com.google.Chrome")
        kind.setPlaying("com.apple.Music")
        kind.setForeground("com.apple.Safari")
        XCTAssertEqual(sent, ["browser"])

        kind.setForeground("com.apple.Music")
        XCTAssertEqual(sent, ["browser", "media"])
    }

    func testStartForgetsWhatWasSentAndNothingIsSentWhileStopped() {
        kind.start()
        kind.setForeground("com.google.Chrome")
        kind.stop()
        kind.setForeground("com.apple.Safari")
        XCTAssertEqual(sent, ["browser"])

        kind.start()
        kind.setForeground("com.google.Chrome")
        XCTAssertEqual(sent, ["browser", "browser"])
    }
}
