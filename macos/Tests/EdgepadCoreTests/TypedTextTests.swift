import XCTest

@testable import EdgepadCore

final class TypedTextTests: XCTestCase {
    func testEveryLineEndingIsOneReturn() {
        XCTAssertEqual(
            TypedText.strokes("a\r\nb\nc\rd"),
            [.text("a"), .returnKey, .text("b"), .returnKey, .text("c"), .returnKey, .text("d")])
    }

    func testBackspaceIsDeleteAndAnEmojiGoesOutWhole() {
        XCTAssertEqual(TypedText.strokes("\u{8}👍🏽"), [.delete, .text("👍🏽")])
    }
}
