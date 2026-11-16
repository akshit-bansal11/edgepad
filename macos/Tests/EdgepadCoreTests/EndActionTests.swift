import XCTest

@testable import EdgepadCore

/// The same table as the shared core's `EndActionTests`.
final class EndActionTests: XCTestCase {
    private var performed: [EndKind] = []
    private var states: [String] = []
    private var end: EndAction!

    private static let nothing = MediaSnapshot(identity: "", playing: false, positionSeconds: 0, durationSeconds: 0)

    override func setUp() {
        performed = []
        states = []
        end = EndAction(
            perform: { [weak self] kind in
                self?.performed.append(kind)
                return true
            },
            stateChanged: { [weak self] state in self?.states.append(state) })
    }

    private func playing(_ id: String, _ at: Int, of length: Int = 200) -> MediaSnapshot {
        MediaSnapshot(identity: id, playing: true, positionSeconds: at, durationSeconds: length)
    }

    private func stopped(_ id: String, _ at: Int, of length: Int = 200) -> MediaSnapshot {
        MediaSnapshot(identity: id, playing: false, positionSeconds: at, durationSeconds: length)
    }

    func testItRefusesToArmWithNoTimeline() {
        XCTAssertFalse(end.arm(.sleep))
        end.observe(playing("live", 10, of: 0))
        XCTAssertFalse(end.arm(.sleep))
        XCTAssertEqual(states, [])
    }

    func testItFiresWhenPlaybackStopsInTheLastThreeSeconds() {
        end.observe(playing("a", 50))
        XCTAssertTrue(end.arm(.shutdown))
        end.observe(stopped("a", 120))
        XCTAssertEqual(performed, [])
        end.observe(stopped("a", 197))
        XCTAssertEqual(performed, [.shutdown])
        XCTAssertEqual(states, ["shutdown", "none"])
    }

    func testItFiresWhenTheTrackChangesAfterTheEnd() {
        end.observe(playing("a", 198))
        XCTAssertTrue(end.arm(.sleep))
        end.observe(playing("b", 1))
        XCTAssertEqual(performed, [.sleep])
    }

    func testItDoesNotFireWhenTheTrackChangesEarly() {
        end.observe(playing("a", 50))
        XCTAssertTrue(end.arm(.sleep))
        end.observe(playing("b", 1))
        XCTAssertEqual(performed, [])
    }

    func testItFiresWhenTheSessionVanishesAfterTheEnd() {
        end.observe(playing("a", 199))
        XCTAssertTrue(end.arm(.sleep))
        end.observe(EndActionTests.nothing)
        XCTAssertEqual(performed, [.sleep])
    }

    func testItDoesNotFireWhenTheSessionVanishesEarly() {
        end.observe(playing("a", 20))
        XCTAssertTrue(end.arm(.sleep))
        end.observe(EndActionTests.nothing)
        XCTAssertEqual(performed, [])
    }

    func testCancelDisarmsAndArmingAgainReplacesTheKind() {
        end.observe(playing("a", 50))
        XCTAssertTrue(end.arm(.sleep))
        XCTAssertTrue(end.arm(.sleep))
        XCTAssertTrue(end.arm(.shutdown))
        end.cancel()
        end.cancel()
        XCTAssertEqual(states, ["sleep", "shutdown", "none"])
        end.observe(stopped("a", 199))
        XCTAssertEqual(performed, [])

        XCTAssertTrue(end.arm(.sleep))
        XCTAssertTrue(end.arm(.shutdown))
        end.observe(stopped("a", 199))
        XCTAssertEqual(performed, [.shutdown])
    }

    func testItFiresOnce() {
        end.observe(playing("a", 50))
        XCTAssertTrue(end.arm(.sleep))
        end.observe(stopped("a", 199))
        end.observe(stopped("a", 199))
        end.observe(EndActionTests.nothing)
        XCTAssertEqual(performed.count, 1)
    }
}
