import XCTest

@testable import EdgepadCore

final class PasteboardTypesTests: XCTestCase {
    func testNothingOfferedIsNothing() {
        XCTAssertEqual(PasteboardTypes.pick([]), .nothing)
        XCTAssertEqual(PasteboardTypes.pick(["public.file-url"]), .nothing)
    }

    func testTextBeatsAnImage() {
        XCTAssertEqual(PasteboardTypes.pick([PasteboardTypes.png, PasteboardTypes.text]), .text)
    }

    func testPngBeatsTiff() {
        XCTAssertEqual(PasteboardTypes.pick([PasteboardTypes.tiff, PasteboardTypes.png]), .png)
        XCTAssertEqual(PasteboardTypes.pick([PasteboardTypes.tiff]), .tiff)
    }

    func testEachSensitiveMarkerWinsOverTheContent() {
        for marker in PasteboardTypes.sensitiveTypes {
            XCTAssertEqual(PasteboardTypes.pick([PasteboardTypes.text, marker]), .sensitive, marker)
        }
    }
}
