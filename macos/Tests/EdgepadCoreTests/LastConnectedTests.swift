import XCTest

@testable import EdgepadCore

final class LastConnectedTests: XCTestCase {
    private let directory = URL(fileURLWithPath: NSTemporaryDirectory())
        .appendingPathComponent("edgepad-tests-" + UUID().uuidString)

    private var path: String { directory.appendingPathComponent("last-connected.txt").path }

    private var utc: Calendar {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = TimeZone(identifier: "UTC")!
        return calendar
    }

    override func tearDown() {
        try? FileManager.default.removeItem(at: directory)
        super.tearDown()
    }

    func testATimeRoundTripsWhateverTheCaseOfTheAddress() {
        let moment = Date(timeIntervalSince1970: 1_762_560_000)  // 2025-11-08T00:00:00Z
        LastConnected(path: path).record("AA:BB:CC:DD:EE:FF", at: moment)

        XCTAssertEqual(LastConnected(path: path).date(for: "aa:bb:cc:dd:ee:ff"), moment)
    }

    func testAPhoneNeverRecordedAndAMissingFileReadAsUnknown() {
        let store = LastConnected(path: path)
        store.record("AA:BB:CC:DD:EE:FF")

        XCTAssertNil(store.date(for: "11:22:33:44:55:66"))
        XCTAssertNil(LastConnected(path: directory.appendingPathComponent("none.txt").path).date(for: "x"))
    }

    func testForgettingOneKeepsTheOthersAndForgettingAllClearsThem() {
        let store = LastConnected(path: path)
        store.record("AA")
        store.record("BB")

        store.forget("aa")
        XCTAssertNil(store.date(for: "AA"))
        XCTAssertNotNil(store.date(for: "BB"))

        store.forgetAll()
        XCTAssertNil(store.date(for: "BB"))
    }

    /// Reset PIN and Forget go through the trust store's forget, so neither leaves a forgotten phone's date behind.
    func testForgettingTrustedPhonesForgetsWhenTheyLastConnected() {
        let store = LastConnected(path: path)
        let trust = TrustStore(path: directory.appendingPathComponent("trusted-phone.txt").path, connections: store)
        _ = trust.admit("AA", ask: { true })
        _ = trust.admit("BB", ask: { true })
        store.record("AA")
        store.record("BB")

        XCTAssertTrue(trust.forgetOne("aa"))
        XCTAssertNil(store.date(for: "AA"))
        XCTAssertNotNil(store.date(for: "BB"))

        XCTAssertTrue(trust.forget())
        XCTAssertNil(store.date(for: "BB"))
    }

    func testAWriteQueuedBeforeAForgetWritesNothing() {
        let store = LastConnected(path: path)
        let admitted = store.generation
        store.forgetAll()
        store.record("AA", since: admitted)
        XCTAssertNil(store.date(for: "AA"))

        store.record("AA", since: store.generation)
        XCTAssertNotNil(store.date(for: "AA"))
    }

    func testTheRowNamesTheDayAndMonthAndAddsTheYearOnlyWhenNotThisYear() {
        let now = Date(timeIntervalSince1970: 1_762_560_000)  // 2025-11-08
        let thisYear = Date(timeIntervalSince1970: 1_762_560_000 - 86400 * 40)  // 2025-09-29
        let lastYear = Date(timeIntervalSince1970: 1_730_000_000)  // 2024-10-27

        XCTAssertEqual(
            LastConnected.rowText(name: "Pixel", last: thisYear, now: now, calendar: utc),
            "Pixel · last connected 29 Sep")
        XCTAssertEqual(
            LastConnected.rowText(name: "Pixel", last: lastYear, now: now, calendar: utc),
            "Pixel · last connected 27 Oct 2024")
    }

    func testAnUnknownTimeSaysNotConnectedSinceThisUpdate() {
        XCTAssertEqual(
            LastConnected.rowText(name: "Pixel", last: nil), "Pixel · not connected since this update")
    }
}
