import XCTest

@testable import EdgepadCore

final class UpdatePolicyTests: XCTestCase {
    private func decide(_ offered: String, _ running: String, skipped: String? = nil, keySet: Bool = true)
        throws -> UpdateDecision
    {
        UpdatePolicy.decide(
            offered: try XCTUnwrap(Version(offered)), running: running, skipped: skipped.flatMap { Version($0) },
            keySet: keySet)
    }

    func testThePolicyDecidesFromTheOfferTheRunningVersionTheSkipAndTheKey() throws {
        XCTAssertEqual(try decide("5.0.1", "5.0.0"), .install)
        XCTAssertEqual(try decide("5.1.0", "5.0.9-draft.3"), .install)
        XCTAssertEqual(try decide("6.0.0", "5.9.9"), .installNeedsMajorConfirm)
        XCTAssertEqual(try decide("5.0.0", "5.0.0"), .notNewer)
        XCTAssertEqual(try decide("4.9.9", "5.0.0"), .notNewer)
        XCTAssertEqual(try decide("5.0.1", "garbage"), .notNewer)
        XCTAssertEqual(try decide("5.0.1", "5.0.0", skipped: "5.0.1"), .skipped)
        XCTAssertEqual(try decide("5.0.2", "5.0.0", skipped: "5.0.1"), .install)
        XCTAssertEqual(try decide("5.0.1", "5.0.0", keySet: false), .noKey)
        XCTAssertEqual(try decide("5.0.0", "5.0.0", keySet: false), .notNewer)
    }

    func testReleaseFilesAreAddressedUnderTheirOwnTag() throws {
        let version = try XCTUnwrap(Version("5.0.1"))
        let base = "https://github.com/akshit-bansal11/edgepad/releases/download/v5.0.1/"

        XCTAssertEqual(UpdateCheck.sumsUrl(version)?.absoluteString, base + "SHA256SUMS.txt")
        XCTAssertEqual(UpdateCheck.sigUrl(version)?.absoluteString, base + "SHA256SUMS.txt.sig")
        XCTAssertEqual(UpdateCheck.macName(version), "Edgepad-5.0.1-mac.zip")
        XCTAssertEqual(UpdateCheck.assetUrl(version, UpdateCheck.macName(version))?.absoluteString, base + "Edgepad-5.0.1-mac.zip")
    }

    func testOnlyHttpsAddressesOnGitHubAreAllowed() throws {
        func allowed(_ text: String) throws -> Bool {
            UpdateDownload.isAllowed(try XCTUnwrap(URL(string: text)))
        }

        XCTAssertTrue(try allowed("https://github.com/a/b"))
        XCTAssertTrue(try allowed("https://objects.githubusercontent.com/x"))
        XCTAssertTrue(try allowed("https://github.com:443/a"))
        XCTAssertFalse(try allowed("http://github.com/a"))
        XCTAssertFalse(try allowed("https://github.com:8443/a"))
        XCTAssertFalse(try allowed("https://user@github.com/a"))
        XCTAssertFalse(try allowed("https://evilgithub.com/a"))
        XCTAssertFalse(try allowed("https://githubusercontent.com.evil.com/a"))
        XCTAssertFalse(try allowed("https://example.com/a"))
    }
}
