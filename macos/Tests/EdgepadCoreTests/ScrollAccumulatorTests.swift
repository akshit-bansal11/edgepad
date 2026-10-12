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
        var scroll = ScrollAccumulator(pixelsPerNotch: 40)
        _ = scroll.pixels(dx: 0, dy: 2)
        _ = scroll.pixels(dx: 0, dy: 2)
        XCTAssertEqual(scroll.pixels(dx: 0, dy: 2).y, 1)
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
