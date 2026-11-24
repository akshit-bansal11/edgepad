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

    func testTheTileStartsAtTheLogoPathAndIsClosed() throws {
        XCTAssertEqual(LogoMotion.tileStart.x, 354.65, accuracy: 1e-9)
        XCTAssertEqual(LogoMotion.tileStart.y, 0, accuracy: 1e-9)
        let last = try XCTUnwrap(LogoMotion.tileSegments.last)
        XCTAssertEqual(last.count, 6)
        XCTAssertEqual(last[4], LogoMotion.tileStart.x, accuracy: 1e-9)
        XCTAssertEqual(last[5], LogoMotion.tileStart.y, accuracy: 1e-9)
    }

    func testTheTileIsSymmetricAcrossBothAxes() {
        var points: [(x: Double, y: Double)] = [LogoMotion.tileStart]
        for segment in LogoMotion.tileSegments {
            for index in stride(from: 0, to: segment.count, by: 2) {
                points.append((segment[index], segment[index + 1]))
            }
        }
        for point in points {
            XCTAssertTrue(
                points.contains { abs($0.x - (LogoMotion.canvas - point.x)) < 1e-9 && abs($0.y - point.y) < 1e-9 })
            XCTAssertTrue(
                points.contains { abs($0.x - point.x) < 1e-9 && abs($0.y - (LogoMotion.canvas - point.y)) < 1e-9 })
        }
    }
}
