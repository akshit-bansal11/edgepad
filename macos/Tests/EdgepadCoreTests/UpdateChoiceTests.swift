import XCTest

@testable import EdgepadCore

final class UpdateChoiceTests: XCTestCase {
    private var directory = ""
    private var path: String { directory + "/update-choice.txt" }
    private let now = Date(timeIntervalSince1970: 1_800_000_000)

    override func setUpWithError() throws {
        directory = NSTemporaryDirectory() + "edgepad-choice-" + UUID().uuidString
        try FileManager.default.createDirectory(atPath: directory, withIntermediateDirectories: true)
    }

    override func tearDown() {
        try? FileManager.default.removeItem(atPath: directory)
    }

    private func v(_ text: String) throws -> Version {
        try XCTUnwrap(Version(text))
    }

    func testWithNoFileEveryUpdateIsShown() throws {
        XCTAssertTrue(UpdateChoice(path: path).shouldShow(try v("9.9.9"), now: now))
    }

    func testASkippedVersionStaysSkippedAfterAReloadButANewerOneIsShown() throws {
        XCTAssertTrue(UpdateChoice(path: path).skip(try v("5.0.1")))

        let reloaded = UpdateChoice(path: path)

        XCTAssertFalse(reloaded.shouldShow(try v("5.0.1"), now: now))
        XCTAssertTrue(reloaded.shouldShow(try v("5.0.2"), now: now))
    }

    func testLaterHidesTheOfferForADayOnly() throws {
        let choice = UpdateChoice(path: path)
        XCTAssertTrue(choice.later(now: now))

        XCTAssertFalse(choice.shouldShow(try v("5.0.1"), now: now.addingTimeInterval(3600)))
        XCTAssertTrue(choice.shouldShow(try v("5.0.1"), now: now.addingTimeInterval(24 * 3600 + 1)))
    }

    func testALaterFarInTheFutureDoesNotHideTheOffer() throws {
        try "later=\(Int64(now.timeIntervalSince1970) + 10 * 24 * 3600)\n".write(
            toFile: path, atomically: true, encoding: .utf8)

        XCTAssertTrue(UpdateChoice(path: path).shouldShow(try v("5.0.1"), now: now))
    }

    func testANotifiedVersionIsRemembered() throws {
        XCTAssertTrue(UpdateChoice(path: path).markNotified(try v("5.0.1")))

        let reloaded = UpdateChoice(path: path)

        XCTAssertTrue(reloaded.wasNotified(try v("5.0.1")))
        XCTAssertFalse(reloaded.wasNotified(try v("5.0.2")))
    }

    func testAGarbledFileReadsAsNothingChosen() throws {
        try "skip=banana\nlater=-5\nlater=+7\nnotified=1.2\nno equals here\n=\n".write(
            toFile: path, atomically: true, encoding: .utf8)

        let choice = UpdateChoice(path: path)

        XCTAssertTrue(choice.shouldShow(try v("5.0.1"), now: now))
        XCTAssertFalse(choice.wasNotified(try v("1.2.0")))
    }

    func testAFileThatCannotBeReadReadsAsNothingChosen() throws {
        try FileManager.default.createDirectory(atPath: path, withIntermediateDirectories: true)

        XCTAssertTrue(UpdateChoice(path: path).shouldShow(try v("5.0.1"), now: now))
    }
}
