import XCTest

@testable import EdgepadCore

final class ClickCounterTests: XCTestCase {
    private func makeCounter() -> ClickCounter {
        ClickCounter(interval: 0.5, radius: 4)
    }

    func testTheFirstDownIsOne() {
        var counter = makeCounter()
        XCTAssertEqual(counter.down(button: 0, time: 10, x: 100, y: 100), 1)
    }

    func testASecondDownInTimeAndPlaceIsTwo() {
        var counter = makeCounter()
        _ = counter.down(button: 0, time: 10, x: 100, y: 100)
        XCTAssertEqual(counter.down(button: 0, time: 10.3, x: 102, y: 101), 2)
    }

    func testAThirdDownIsThree() {
        var counter = makeCounter()
        _ = counter.down(button: 0, time: 10, x: 100, y: 100)
        _ = counter.down(button: 0, time: 10.2, x: 100, y: 100)
        XCTAssertEqual(counter.down(button: 0, time: 10.4, x: 100, y: 100), 3)
    }

    func testADownAfterTheIntervalRestarts() {
        var counter = makeCounter()
        _ = counter.down(button: 0, time: 10, x: 100, y: 100)
        XCTAssertEqual(counter.down(button: 0, time: 10.6, x: 100, y: 100), 1)
    }

    func testADownBeyondTheRadiusRestarts() {
        var counter = makeCounter()
        _ = counter.down(button: 0, time: 10, x: 100, y: 100)
        XCTAssertEqual(counter.down(button: 0, time: 10.1, x: 110, y: 100), 1)
    }

    func testADifferentButtonRestarts() {
        var counter = makeCounter()
        _ = counter.down(button: 0, time: 10, x: 100, y: 100)
        XCTAssertEqual(counter.down(button: 1, time: 10.1, x: 100, y: 100), 1)
    }

    func testTheCountBeforeAnyDownIsOne() {
        XCTAssertEqual(makeCounter().count, 1)
    }
}
