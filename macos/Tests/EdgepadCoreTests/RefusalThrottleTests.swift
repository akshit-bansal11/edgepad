import XCTest

@testable import EdgepadCore

final class RefusalThrottleTests: XCTestCase {
    func testTheFirstRefusalFromAHostIsLogged() {
        var throttle = RefusalThrottle()
        XCTAssertTrue(throttle.shouldLog(host: "10.0.0.2", now: 100))
    }

    func testASecondRefusalFromTheSameHostWithinTheWindowIsNot() {
        var throttle = RefusalThrottle()
        _ = throttle.shouldLog(host: "10.0.0.2", now: 100)
        XCTAssertFalse(throttle.shouldLog(host: "10.0.0.2", now: 159))
    }

    func testAnotherHostIsLogged() {
        var throttle = RefusalThrottle()
        _ = throttle.shouldLog(host: "10.0.0.2", now: 100)
        XCTAssertTrue(throttle.shouldLog(host: "10.0.0.3", now: 101))
    }

    func testTheSameHostIsLoggedAgainAfterTheWindow() {
        var throttle = RefusalThrottle()
        _ = throttle.shouldLog(host: "10.0.0.2", now: 100)
        XCTAssertTrue(throttle.shouldLog(host: "10.0.0.2", now: 160))
    }

    func testTheTableNeverExceedsItsLimit() {
        var throttle = RefusalThrottle(window: 60, limit: 4)
        for index in 0..<20 {
            _ = throttle.shouldLog(host: "10.0.0.\(index)", now: 100)
            XCTAssertLessThanOrEqual(throttle.count, 4)
        }
        XCTAssertFalse(throttle.shouldLog(host: "10.0.1.1", now: 100))
        XCTAssertTrue(throttle.shouldLog(host: "10.0.1.1", now: 160))
    }
}
