import XCTest

@testable import EdgepadCore

final class KeyMapTests: XCTestCase {
    func testLetterAMapsToMacA() {
        XCTAssertEqual(KeyMap.target(forWindows: 0x41), .key(MacKey(0x00)))
    }

    func testLetterZMapsToMacZ() {
        XCTAssertEqual(KeyMap.target(forWindows: 0x5A), .key(MacKey(0x06)))
    }

    func testDigitZeroMapsToMacZero() {
        XCTAssertEqual(KeyMap.target(forWindows: 0x30), .key(.zero))
    }

    func testDigitNineMapsToMacNine() {
        XCTAssertEqual(KeyMap.target(forWindows: 0x39), .key(MacKey(0x19)))
    }

    func testWinIsCommand() {
        XCTAssertEqual(KeyMap.target(forWindows: 0x5B), .key(.command))
    }

    func testCtrlIsControl() {
        XCTAssertEqual(KeyMap.target(forWindows: 0x11), .key(.control))
    }

    func testAltIsOption() {
        XCTAssertEqual(KeyMap.target(forWindows: 0x12), .key(.option))
    }

    func testShiftIsShift() {
        XCTAssertEqual(KeyMap.target(forWindows: 0x10), .key(.shift))
    }

    func testLeftArrowMapsToMacLeftArrow() {
        XCTAssertEqual(KeyMap.target(forWindows: 0x25), .key(.leftArrow))
    }

    func testUpArrowMapsToMacUpArrow() {
        XCTAssertEqual(KeyMap.target(forWindows: 0x26), .key(.upArrow))
    }

    func testRightArrowMapsToMacRightArrow() {
        XCTAssertEqual(KeyMap.target(forWindows: 0x27), .key(.rightArrow))
    }

    func testPlayPauseIsAMediaKey() {
        XCTAssertEqual(KeyMap.target(forWindows: 0xB3), .media(.play))
    }

    func testAnUnmappedCodeIsNil() {
        XCTAssertNil(KeyMap.target(forWindows: 0xE9))
    }
}
