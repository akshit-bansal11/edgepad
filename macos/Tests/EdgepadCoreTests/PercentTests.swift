import XCTest

@testable import EdgepadCore

final class PercentTests: XCTestCase {
    func testAValueBelowZeroBecomesZero() {
        XCTAssertEqual(Percent.clamp(-5), 0)
    }

    func testAValueAboveTheMaximumBecomesTheMaximum() {
        XCTAssertEqual(Percent.clamp(101), 100)
    }

    func testAValueInsideTheRangeIsUnchanged() {
        XCTAssertEqual(Percent.clamp(42), 42)
    }
}
