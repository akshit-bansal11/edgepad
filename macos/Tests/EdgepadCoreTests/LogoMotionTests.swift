import XCTest

@testable import EdgepadCore

final class LogoMotionTests: XCTestCase {
    func testEveryPartStartsSmallAndInvisible() {
        for pose in LogoMotion.frame(ms: 0) {
            XCTAssertEqual(pose.scale, 0.55, accuracy: 1e-9)
            XCTAssertEqual(pose.opacity, 0, accuracy: 1e-9)
        }
    }

    func testEveryPartEndsFullSizeAndOpaque() {
        let poses = LogoMotion.frame(ms: LogoMotion.totalMs)
        XCTAssertEqual(poses.count, LogoMotion.strokes.count)
        for pose in poses {
            XCTAssertEqual(pose.scale, 1, accuracy: 1e-9)
            XCTAssertEqual(pose.opacity, 1, accuracy: 1e-9)
        }
    }
}
