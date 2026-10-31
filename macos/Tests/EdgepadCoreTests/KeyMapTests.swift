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

    func testLeftCommandIsACommandModifier() {
        XCTAssertEqual(MacKey.command.modifier, .command)
    }

    func testRightCommandIsACommandModifier() {
        XCTAssertEqual(MacKey.rightCommand.modifier, .command)
    }

    func testRightShiftIsAShiftModifier() {
        XCTAssertEqual(MacKey.rightShift.modifier, .shift)
    }

    func testRightControlIsAControlModifier() {
        XCTAssertEqual(MacKey.rightControl.modifier, .control)
    }

    func testRightOptionIsAnOptionModifier() {
        XCTAssertEqual(MacKey.rightOption.modifier, .option)
    }

    func testALetterIsNoModifier() {
        XCTAssertNil(MacKey.q.modifier)
    }

    func testDownArrowIsAnArrow() {
        XCTAssertTrue(MacKey.downArrow.isArrow)
    }

    func testSpaceIsNotAnArrow() {
        XCTAssertFalse(MacKey.space.isArrow)
    }

    func testF11IsFunctionOrNavigation() {
        XCTAssertTrue(MacKey.f11.isFunctionOrNavigation)
    }

    func testHomeIsFunctionOrNavigation() {
        XCTAssertTrue(MacKey(0x73).isFunctionOrNavigation)
    }

    func testALetterIsNotFunctionOrNavigation() {
        XCTAssertFalse(MacKey.q.isFunctionOrNavigation)
    }

    func testPrintScreenIsTheScreenshotChord() {
        XCTAssertEqual(KeyMap.target(forWindows: 0x2C), .chord([.shift, .command, .three]))
    }

    func testNumLockIsTheKeypadClearKey() {
        XCTAssertEqual(KeyMap.target(forWindows: 0x90), .key(MacKey(0x47)))
    }

    // The Menu key (0x5D) is the one code a Mac drops on purpose; every other code in keys.txt must map.
    func testEveryKeyInTheSharedTableIsMappedExceptMenu() {
        for code in sharedKeyCodes() {
            if code == 0x5D {
                XCTAssertNil(KeyMap.target(forWindows: code))
            } else {
                XCTAssertNotNil(KeyMap.target(forWindows: code), "0x\(String(code, radix: 16)) has no Mac key")
            }
        }
    }

    private func sharedKeyCodes() -> [UInt16] {
        var dir = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
        while dir.path != "/" {
            let candidate = dir.appendingPathComponent("protocol/keys.txt")
            if let text = try? String(contentsOf: candidate, encoding: .utf8) {
                return text.split(separator: "\n").compactMap { line in
                    let parts = line.split(separator: " ")
                    guard parts.count == 3, parts[0] == "KEY" else { return nil }
                    return UInt16(parts[1].dropFirst(2), radix: 16)
                }
            }
            dir.deleteLastPathComponent()
        }
        XCTFail("protocol/keys.txt was not found above \(#filePath)")
        return []
    }
}
