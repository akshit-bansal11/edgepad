import XCTest

@testable import EdgepadCore

/// The stroke model on its own: which PEN frames and tool changes a session accepts, and the geometry the
/// overlay and the export both draw from. Ported from the Windows tests, so the two cannot disagree.
final class StrokeBoardTests: XCTestCase {
    private let up: UInt8 = 0
    private let down: UInt8 = 1
    private let move: UInt8 = 2

    private func started(_ mode: BoardMode = .annotate) -> StrokeBoard {
        let board = StrokeBoard()
        board.begin(mode)
        return board
    }

    private func draw(_ board: StrokeBoard, _ fromX: UInt16, _ fromY: UInt16, _ toX: UInt16, _ toY: UInt16) {
        XCTAssertTrue(board.pen(x: fromX, y: fromY, state: down))
        XCTAssertTrue(board.pen(x: toX, y: toY, state: move))
        XCTAssertTrue(board.pen(x: toX, y: toY, state: up))
    }

    func testANewBoardIsOffAndAcceptsNothing() {
        let board = StrokeBoard()

        XCTAssertEqual(board.mode, .off)
        XCTAssertFalse(board.pen(x: 1, y: 1, state: down))
        XCTAssertFalse(board.setTool(.pen))
        XCTAssertFalse(board.undo())
        XCTAssertFalse(board.clear())
        XCTAssertTrue(board.strokes.isEmpty)
        XCTAssertNil(board.active)
    }

    func testADownMoveUpIsOneStrokeAndIsFinishedOnlyOnTheUp() {
        let board = started()

        XCTAssertTrue(board.pen(x: 10, y: 20, state: down))
        XCTAssertTrue(board.pen(x: 30, y: 40, state: move))
        XCTAssertTrue(board.strokes.isEmpty)
        XCTAssertNotNil(board.active)

        XCTAssertTrue(board.pen(x: 50, y: 60, state: up))

        XCTAssertNil(board.active)
        XCTAssertEqual(board.strokes.count, 1)
        XCTAssertEqual(board.strokes[0].points, [NormPoint(10, 20), NormPoint(30, 40), NormPoint(50, 60)])
    }

    func testAMoveOrUpWithNoFingerDownIsDropped() {
        for state in [move, up] {
            let board = started()

            XCTAssertFalse(board.pen(x: 5, y: 5, state: state))
            XCTAssertTrue(board.strokes.isEmpty)
            XCTAssertNil(board.active)
        }
    }

    func testAStateThatIsNotUpDownOrMoveIsDroppedEvenMidStroke() {
        for state: UInt8 in [3, 7, 255] {
            let board = started()
            XCTAssertTrue(board.pen(x: 1, y: 1, state: down))

            XCTAssertFalse(board.pen(x: 2, y: 2, state: state))

            XCTAssertEqual(board.active?.points.count, 1)
        }
    }

    func testADownWhileOneIsOpenFinishesTheOpenOneFirst() {
        let board = started()
        XCTAssertTrue(board.pen(x: 1, y: 1, state: down))
        XCTAssertTrue(board.pen(x: 2, y: 2, state: move))

        XCTAssertTrue(board.pen(x: 9, y: 9, state: down))

        XCTAssertEqual(board.strokes.count, 1)
        XCTAssertEqual(board.strokes[0].points.count, 2)
        XCTAssertEqual(board.active?.points, [NormPoint(9, 9)])
    }

    func testAStrokeIsCappedRatherThanGrowingWithoutEnd() {
        let board = started()
        XCTAssertTrue(board.pen(x: 0, y: 0, state: down))
        for _ in 1..<StrokeBoard.maxPointsPerStroke {
            XCTAssertTrue(board.pen(x: 1, y: 1, state: move))
        }

        XCTAssertFalse(board.pen(x: 2, y: 2, state: move))
        XCTAssertEqual(board.active?.points.count, StrokeBoard.maxPointsPerStroke)
    }

    func testAToolChangeMidStrokeAppliesToTheNextStroke() {
        let board = started()
        XCTAssertTrue(board.pen(x: 1, y: 1, state: down))

        XCTAssertTrue(board.setTool(.rectangle))
        XCTAssertTrue(board.setColour(2))
        XCTAssertTrue(board.pen(x: 2, y: 2, state: up))
        XCTAssertTrue(board.pen(x: 3, y: 3, state: down))

        XCTAssertEqual(board.strokes[0].tool, .pen)
        XCTAssertEqual(board.strokes[0].colour, 0)
        XCTAssertEqual(board.active?.tool, .rectangle)
        XCTAssertEqual(board.active?.colour, 2)
    }

    func testSigningAcceptsOnlyThePenTool() {
        for tool in StrokeTool.allCases where tool != .pen {
            let board = started(.sign)

            XCTAssertFalse(board.setTool(tool))
            XCTAssertTrue(board.setTool(.pen))
        }
    }

    func testAnnotatingAcceptsEveryTool() {
        for tool in StrokeTool.allCases {
            let board = started()

            XCTAssertTrue(board.setTool(tool))
            XCTAssertEqual(board.tool, tool)
        }
    }

    func testABeginStartsOnThePenWhateverTheLastSessionLeftSelected() {
        let board = started()
        XCTAssertTrue(board.setTool(.ellipse))

        board.begin(.sign)

        XCTAssertEqual(board.tool, .pen)
    }

    func testOnlyAPaletteIndexSetsTheColour() {
        for (index, accepted) in [(0, true), (7, true), (8, false), (-1, false)] {
            let board = started()

            XCTAssertEqual(board.setColour(index), accepted)
            XCTAssertEqual(board.colour, accepted ? index : 0)
        }
    }

    func testUndoTakesBackTheLastFinishedStrokeAndNeverTheOneUnderTheFinger() {
        let board = started()
        draw(board, 1, 1, 2, 2)
        draw(board, 3, 3, 4, 4)
        XCTAssertTrue(board.pen(x: 5, y: 5, state: down))

        XCTAssertTrue(board.undo())

        XCTAssertEqual(board.strokes.count, 1)
        XCTAssertEqual(board.strokes[0].points[0], NormPoint(1, 1))
        XCTAssertNotNil(board.active)
        XCTAssertTrue(board.undo())
        XCTAssertFalse(board.undo())
    }

    func testClearEmptiesTheBoardAndDiscardsAStrokeInProgress() {
        let board = started()
        draw(board, 1, 1, 2, 2)
        XCTAssertTrue(board.pen(x: 5, y: 5, state: down))

        XCTAssertTrue(board.clear())

        XCTAssertTrue(board.strokes.isEmpty)
        XCTAssertNil(board.active)
        XCTAssertEqual(board.mode, .annotate)
        XCTAssertFalse(board.pen(x: 6, y: 6, state: move))
    }

    func testEndAndABeginBothDiscardTheStrokes() {
        let board = started()
        draw(board, 1, 1, 2, 2)

        board.end()
        XCTAssertEqual(board.mode, .off)
        XCTAssertTrue(board.strokes.isEmpty)
        XCTAssertFalse(board.pen(x: 1, y: 1, state: down))

        board.begin(.annotate)
        draw(board, 1, 1, 2, 2)
        board.begin(.sign)
        XCTAssertTrue(board.strokes.isEmpty)
        XCTAssertNil(board.active)
    }

    func testAHighlighterStrokeIsOnePathHoweverManyPointsItHas() {
        let board = started()
        XCTAssertTrue(board.setTool(.highlighter))
        XCTAssertTrue(board.pen(x: 100, y: 100, state: down))
        XCTAssertTrue(board.pen(x: 20000, y: 100, state: move))
        XCTAssertTrue(board.pen(x: 100, y: 100, state: move))
        XCTAssertTrue(board.pen(x: 20000, y: 100, state: move))
        XCTAssertTrue(board.pen(x: 100, y: 100, state: up))

        XCTAssertEqual(board.strokes.count, 1)
        XCTAssertEqual(board.strokes[0].tool, .highlighter)
        XCTAssertEqual(board.strokes[0].points.count, 5)
    }

    func testAShapeRunsFromTheDownPointToTheLatestMove() {
        for tool in [StrokeTool.line, .arrow, .rectangle, .ellipse] {
            let board = started()
            XCTAssertTrue(board.setTool(tool))
            XCTAssertTrue(board.pen(x: 100, y: 200, state: down))
            XCTAssertTrue(board.pen(x: 300, y: 400, state: move))
            XCTAssertTrue(board.pen(x: 500, y: 600, state: move))
            XCTAssertTrue(board.pen(x: 700, y: 800, state: up))

            XCTAssertEqual(board.strokes.count, 1)
            XCTAssertEqual(board.strokes[0].points, [NormPoint(100, 200), NormPoint(700, 800)])
        }
    }

    func testTheActionsDriveTheBoardAndTheWrongOnesAreDropped() {
        let board = StrokeBoard()
        XCTAssertFalse(board.run(.toolPen))
        XCTAssertFalse(board.run(.annotateUndo))

        XCTAssertTrue(board.run(.signBegin))
        XCTAssertEqual(board.mode, .sign)
        XCTAssertFalse(board.run(.toolArrow))
        XCTAssertFalse(board.run(.signSave))

        XCTAssertTrue(board.run(.annotateBegin))
        XCTAssertEqual(board.mode, .annotate)
        XCTAssertTrue(board.run(.toolArrow))
        XCTAssertEqual(board.tool, .arrow)
        XCTAssertFalse(board.run(.muteToggle))

        XCTAssertTrue(board.run(.annotateEnd))
        XCTAssertEqual(board.mode, .off)
    }

    func testWidthsAreASharePerScreenHeightAndNeverBelowOnePixel() {
        XCTAssertEqual(Stroke(tool: .pen, colour: 0).widthPx(height: 1000), 4, accuracy: 0.001)
        XCTAssertEqual(Stroke(tool: .arrow, colour: 0).widthPx(height: 1000), 4, accuracy: 0.001)
        XCTAssertEqual(Stroke(tool: .highlighter, colour: 0).widthPx(height: 1000), 20, accuracy: 0.001)
        XCTAssertEqual(Stroke(tool: .pen, colour: 0).widthPx(height: 100), 1, accuracy: 0.001)
    }

    func testPointsMapTheWholeSpanOntoTheScreen() {
        let stroke = Stroke(tool: .pen, colour: 0, points: [NormPoint(0, 0), NormPoint(65535, 65535)])

        let path = stroke.path(width: 1920, height: 1080)

        XCTAssertEqual(path[0], SIMD2<Float>(0, 0))
        XCTAssertEqual(path[1], SIMD2<Float>(1919, 1079))
    }

    func testAnArrowHeadIsFourPenWidthsLongAndSitsBehindTheTip() {
        let stroke = Stroke(tool: .arrow, colour: 0, points: [NormPoint(0, 32768), NormPoint(65535, 32768)])

        let wings = stroke.arrowWings(width: 1000, height: 1000)
        let tip = stroke.path(width: 1000, height: 1000)[1]

        XCTAssertEqual(wings.count, 2)
        for wing in wings {
            let gap = wing - tip
            XCTAssertEqual((gap * gap).sum().squareRoot(), 16, accuracy: 0.01)
            XCTAssertLessThan(wing.x, tip.x)
        }
        XCTAssertEqual(tip.y, (wings[0].y + wings[1].y) / 2, accuracy: 0.01)
        XCTAssertNotEqual(wings[0].y, wings[1].y)
    }

    func testOnlyAnArrowWithLengthHasAHead() {
        let line = Stroke(tool: .line, colour: 0, points: [NormPoint(0, 0), NormPoint(900, 900)])
        let still = Stroke(tool: .arrow, colour: 0, points: [NormPoint(5, 5), NormPoint(5, 5)])

        XCTAssertTrue(line.arrowWings(width: 1000, height: 1000).isEmpty)
        XCTAssertTrue(still.arrowWings(width: 1000, height: 1000).isEmpty)
        XCTAssertTrue(Stroke(tool: .arrow, colour: 0).arrowWings(width: 1000, height: 1000).isEmpty)
    }
}
