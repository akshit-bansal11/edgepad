import CryptoKit
import Foundation
import XCTest

@testable import EdgepadCore

/// The Mac's side of sending a capture to the phone: which file is picked, and when, if ever, it is deleted.
final class CaptureToPhoneTests: XCTestCase {
    private final class Folder: CaptureFolders {
        let path: String
        var stamp: Int?
        var png: Data?

        init(_ path: String) {
            self.path = path
        }

        func folders(for capture: ActionId) -> [CaptureFolder] {
            [CaptureFolder(path: path, shared: false)]
        }

        func clipboardStamp() -> Int? {
            stamp
        }

        func clipboardPNG() -> Data? {
            png
        }
    }

    private var directory = ""
    private var scheduled: [() -> Void] = []
    private var sent: [Frame] = []
    /// The capture's clock, moved by hand.
    private var now: TimeInterval = 0

    override func setUpWithError() throws {
        directory = (NSTemporaryDirectory() as NSString).appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(atPath: directory, withIntermediateDirectories: true)
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(atPath: directory)
    }

    private func capture(_ folder: Folder? = nil) -> CaptureToPhone {
        let later: (TimeInterval, @escaping () -> Void) -> Void = { [unowned self] _, work in
            self.scheduled.append(work)
        }
        let capture = CaptureToPhone(
            folders: folder ?? Folder(directory), clock: { [unowned self] in self.now }, schedule: later,
            lastInput: { -.infinity },
            offMain: { work, done in done(work()) })
        capture.send = { [unowned self] frame in
            self.sent.append(frame)
            return true
        }
        XCTAssertTrue(capture.setTarget(1))
        return capture
    }

    /// Runs what is scheduled now, once; what that schedules waits for the next call.
    private func fire(_ times: Int = 1) {
        for _ in 0..<times {
            let now = scheduled
            scheduled.removeAll()
            for task in now { task() }
        }
    }

    private func shoot(_ capture: CaptureToPhone) -> String {
        let path = (directory as NSString).appendingPathComponent("Screenshot 1.png")
        let pressed = capture.run(.captureScreen) {
            FileManager.default.createFile(atPath: path, contents: Data(repeating: 7, count: 600))
        }
        XCTAssertTrue(pressed)
        return path
    }

    private var endSent: Bool {
        sent.contains { frame in
            if case .fileEnd = frame {
                return true
            }
            return false
        }
    }

    func testAnUnchangedFileSavedAfterItsEndIsDeleted() {
        let capture = capture()
        let path = shoot(capture)

        fire(4)

        XCTAssertTrue(endSent)
        XCTAssertTrue(capture.take(status: FileStatus.saved.rawValue))
        XCTAssertFalse(FileManager.default.fileExists(atPath: path))
    }

    func testASavedBeforeTheEndKeepsTheFile() {
        let capture = capture()
        let path = shoot(capture)

        fire(3)
        XCTAssertFalse(endSent)
        XCTAssertTrue(capture.take(status: FileStatus.saved.rawValue))

        XCTAssertTrue(FileManager.default.fileExists(atPath: path))
    }

    func testAFileThatChangedAfterItWasSentIsKept() throws {
        let capture = capture()
        let path = shoot(capture)

        fire(4)
        try FileManager.default.setAttributes([.modificationDate: Date(timeIntervalSinceNow: 3600)], ofItemAtPath: path)
        XCTAssertTrue(capture.take(status: FileStatus.saved.rawValue))

        XCTAssertTrue(FileManager.default.fileExists(atPath: path))
    }

    func testAFullLinkHoldsTheChunksBackUntilItDrains() {
        var held = CaptureToPhone.maxBacklog
        let capture = capture()
        capture.backlog = { held }
        _ = shoot(capture)

        fire(4)
        XCTAssertEqual(sent.count, 1, "only FILE_BEGIN may go while the link is full")

        held = 0
        fire()
        XCTAssertTrue(endSent)
    }

    private func clipboardFolder() -> Folder {
        let folder = Folder(directory)
        folder.stamp = 1
        folder.png = Data([1, 2, 3])
        return folder
    }

    private func beginNames() -> [String] {
        sent.compactMap { frame -> String? in
            guard case .fileBegin(_, _, let name) = frame else { return nil }
            return name
        }
    }

    func testTheClipboardPictureIsSentAfterTheGraceAndNotBefore() {
        let folder = clipboardFolder()
        let capture = capture(folder)
        XCTAssertTrue(capture.run(.captureScreen) { true })
        folder.stamp = 2

        fire()
        now = CaptureToPhone.clipboardGrace - 0.1
        fire()
        XCTAssertTrue(sent.isEmpty)

        now = CaptureToPhone.clipboardGrace
        fire()
        let names = beginNames()
        XCTAssertEqual(names.count, 1)
        XCTAssertTrue(names[0].hasPrefix("Screenshot "))
        XCTAssertTrue(names[0].hasSuffix(".png"))
        capture.end()
    }

    func testAFileThatArrivesFirstWinsOverTheClipboard() {
        let folder = clipboardFolder()
        let capture = capture(folder)
        XCTAssertTrue(capture.run(.captureScreen) { true })
        folder.stamp = 2
        fire()
        let path = (directory as NSString).appendingPathComponent("Screenshot 1.png")
        FileManager.default.createFile(atPath: path, contents: Data(repeating: 7, count: 600))

        now = CaptureToPhone.clipboardGrace
        fire(4)

        XCTAssertEqual(beginNames(), ["Screenshot 1.png"])
        capture.end()
    }

    func testNothingIsSentFromAClipboardThatDidNotChangeOrCannotBeRead() {
        let folder = clipboardFolder()
        let capture = capture(folder)
        XCTAssertTrue(capture.run(.captureScreen) { true })

        now = 10
        fire(3)
        XCTAssertTrue(sent.isEmpty)

        folder.stamp = nil
        XCTAssertTrue(capture.run(.captureScreen) { true })
        now = 20
        fire(4)
        XCTAssertTrue(sent.isEmpty)
        capture.end()
    }

    func testARecordingNeverFallsBackToTheClipboard() {
        let folder = clipboardFolder()
        let capture = capture(folder)
        XCTAssertTrue(capture.run(.captureRecord) { true })
        folder.stamp = 2

        now = 10
        fire(4)

        XCTAssertTrue(sent.isEmpty)
    }

    func testAnAnswerWithNothingOnItsWayIsNotTaken() {
        XCTAssertFalse(capture().take(status: FileStatus.saved.rawValue))
    }

    func testOnlyLaptopAndPhoneAreTargets() {
        let capture = capture()

        XCTAssertTrue(capture.setTarget(0))
        XCTAssertFalse(capture.setTarget(2))
    }

    func testTwoNewFilesAreNeitherPicked() {
        let stamp = Date()
        let files = [
            FileStamp(path: "/s/a.png", length: 1, written: stamp),
            FileStamp(path: "/s/b.png", length: 1, written: stamp),
        ]

        let (file, ambiguous) = CapturePick.newFile(before: [], now: files)

        XCTAssertNil(file)
        XCTAssertTrue(ambiguous)
    }

    func testAFileThereBeforeOrOfAnotherKindIsNotACandidate() {
        let stamp = Date()
        let files = [
            FileStamp(path: "/s/Old.PNG", length: 1, written: stamp),
            FileStamp(path: "/s/notes.txt", length: 1, written: stamp),
            FileStamp(path: "/s/.hidden.png", length: 1, written: stamp),
            FileStamp(path: "/s/Screenshot 2.png", length: 1, written: stamp),
        ]

        let (file, ambiguous) = CapturePick.newFile(before: ["/s/old.png"], now: files)

        XCTAssertEqual(file?.path, "/s/Screenshot 2.png")
        XCTAssertFalse(ambiguous)
    }

    func testOnTheDesktopOnlyACaptureNameIsTaken() {
        XCTAssertTrue(CapturePick.candidate("/Users/me/Desktop/Screenshot 2026-10-25 at 10.30.00.png", shared: true))
        XCTAssertTrue(CapturePick.candidate("/Users/me/Desktop/Screen Recording 2026-10-25.mov", shared: true))
        XCTAssertFalse(CapturePick.candidate("/Users/me/Desktop/holiday.png", shared: true))
    }

    /// A Mac in another language names its screenshots in it; Spotlight's mark is taken instead.
    func testSpotlightsMarkOrAnEnglishNameMakesACapture() {
        XCTAssertTrue(CapturePick.isScreenCapture(name: "Bildschirmfoto 2026-11-16 um 10.30.00.png", flag: true))
        XCTAssertTrue(CapturePick.isScreenCapture(name: "Screenshot 2026-11-16 at 10.30.00.png", flag: false))
        XCTAssertFalse(CapturePick.isScreenCapture(name: "Bildschirmfoto 2026-11-16 um 10.30.00.png", flag: false))
    }

    func testOnTheDesktopSpotlightIsAskedOnlyAboutANewFileWithoutACaptureName() {
        let stamp = Date()
        var asked: [String] = []
        let files = [
            FileStamp(path: "/d/Bildschirmfoto 1.png", length: 1, written: stamp, shared: true),
            FileStamp(path: "/d/Screenshot 2.png", length: 1, written: stamp, shared: true),
            FileStamp(path: "/d/notes.txt", length: 1, written: stamp, shared: true),
            FileStamp(path: "/d/old.png", length: 1, written: stamp, shared: true),
        ]

        let (file, ambiguous) = CapturePick.newFile(before: ["/d/old.png"], now: files) { path in
            asked.append(path)
            return false
        }

        XCTAssertEqual(file?.path, "/d/Screenshot 2.png")
        XCTAssertFalse(ambiguous)
        XCTAssertEqual(asked, ["/d/Bildschirmfoto 1.png"])
    }

    func testTheHashReadInChunksIsTheHashOfTheWholeFile() throws {
        let path = (directory as NSString).appendingPathComponent("big.mov")
        let bytes = (0..<(3 * 1024 * 1024)).map { UInt8(truncatingIfNeeded: $0 &* 31) }
        XCTAssertTrue(FileManager.default.createFile(atPath: path, contents: Data(bytes)))

        XCTAssertEqual(CaptureToPhone.digest(atPath: path), Array(SHA256.hash(data: bytes)))
    }
}
