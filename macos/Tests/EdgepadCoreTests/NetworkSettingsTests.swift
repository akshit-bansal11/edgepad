import XCTest

@testable import EdgepadCore

final class NetworkSettingsTests: XCTestCase {
    private let directory = URL(fileURLWithPath: NSTemporaryDirectory())
        .appendingPathComponent("edgepad-network-" + UUID().uuidString)

    private var path: String { directory.appendingPathComponent("network.txt").path }

    override func tearDown() {
        try? FileManager.default.removeItem(at: directory)
        super.tearDown()
    }

    private func write(_ text: String) throws {
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        try text.write(toFile: path, atomically: true, encoding: .utf8)
    }

    func testSwitchingKeepsThePort() throws {
        try write("enabled=0\nport=50000\n")
        let settings = NetworkSettings(path: path)

        XCTAssertTrue(settings.setEnabled(true))

        XCTAssertEqual(settings.load().enabled, true)
        XCTAssertEqual(settings.load().port, 50000)
    }

    func testAMalformedFileIsNeverRewritten() throws {
        let malformed = "enabled=1\nport=50000\nsomething=else\n"
        try write(malformed)
        let settings = NetworkSettings(path: path)

        XCTAssertFalse(settings.setEnabled(true))

        XCTAssertEqual(try String(contentsOfFile: path, encoding: .utf8), malformed)
        XCTAssertFalse(settings.load().enabled)
    }
}
