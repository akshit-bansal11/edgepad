import XCTest

@testable import EdgepadCore

final class UpdateCheckTests: XCTestCase {
    private let tag = "https://github.com/akshit-bansal11/edgepad/releases/tag/v"

    func testTheVersionIsReadFromTheTagRedirect() {
        XCTAssertEqual(UpdateCheck.versionIn(tag + "3.2.1"), Version("3.2.1"))
    }

    func testAMissingRedirectIsRefused() {
        XCTAssertNil(UpdateCheck.versionIn(nil))
    }

    func testAnotherRepositorysTagPageIsRefused() {
        XCTAssertNil(UpdateCheck.versionIn("https://github.com/someone-else/edgepad/releases/tag/v9.9.9"))
    }

    func testThisRepositorysReleasesPageWithoutATagIsRefused() {
        XCTAssertNil(UpdateCheck.versionIn("https://github.com/akshit-bansal11/edgepad/releases"))
    }

    func testATagWithTheWrongNumberOfPartsIsRefused() {
        XCTAssertNil(UpdateCheck.versionIn(tag + "3.2"))
        XCTAssertNil(UpdateCheck.versionIn(tag + "3.2.1.4"))
    }

    func testATagWithASuffixOrOddCharactersIsRefused() {
        XCTAssertNil(UpdateCheck.versionIn(tag + "3.2.1-draft.4"))
        XCTAssertNil(UpdateCheck.versionIn(tag + "3.2.+1"))
        XCTAssertNil(UpdateCheck.versionIn(tag + "3.2.1/../../evil"))
    }

    func testAPartLongerThanFourDigitsIsRefused() {
        XCTAssertNil(Version("3.2.12345"))
    }

    func testALaterReleaseIsNewer() throws {
        let candidate = try XCTUnwrap(Version("3.2.1"))

        XCTAssertTrue(UpdateCheck.isNewer(candidate, than: "3.2.0"))
    }

    func testTheMinorPartIsComparedAsANumberNotText() throws {
        let candidate = try XCTUnwrap(Version("3.10.0"))

        XCTAssertTrue(UpdateCheck.isNewer(candidate, than: "3.9.5"))
    }

    func testTheSameReleaseIsNotNewer() throws {
        let candidate = try XCTUnwrap(Version("3.2.0"))

        XCTAssertFalse(UpdateCheck.isNewer(candidate, than: "3.2.0"))
    }

    func testAnEarlierReleaseIsNotNewer() throws {
        let candidate = try XCTUnwrap(Version("3.1.9"))

        XCTAssertFalse(UpdateCheck.isNewer(candidate, than: "3.2.0"))
    }

    func testASuffixOnTheRunningVersionIsIgnored() throws {
        let candidate = try XCTUnwrap(Version("3.3.0"))

        XCTAssertFalse(UpdateCheck.isNewer(candidate, than: "3.3.0-draft.38"))
    }

    func testAnUnreadableRunningVersionIsNeverOlder() throws {
        let candidate = try XCTUnwrap(Version("3.3.0"))

        XCTAssertFalse(UpdateCheck.isNewer(candidate, than: "not a version"))
    }
}
