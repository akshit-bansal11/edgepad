import XCTest

@testable import EdgepadCore

final class CaptureOverlayTests: XCTestCase {
    private var now: TimeInterval = 100

    private func makeOverlay() -> CaptureOverlay {
        CaptureOverlay(clock: { [unowned self] in self.now })
    }

    func testNothingPendingMeansNoEscape() {
        var overlay = makeOverlay()

        XCTAssertFalse(overlay.begin(opensOverlay: true))
        var other = makeOverlay()
        XCTAssertFalse(other.cancel())
    }

    func testANewActionWhileAnOverlayIsOpenDismissesItFirst() {
        var overlay = makeOverlay()
        _ = overlay.begin(opensOverlay: true)
        now += 1

        XCTAssertTrue(overlay.begin(opensOverlay: false))
        XCTAssertFalse(overlay.isPending)
    }

    func testCancelDismissesOnceThenIsQuiet() {
        var overlay = makeOverlay()
        _ = overlay.begin(opensOverlay: true)

        XCTAssertTrue(overlay.cancel())
        XCTAssertFalse(overlay.cancel())
    }

    func testTheOverlayIsForgottenAfterTheCeiling() {
        var overlay = makeOverlay()
        _ = overlay.begin(opensOverlay: true)
        now += CaptureOverlay.pendingSeconds - 0.001
        XCTAssertTrue(overlay.isPending)

        now += 0.001
        XCTAssertFalse(overlay.isPending)
        XCTAssertFalse(overlay.cancel())
    }
}
