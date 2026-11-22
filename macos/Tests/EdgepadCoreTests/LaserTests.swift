import XCTest

@testable import EdgepadCore

/// The laser's contract (K1): a 250 ms trail, a 400 ms watchdog, three sizes. A fake clock drives it. Ported from
/// the shared C# `LaserTests`, same numbers.
final class LaserTests: XCTestCase {
    private let up: UInt8 = 0
    private let down: UInt8 = 1
    private let move: UInt8 = 2

    private var now: Int64 = 0

    private func laser(_ tool: StrokeTool = .laserTrail, width: Int = 15) -> StrokeBoard {
        let board = StrokeBoard(clock: { self.now })
        board.begin(.annotate)
        XCTAssertTrue(board.setWidth(width))
        XCTAssertTrue(board.setTool(tool))
        return board
    }

    private func trailing(_ atMs: [Int64]) -> Stroke {
        let stroke = Stroke(tool: .laserTrail, style: .default)
        for (i, ms) in atMs.enumerated() {
            stroke.extend(NormPoint(UInt16(i * 100), UInt16(i * 100)), nowMs: ms)
        }
        return stroke
    }

    func testATrailPointOlderThan250MsIsGone() {
        for (age, expected) in [(249, 2), (250, 2), (251, 1)] as [(Int64, Int)] {
            let stroke = trailing([0, 100])
            stroke.trimTrail(nowMs: age)
            XCTAssertEqual(stroke.points.count, expected, "age \(age)")
        }
    }

    func testARestingFingersTrailIsEmptyAfter250Ms() throws {
        let board = laser()
        XCTAssertTrue(board.pen(x: 0, y: 0, state: down))
        now = 100
        XCTAssertTrue(board.pen(x: 500, y: 500, state: move))
        let stroke = try XCTUnwrap(board.active)

        XCTAssertFalse(stroke.trail(width: 1920, height: 1080, nowMs: now).points.isEmpty)
        XCTAssertTrue(stroke.trail(width: 1920, height: 1080, nowMs: now + 251).points.isEmpty)
    }

    func testTheDotFadesOverTheLast150MsOfTheWatchdog() {
        for (silent, visible) in [(0, 1), (399, 1), (400, 1), (475, 0.5), (550, 0), (900, 0)] as [(Int64, Float)] {
            XCTAssertEqual(LaserGeometry.visibility(nowMs: 1000 + silent, lastEventMs: 1000), visible, accuracy: 0.001)
        }
    }

    func testALostPenUpLeavesNothingAfter550Ms() {
        let board = laser(.laser)
        XCTAssertTrue(board.pen(x: 100, y: 100, state: down))
        XCTAssertTrue(board.laserVisible(at: now + 399))
        XCTAssertTrue(board.laserVisible(at: now + 549))
        XCTAssertFalse(board.laserVisible(at: now + 550))
    }

    func testAPenEventRestartsTheWatchdog() {
        let board = laser()
        XCTAssertTrue(board.pen(x: 0, y: 0, state: down))
        now = 300
        XCTAssertTrue(board.pen(x: 1, y: 1, state: move))

        XCTAssertTrue(board.laserVisible(at: now + 549))
        XCTAssertFalse(board.laserVisible(at: now + 550))
    }

    func testPenUpDropsTheStrokeAtOnce() {
        let board = laser()
        XCTAssertTrue(board.pen(x: 0, y: 0, state: down))
        XCTAssertTrue(board.pen(x: 1, y: 1, state: up))

        XCTAssertNil(board.active)
        XCTAssertFalse(board.laserVisible(at: now))
        XCTAssertTrue(board.strokes.isEmpty)
    }

    func testPenAndShapeStrokesAreNeverLaserVisible() {
        let board = StrokeBoard(clock: { self.now })
        board.begin(.annotate)
        XCTAssertTrue(board.pen(x: 0, y: 0, state: down))

        XCTAssertFalse(board.laserVisible(at: now))
    }

    func testReadingWhilePenEventsArriveStaysConsistent() throws {
        let board = laser()
        XCTAssertTrue(board.pen(x: 0, y: 0, state: down))
        for i in 1...50 {
            now += 20
            XCTAssertTrue(board.laserVisible(at: now))
            XCTAssertTrue(board.pen(x: UInt16(i), y: UInt16(i), state: move))
            let stroke = try XCTUnwrap(board.active)
            let (points, ages) = stroke.trail(width: 1920, height: 1080, nowMs: now)
            XCTAssertEqual(points.count, ages.count)
            for age in ages {
                XCTAssertTrue((0...250).contains(age), "age \(age)")
            }
        }
    }

    func testTheDotRadiusIs005PercentOfScreenHeightPerStep() {
        for (width, radius) in [(10, 5.4), (15, 8.1), (20, 10.8)] as [(Int, Float)] {
            var style = PenStyle.default
            style.width = width
            XCTAssertEqual(Stroke(tool: .laser, style: style).laserRadiusPx(height: 1080), radius, accuracy: 0.01)
        }
    }

    func testTheDotHasAMinimumOfTwoPixelsAndTheGlowIs18Times() {
        var style = PenStyle.default
        style.width = 10
        XCTAssertEqual(Stroke(tool: .laser, style: style).laserRadiusPx(height: 100), 2, accuracy: 0.001)
        XCTAssertEqual(Stroke.laserGlow, 1.8, accuracy: 0.001)
    }

    private func line(_ n: Int) -> (points: [SIMD2<Float>], ages: [Float]) {
        (
            (0..<n).map { SIMD2(Float($0) * 20, 0) },
            (0..<n).map { 250 * Float(n - 1 - $0) / Float(n - 1) }
        )
    }

    func testAFewerThanTwoPointsTrailHasNoOutline() {
        XCTAssertTrue(LaserGeometry.outline(points: [], ages: [], radius: 8).isEmpty)
        XCTAssertTrue(LaserGeometry.outline(points: [SIMD2(1, 1)], ages: [0], radius: 8).isEmpty)
    }

    func testTheOutlineIsOneClosedPolygonTaperingFromTheDotToNothing() {
        let radius: Float = 8
        let (points, ages) = line(6)
        let outline = LaserGeometry.outline(points: points, ages: ages, radius: radius)

        let perSide = (points.count - 1) * LaserGeometry.subdivisions + 1
        XCTAssertEqual(outline.count, 2 * perSide)

        let left = Array(outline[..<perSide])
        let right = Array(outline[perSide...].reversed())
        func distance(_ a: SIMD2<Float>, _ b: SIMD2<Float>) -> Float { ((a - b) * (a - b)).sum().squareRoot() }

        // Tail: both edges meet at the oldest point. Head: they are a full diameter apart.
        XCTAssertEqual(distance(left[0], right[0]), 0, accuracy: 0.001)
        XCTAssertEqual(distance(left[perSide - 1], points[points.count - 1]), radius, accuracy: 0.001)
        XCTAssertEqual(distance(right[perSide - 1], points[points.count - 1]), radius, accuracy: 0.001)

        // Symmetric: each pair of edge points is the same distance from the centre line (y = 0 here).
        for i in 0..<perSide {
            XCTAssertEqual(left[i].x, right[i].x, accuracy: 0.001)
            XCTAssertEqual(left[i].y, -right[i].y, accuracy: 0.001)
        }
    }

    func testEveryOutlinePointStaysInsideTheCapsuleAroundTheTrail() {
        let radius: Float = 8
        let (points, ages) = line(6)

        for v in LaserGeometry.outline(points: points, ages: ages, radius: radius) {
            XCTAssertTrue((points[0].x - radius...points[points.count - 1].x + radius).contains(v.x))
            XCTAssertTrue((-radius...radius).contains(v.y))
        }
    }

    func testTrailAlphaRunsFromPoint9AtTheHeadToNothingAtTheTail() {
        XCTAssertEqual(LaserGeometry.trailAlpha(ageMs: 0), 0.9, accuracy: 0.001)
        XCTAssertEqual(LaserGeometry.trailAlpha(ageMs: 250), 0, accuracy: 0.001)
        let middle = LaserGeometry.trailAlpha(ageMs: 125)
        XCTAssertTrue((0.01...0.89).contains(middle))
    }
}
