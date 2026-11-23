import XCTest

@testable import EdgepadCore

final class ForegroundAppTests: XCTestCase {
    func testATokenAndNameAreJoinedWithASlash() {
        XCTAssertEqual(ForegroundApp.encode(token: "com.apple.Safari", name: "Safari"), "com.apple.Safari/Safari")
    }

    func testABadOrMissingTokenIsNothingKnown() {
        XCTAssertEqual(ForegroundApp.encode(token: "", name: "X"), "")
        XCTAssertEqual(ForegroundApp.encode(token: "has space", name: "X"), "")
        XCTAssertEqual(ForegroundApp.encode(token: "a/b", name: "X"), "")
        XCTAssertEqual(ForegroundApp.encode(token: String(repeating: "a", count: 65), name: "X"), "")
        XCTAssertNotEqual(ForegroundApp.encode(token: String(repeating: "a", count: 64), name: "X"), "")
    }

    func testTheNameLosesControlCharactersAndBarsAndIsTrimmed() {
        XCTAssertEqual(ForegroundApp.encode(token: "a", name: "  A|B\tC\n "), "a/A B C")
        XCTAssertEqual(ForegroundApp.encode(token: "a", name: nil), "a/")
    }

    func testTheNameIsCutToFortyCodePointsNeverInsideAPair() {
        let name = String(repeating: "\u{1F600}", count: 41)

        let encoded = ForegroundApp.encode(token: "a", name: name)

        XCTAssertEqual(encoded, "a/" + String(repeating: "\u{1F600}", count: 40))
    }
}
