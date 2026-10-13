import XCTest

@testable import EdgepadCore

final class ScrollAccumulatorTests: XCTestCase {
    func testOneNotchGivesPixelsPerNotch() {
        var scroll = ScrollAccumulator(pixelsPerNotch: 40)
        XCTAssertEqual(scroll.pixels(dx: 0, dy: 120).y, 40)
    }

    func testAFractionBelowOnePixelGivesZero() {
        var scroll = ScrollAccumulator(pixelsPerNotch: 40)
        XCTAssertEqual(scroll.pixels(dx: 0, dy: 1).y, 0)
    }

    func testRepeatedSmallScrollsEventuallyGiveOnePixel() {
        // Half a pixel a unit, which a Double holds exactly: a third of one does not, and the test would then
        // be about rounding in the last place rather than about the carry.
        var scroll = ScrollAccumulator(pixelsPerNotch: 60)
        _ = scroll.pixels(dx: 0, dy: 1)
        XCTAssertEqual(scroll.pixels(dx: 0, dy: 1).y, 1)
    }

    func testNegativeValuesKeepTheirSign() {
        var scroll = ScrollAccumulator(pixelsPerNotch: 40)
        XCTAssertEqual(scroll.pixels(dx: -120, dy: 0).x, -40)
    }

    func testTheAxesAreIndependent() {
        var scroll = ScrollAccumulator(pixelsPerNotch: 40)
        let result = scroll.pixels(dx: 120, dy: -240)
        XCTAssertEqual(result.x, 40)
        XCTAssertEqual(result.y, -80)
    }

    func testAHugeValueDoesNotTrap() {
        var scroll = ScrollAccumulator(pixelsPerNotch: 40)
        XCTAssertEqual(scroll.pixels(dx: 0, dy: Int.max).y, Int32.max)
    }
}
