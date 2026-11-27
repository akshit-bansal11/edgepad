import XCTest

@testable import EdgepadCore

/// The same cases as the shared core's `PowerControlTests`.
final class PowerControlTests: XCTestCase {
    private var platform = FakePowerPlatform()
    private var states: [String] = []
    private var control: PowerControl!

    override func setUp() {
        platform = FakePowerPlatform()
        states = []
        control = PowerControl(platform: platform)
        control.stateChanged = { [weak self] state in self?.states.append(state) }
    }

    func testRestartReportsTheCountdownThenNothingOnCancel() {
        XCTAssertTrue(control.run(.restart))
        XCTAssertEqual(states, ["restart/60"])
        XCTAssertEqual(control.currentState(), "restart/60")

        XCTAssertTrue(control.run(.powerCancel))
        XCTAssertEqual(states, ["restart/60", ""])
        XCTAssertEqual(control.currentState(), "")
        XCTAssertTrue(platform.executed.isEmpty)
    }

    func testExpiryExecutesOnceAndReportsNothing() {
        XCTAssertTrue(control.run(.shutDown))
        platform.finish(true)
        XCTAssertEqual(platform.executed, [.shutDown])
        XCTAssertEqual(states, ["shutdown/60", ""])
    }

    func testASecondRestartWhilePendingExecutesNothingAndReportsNothing() {
        XCTAssertTrue(control.run(.restart))
        XCTAssertTrue(control.run(.restart))
        XCTAssertTrue(control.run(.shutDown))
        XCTAssertEqual(platform.started, [.restart])
        XCTAssertEqual(states, ["restart/60"])
        XCTAssertTrue(platform.executed.isEmpty)
    }

    func testSleepIsRefusedWithoutTheCapability() {
        platform.caps = [.restart]
        XCTAssertFalse(control.run(.sleep))
        XCTAssertFalse(control.run(.hibernate))
        XCTAssertEqual(platform.slept + platform.hibernated, 0)

        platform.caps = [.sleep]
        XCTAssertTrue(control.run(.sleep))
        XCTAssertEqual(platform.slept, 1)
    }

    func testACountdownOutlivesTheSessionAndTheNextControlHearsTheCancel() {
        XCTAssertTrue(control.run(.restart))
        control.stateChanged = nil

        let next = PowerControl(platform: platform)
        var heard: [String] = []
        next.stateChanged = { heard.append($0) }
        XCTAssertEqual(next.currentState(), "restart/60")
        XCTAssertTrue(next.run(.powerCancel))
        XCTAssertEqual(heard, [""])
    }

    func testFeaturesComeInAFixedOrder() {
        XCTAssertEqual(
            PowerControl.features([.shutDown, .sleep, .restart, .hibernate]),
            ["sleep", "hibernate", "restart", "shutdown"])
        XCTAssertEqual(PowerControl.features([.restart, .sleep]), ["sleep", "restart"])
        XCTAssertEqual(PowerControl.features([]), [])
    }
}
