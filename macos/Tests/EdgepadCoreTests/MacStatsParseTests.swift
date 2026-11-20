import XCTest

@testable import EdgepadCore

final class MacStatsParseTests: XCTestCase {
    func testCpuPercentRoundsAndKnowsWhenNoTickPassed() {
        XCTAssertEqual(MacStatsParse.cpuPercent(busy: 1, total: 3), 33)
        XCTAssertEqual(MacStatsParse.cpuPercent(busy: 2, total: 3), 67)
        XCTAssertEqual(MacStatsParse.cpuPercent(busy: 9, total: 3), 100)
        XCTAssertEqual(MacStatsParse.cpuPercent(busy: 0, total: 0), -1)
    }

    func testRatesAreKibPerSecondAndZeroWithoutTime() {
        XCTAssertEqual(MacStatsParse.kibPerSecond(bytes: 2048, seconds: 2), 1)
        XCTAssertEqual(MacStatsParse.kibPerSecond(bytes: 2048, seconds: 0), 0)
        XCTAssertEqual(MacStatsParse.kibPerSecond(bytes: UInt64.max, seconds: 1), Int(Int32.max))
    }

    func testRouteReadsTheGatewayAndInterface() {
        let text = "   route to: default\n    gateway: 192.168.1.1\n  interface: en0\n      flags: <UP>\n"
        let route = MacStatsParse.route(text)
        XCTAssertEqual(route?.gateway, "192.168.1.1")
        XCTAssertEqual(route?.interface, "en0")
        XCTAssertNil(MacStatsParse.route("route: writing to routing socket: not in table\n"))
    }

    func testOnlyADottedIPv4AddressIsPinged() {
        XCTAssertTrue(MacStatsParse.isIPv4("10.0.0.1"))
        XCTAssertFalse(MacStatsParse.isIPv4("fe80::1%en0"))
        XCTAssertFalse(MacStatsParse.isIPv4("256.1.1.1"))
        XCTAssertFalse(MacStatsParse.isIPv4("1.1.1"))
        XCTAssertFalse(MacStatsParse.isIPv4("+1.1.1.1"))
        XCTAssertFalse(MacStatsParse.isIPv4("-h.1.1.1"))
        XCTAssertFalse(MacStatsParse.isIPv4("link#11"))
    }

    func testPingReadsTheRoundTrip() {
        XCTAssertEqual(MacStatsParse.pingMs("64 bytes from 192.168.1.1: icmp_seq=0 ttl=64 time=3.612 ms"), 4)
        XCTAssertNil(MacStatsParse.pingMs("Request timeout for icmp_seq 0"))
    }
}
