import XCTest

@testable import EdgepadCore

/// Runs protocol/titles.txt, the same table the shared core's suite runs.
final class TitleCleanerTests: XCTestCase {
    func testEveryFixtureLineIsCleanedAsWritten() throws {
        let lines = try Fixtures.lines("titles.txt")
        XCTAssertFalse(lines.isEmpty)
        for line in lines {
            let parts = line.split(separator: "|", omittingEmptySubsequences: false)
                .map { $0.trimmingCharacters(in: .whitespaces) }
            XCTAssertEqual(parts.count, 3, line)
            XCTAssertEqual(TitleCleaner.clean(title: parts[0], artist: parts[1]), parts[2], line)
        }
    }

    func testAnEmptyTitleStaysEmpty() {
        XCTAssertEqual(TitleCleaner.clean(title: "", artist: "Artist"), "")
    }

    func testNowPlayingJoinsTheCleanedTitleAndTheArtist() {
        XCTAssertEqual(TitleCleaner.nowPlaying(title: "Song (Live)", artist: "Artist"), "Song - Artist")
    }

    func testNowPlayingWithoutAnArtistIsTheCleanedTitle() {
        XCTAssertEqual(TitleCleaner.nowPlaying(title: "Song (Live)", artist: " "), "Song")
    }
}
