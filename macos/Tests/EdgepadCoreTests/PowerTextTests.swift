import XCTest

@testable import EdgepadCore

final class PowerTextTests: XCTestCase {
    private func entry(_ kind: BatteryKind, _ level: Int, _ charging: Bool, _ name: String) -> BatteryEntry {
        BatteryEntry(kind: kind, level: level, charging: charging, name: name)
    }

    func testBatteriesMatchTheGoldenPayloads() {
        XCTAssertEqual(
            PowerText.encodeBatteries([
                entry(.laptop, 87, true, "Desk"), entry(.headphones, 60, false, "AirPods"),
            ]),
            "laptop:87:1:Desk|headphones:60:0:AirPods")
        XCTAssertEqual(PowerText.encodeBatteries([]), "")
        XCTAssertEqual(PowerText.encodeBatteries([entry(.laptop, 50, false, "Café")]), "laptop:50:0:Café")
    }

    func testOutputMatchesTheGoldenPayloads() {
        XCTAssertEqual(PowerText.encodeOutput((.headphones, "AirPods")), "headphones/AirPods")
        XCTAssertEqual(PowerText.encodeOutput((.speaker, "Speakers")), "speaker/Speakers")
        XCTAssertEqual(PowerText.encodeOutput(nil), "")
    }

    func testLevelIsClampedAndNameSanitised() {
        XCTAssertEqual(PowerText.encodeBatteries([entry(.mouse, 140, false, " a|b\u{7}c ")]), "mouse:100:0:a b c")
        XCTAssertEqual(PowerText.encodeBatteries([entry(.mouse, -5, false, "  ")]), "mouse:0:0:mouse")
    }

    func testNamesAreCutToCodePointsNeverInsideAPair() {
        let long = String(repeating: "😀", count: 30)
        XCTAssertEqual(
            PowerText.encodeBatteries([entry(.other, 1, false, long)]),
            "other:1:0:" + String(repeating: "😀", count: 24))
        XCTAssertEqual(
            PowerText.encodeOutput((.other, String(repeating: "a", count: 50))),
            "other/" + String(repeating: "a", count: 40))
    }

    func testEntriesAreDroppedFromTheEndAndTheFirstIsKept() {
        let entries = [entry(.laptop, 90, false, "L")] + (0..<20).map { entry(.mouse, $0, false, "Mouse \($0)") }
        let text = PowerText.encodeBatteries(entries)
        XCTAssertLessThanOrEqual(text.utf8.count, 255)
        XCTAssertTrue(text.hasPrefix("laptop:90:0:L|mouse:0:0:Mouse 0|"))
        XCTAssertFalse(text.hasSuffix("|"))
        XCTAssertTrue(text.split(separator: "|").allSatisfy { $0.split(separator: ":").count == 4 })
    }
}
