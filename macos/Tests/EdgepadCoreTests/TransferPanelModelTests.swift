import XCTest

@testable import EdgepadCore

final class TransferPanelModelTests: XCTestCase {
    func testFractionFollowsBytes() {
        var model = TransferPanelModel()
        model.update(sending: true, name: "a.zip", done: 25, size: 100, active: true, now: 0)
        XCTAssertEqual(model.fraction, 0.25, accuracy: 0.0001)
        XCTAssertTrue(model.running)
        XCTAssertEqual(model.number, 1)
        XCTAssertEqual(model.total, 1)
    }

    func testEmptyFileHasNoFraction() {
        var model = TransferPanelModel()
        model.update(sending: false, name: "e", done: 0, size: 0, active: true, now: 0)
        XCTAssertEqual(model.fraction, 0)
    }

    func testBatchCountsFilesAndStartsOverWhenIdle() {
        var model = TransferPanelModel()
        model.expect(files: 2)
        model.update(sending: true, name: "a", done: 0, size: 10, active: true, now: 0)
        model.update(sending: true, name: "a", done: 10, size: 10, active: false, now: 1)
        model.update(sending: true, name: "b", done: 0, size: 10, active: true, now: 1)
        XCTAssertEqual(model.number, 2)
        XCTAssertEqual(model.total, 2)
        model.update(sending: true, name: "b", done: 10, size: 10, active: false, now: 2)
        model.expect(files: 1)
        XCTAssertEqual(model.number, 0)
        XCTAssertEqual(model.total, 1)
    }

    func testSpeedNeedsAWindowThenSmooths() {
        var model = TransferPanelModel()
        model.update(sending: true, name: "a", done: 0, size: 10_000, active: true, now: 0)
        model.update(sending: true, name: "a", done: 100, size: 10_000, active: true, now: 0.2)
        XCTAssertEqual(model.bytesPerSecond, 0)
        model.update(sending: true, name: "a", done: 1000, size: 10_000, active: true, now: 1)
        XCTAssertEqual(model.bytesPerSecond, 1000, accuracy: 0.001)
        model.update(sending: true, name: "a", done: 3000, size: 10_000, active: true, now: 2)
        XCTAssertEqual(model.bytesPerSecond, 0.7 * 1000 + 0.3 * 2000, accuracy: 0.001)
    }

    func testHoldStopsTheMeterAndEndingClearsIt() {
        var model = TransferPanelModel()
        model.update(sending: true, name: "a", done: 0, size: 10_000, active: true, now: 0)
        model.update(sending: true, name: "a", done: 1000, size: 10_000, active: true, now: 1)
        XCTAssertGreaterThan(model.bytesPerSecond, 0)
        model.setHeld(true, now: 1)
        XCTAssertTrue(model.held)
        XCTAssertEqual(model.bytesPerSecond, 0)
        model.update(sending: true, name: "a", done: 1000, size: 10_000, active: true, now: 5)
        XCTAssertEqual(model.bytesPerSecond, 0)
        model.update(sending: true, name: "a", done: 1000, size: 10_000, active: false, now: 6)
        XCTAssertFalse(model.running)
        XCTAssertFalse(model.held)
    }
}
