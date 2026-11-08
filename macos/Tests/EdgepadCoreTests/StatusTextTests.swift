import XCTest

@testable import EdgepadCore

final class StatusTextTests: XCTestCase {
    func testConnectedNamesThePhoneAndTheLink() {
        XCTAssertEqual(StatusText.connected("Pixel", over: "Bluetooth"), "Connected to Pixel · Bluetooth")
    }

    func testAnOlderPhoneIsToldToUpdateThePhone() {
        XCTAssertEqual(
            StatusText.mismatch(name: "Pixel", phoneVersion: 4, laptopVersion: 5),
            "Pixel could not connect: its Edgepad app is older. Update Edgepad on the phone.")
    }

    func testAnOlderLaptopIsToldToUpdateHere() {
        XCTAssertEqual(
            StatusText.mismatch(name: "Pixel", phoneVersion: 6, laptopVersion: 5),
            "Pixel could not connect: Edgepad on this laptop is older. Update Edgepad here.")
    }

    func testBluetoothPermissionOutranksPower() {
        XCTAssertTrue(StatusText.bluetoothProblem(denied: true, off: true).contains("not allowed"))
        XCTAssertTrue(StatusText.bluetoothProblem(denied: false, off: true).hasPrefix("Bluetooth is off."))
        XCTAssertEqual(
            StatusText.bluetoothProblem(denied: false, off: false), "Bluetooth is off or unavailable; trying again")
    }

    func testASecondLaunchOfTheSameVersionExits() {
        XCTAssertEqual(SingleInstance.decide(mine: "4.0.0", others: ["4.0.0"]), .exitWithNotice)
        XCTAssertEqual(SingleInstance.decide(mine: "4.0.0", others: [nil, "3.9.0"]), .takeOver)
        XCTAssertEqual(SingleInstance.decide(mine: "4.0.0", others: []), .takeOver)
    }
}
