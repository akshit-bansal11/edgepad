import XCTest

@testable import EdgepadCore

final class UpdateDeadlineTests: XCTestCase {
    func testFinishedWorkReturnsItsResultAndAStalledJobIsStoppedAtTheDeadline() async {
        let done = await UpdateDownload.withDeadline(5) { "ok" as String? }
        XCTAssertEqual(done ?? nil, "ok")
        let succeededWithNil = await UpdateDownload.withDeadline(5) { nil as String? }
        XCTAssertNotNil(succeededWithNil)
        let stalled = await UpdateDownload.withDeadline(0.05) { () -> Int in
            try? await Task.sleep(nanoseconds: 30_000_000_000)
            return 1
        }
        XCTAssertNil(stalled)
    }
}
