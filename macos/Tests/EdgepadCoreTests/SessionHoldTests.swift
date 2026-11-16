import XCTest

@testable import EdgepadCore

final class SessionHoldTests: XCTestCase {
    func testOneReasonIsOneHoldAndOneResume() {
        var hold = SessionHold()

        XCTAssertTrue(hold.begin(.locked))
        XCTAssertTrue(hold.end(.locked))
    }

    func testOverlappingReasonsAreOneHoldAndOneResume() {
        var hold = SessionHold()

        XCTAssertTrue(hold.begin(.locked))
        XCTAssertFalse(hold.begin(.switchedOut))
        XCTAssertFalse(hold.end(.locked))
        XCTAssertTrue(hold.end(.switchedOut))
    }

    func testARepeatedReasonChangesNothing() {
        var hold = SessionHold()

        XCTAssertTrue(hold.begin(.locked))
        XCTAssertFalse(hold.begin(.locked))
        XCTAssertTrue(hold.end(.locked))
        XCTAssertFalse(hold.end(.locked))
    }

    func testEndingAReasonThatWasNotHeldIsNothing() {
        var hold = SessionHold()

        XCTAssertFalse(hold.end(.switchedOut))
        XCTAssertTrue(hold.begin(.locked))
        XCTAssertFalse(hold.end(.switchedOut))
        XCTAssertTrue(hold.end(.locked))
    }

    func testLockSwitchOutUnlockSwitchInIsOneHoldAndOneResume() {
        var hold = SessionHold()
        var holds = 0
        var resumes = 0
        func count(_ begun: Bool, _ ended: Bool) {
            holds += begun ? 1 : 0
            resumes += ended ? 1 : 0
        }

        count(hold.begin(.locked), false)
        count(hold.begin(.switchedOut), false)
        count(false, hold.end(.locked))
        count(false, hold.end(.switchedOut))

        XCTAssertEqual(holds, 1)
        XCTAssertEqual(resumes, 1)
    }

    func testClearReportsWhetherAHoldWasOpen() {
        var hold = SessionHold()

        XCTAssertFalse(hold.clear())
        XCTAssertTrue(hold.begin(.locked))
        XCTAssertTrue(hold.clear())
        XCTAssertTrue(hold.begin(.locked))
    }
}
