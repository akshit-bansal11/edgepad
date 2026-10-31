import Foundation
import XCTest

@testable import EdgepadCore

extension Frame {
    fileprivate var listBegin: Bool {
        if case .blobBegin(let kind, _, _) = self {
            return kind == BlobKind.appList.rawValue
        }
        return false
    }

    fileprivate var blobEnded: Bool {
        if case .blobEnd = self {
            return true
        }
        return false
    }
}

/// The trust boundary is what is worth pinning here: whatever token arrives, the store either gains a row this Mac's
/// own catalogue offered or stays byte-identical on disk. Nothing launches: the launcher only writes down what it is
/// asked. The same cases as the shared core's AppPickerTests, on a sender and a catalogue that run at once.
final class AppPickerTests: XCTestCase {
    private let directory = URL(fileURLWithPath: NSTemporaryDirectory())
        .appendingPathComponent("edgepad-picker-" + UUID().uuidString)

    private let catalog = FakeCatalog()
    private let launcher = FakeLauncher()
    private var wire: [Frame] = []
    private var ticks: [TimeInterval] = []

    private var macrosPath: String {
        directory.appendingPathComponent("macros.txt").path
    }

    private lazy var store = MacroStore(path: macrosPath, launcher: launcher, schedule: { $0() })

    override func tearDown() {
        try? FileManager.default.removeItem(at: directory)
        super.tearDown()
    }

    private func app(_ name: String) -> AppEntry {
        AppEntry(name: name, target: "/Applications/" + name + ".app")
    }

    private func sender() -> BlobSender {
        let sender = BlobSender(
            link: .bluetooth,
            schedule: { _, work in work() },
            lastInput: { -.infinity }
        )
        sender.output = { [unowned self] frame in
            self.wire.append(frame)
            return true
        }
        return sender
    }

    /// Scans and sends as soon as it is asked, and runs each icon's tick at once, noting how long it was told to wait.
    private func makePicker() -> AppPicker {
        AppPicker(
            catalog: catalog,
            store: store,
            sender: sender(),
            schedule: { $0() },
            scheduleAfter: { [unowned self] delay, work in
                self.ticks.append(delay)
                work()
            }
        )
    }

    /// The rows of the first list on the wire.
    private func lines() -> [String] {
        guard let begin = wire.firstIndex(where: { $0.listBegin }) else { return [] }
        var bytes: [UInt8] = []
        for frame in wire[(begin + 1)...] {
            guard case .blobChunk(_, let data) = frame else { break }
            bytes += data
        }
        return String(decoding: bytes, as: UTF8.self).split(separator: "\n").map(String.init)
    }

    private func iconBegins() -> [(label: String, size: UInt32)] {
        wire.compactMap { frame -> (label: String, size: UInt32)? in
            guard case .blobBegin(let kind, let size, let label) = frame, kind == BlobKind.appIcon.rawValue else {
                return nil
            }
            return (label, size)
        }
    }

    private func ends() -> Int {
        wire.filter { $0.blobEnded }.count
    }

    private func fileBytes() throws -> Data {
        try Data(contentsOf: URL(fileURLWithPath: macrosPath))
    }

    private func macros(_ count: Int) -> [Macro] {
        (0..<count).map { Macro(name: "M\($0)", target: "m\($0).app") }
    }

    func testTheListIsSortedAndEachRowCarriesItsToken() {
        catalog.apps = [app("Zed"), app("alpha"), app("Beta")]

        XCTAssertTrue(makePicker().list())

        XCTAssertEqual(lines(), ["1.0\talpha", "1.1\tBeta", "1.2\tZed"])
    }

    func testTheListStopsAtItsCapAndAlwaysLosesTheSameApps() {
        catalog.apps = (0...AppPicker.maxApps).reversed().map { app(String(format: "%03d", $0)) }

        _ = makePicker().list()

        let rows = lines()
        XCTAssertEqual(rows.count, AppPicker.maxApps)
        XCTAssertEqual(rows.first, "1.0\t000")
        XCTAssertEqual(rows.last, "1.399\t399")
    }

    func testANameIsCutToItsCapOnAWholeCharacter() {
        let pair = "\u{1F600}"
        catalog.apps = [
            app(String(repeating: "a", count: AppPicker.maxNameChars + 10)),
            app(String(repeating: "b", count: AppPicker.maxNameChars - 1) + pair),
        ]

        _ = makePicker().list()

        XCTAssertEqual(
            lines(),
            [
                "1.0\t" + String(repeating: "a", count: AppPicker.maxNameChars),
                "1.1\t" + String(repeating: "b", count: AppPicker.maxNameChars - 1),
            ])
    }

    func testANameThatFitsKeepsItsWholeCharacters() {
        let pair = "\u{1F600}"
        catalog.apps = [app(String(repeating: "c", count: AppPicker.maxNameChars - 2) + pair)]

        _ = makePicker().list()

        XCTAssertEqual(lines(), ["1.0\t" + String(repeating: "c", count: AppPicker.maxNameChars - 2) + pair])
    }

    func testANameCannotBreakARow() {
        catalog.apps = [app("One\ttwo\nthree\r"), app(" \t ")]

        _ = makePicker().list()

        XCTAssertEqual(lines(), ["1.0\tOne two three"])
    }

    func testTwoAppsWithTheSameNameKeepTheScansOrder() {
        catalog.apps = [
            AppEntry(name: "Same", target: "/one.app"),
            AppEntry(name: "SAME", target: "/two.app"),
            AppEntry(name: "same", target: "/three.app"),
        ]
        let picker = makePicker()

        _ = picker.list()

        XCTAssertEqual(lines(), ["1.0\tSame", "1.1\tSAME", "1.2\tsame"])
        XCTAssertEqual(picker.add("1.2"), .added)
        XCTAssertEqual(store.macros.map(\.target), ["/three.app"])
    }

    func testAScanThatFailsStillAnswersThePhone() {
        catalog.scanThrows = true

        XCTAssertTrue(makePicker().list())

        XCTAssertEqual(lines(), [])
        XCTAssertEqual(wire.filter { $0.listBegin }.count, 1)
        XCTAssertEqual(ends(), 1)
    }

    func testEachIconIsLabelledWithItsTokenAndPaced() {
        catalog.apps = [app("One"), app("Two")]
        catalog.iconFor = { _ in [UInt8](repeating: 1, count: 10) }

        _ = makePicker().list()

        XCTAssertEqual(iconBegins().map { $0.label }, ["1.0", "1.1"])
        // One wait between the two, and none after the last: there is nothing left to hold back.
        XCTAssertEqual(ticks, [BlobSender.tick])
    }

    func testAnIconThatIsMissingTooBigOrFailingCostsOnlyThatIcon() {
        catalog.apps = [app("A"), app("B"), app("C"), app("D"), app("E")]
        catalog.iconFor = { app in
            switch app.name {
            case "A":
                return nil
            case "B":
                return [UInt8](repeating: 1, count: AppPicker.maxIconBytes + 1)
            case "C":
                throw FakeCatalog.Gone()
            case "D":
                return [UInt8](repeating: 1, count: AppPicker.maxIconBytes)
            default:
                return []
            }
        }

        _ = makePicker().list()

        let icons = iconBegins()
        XCTAssertEqual(icons.map { $0.label }, ["1.3"])
        XCTAssertEqual(icons.first?.size, UInt32(AppPicker.maxIconBytes))
    }

    func testANewListStopsTheOldListsIcons() {
        let picker = makePicker()
        catalog.apps = [app("One"), app("Two")]
        catalog.iconFor = { [unowned self] app in
            if app.name == "One", self.catalog.scans == 1 {
                _ = picker.list()
            }
            return [1]
        }

        _ = picker.list()

        // Two lists and the second's two icons. Nothing of the first's icons went, and the second's came in order.
        XCTAssertEqual(ends(), 4)
        XCTAssertEqual(iconBegins().map { $0.label }, ["2.0", "2.1"])
    }

    func testCancelStopsTheIconsAndLeavesTheListUsable() {
        let picker = makePicker()
        catalog.apps = [app("One"), app("Two")]
        catalog.iconFor = { _ in
            _ = picker.cancel()
            return [1]
        }

        _ = picker.list()

        XCTAssertEqual(ends(), 1)
        XCTAssertEqual(iconBegins().count, 0)
        XCTAssertEqual(picker.add("1.1"), .added)
    }

    func testACancelDuringTheScanSendsNothing() {
        var work: [() -> Void] = []
        let picker = AppPicker(
            catalog: catalog, store: store, sender: sender(), schedule: { work.append($0) },
            scheduleAfter: { _, run in run() })
        catalog.apps = [app("One")]

        _ = picker.list()
        _ = picker.cancel()
        work[0]()

        XCTAssertEqual(wire, [])
        XCTAssertEqual(catalog.scans, 0)
        XCTAssertEqual(picker.add("1.0"), .stale)
    }

    func testTheEndOfTheSessionStopsTheIconsOnTheirWay() {
        var held: [() -> Void] = []
        let picker = AppPicker(
            catalog: catalog, store: store, sender: sender(), schedule: { $0() },
            scheduleAfter: { _, run in held.append(run) })
        catalog.apps = [app("One"), app("Two")]
        catalog.iconFor = { _ in [1] }
        _ = picker.list()
        XCTAssertEqual(iconBegins().map { $0.label }, ["1.0"])

        picker.end()
        for tick in held {
            tick()
        }

        XCTAssertEqual(iconBegins().map { $0.label }, ["1.0"])
        XCTAssertEqual(picker.add("1.0"), .stale)
    }

    func testATokenThatNamesNoRowChangesNothing() throws {
        let tokens = [
            "", ".", "1", "1.", ".0", "abc", "1.x", "x.0", "1.-1", "-1.0", "+1.0", "1.+0", "1.1", "1.99",
            "1.99999999999999999999", "99999999999999999999.0", "0.0", "2.0", "1.0.0", " 1.0", "1.0 ", "1.0\n",
            "\u{FF11}.\u{FF10}", "C:\\Windows\\System32\\cmd.exe", "/usr/bin/sh", "../../bin/sh",
        ]
        store.save([Macro(name: "Kept", target: "kept.app")])
        let before = try fileBytes()

        for token in tokens {
            let picker = makePicker()
            catalog.apps = [app("One")]
            _ = picker.list()

            XCTAssertEqual(picker.add(token), .stale, "token \(token)")

            XCTAssertEqual(try fileBytes(), before, "token \(token)")
            XCTAssertEqual(store.macros.map(\.name), ["Kept"], "token \(token)")
        }
    }

    func testNoTokenWorksBeforeAList() {
        let picker = makePicker()

        XCTAssertEqual(picker.add("0.0"), .stale)
        XCTAssertEqual(picker.add("1.0"), .stale)
        XCTAssertFalse(FileManager.default.fileExists(atPath: macrosPath))
    }

    func testATokenFromTheLastEpochIsStale() {
        let picker = makePicker()
        catalog.apps = [app("One")]
        _ = picker.list()
        _ = picker.list()

        XCTAssertEqual(picker.add("1.0"), .stale)
        XCTAssertFalse(FileManager.default.fileExists(atPath: macrosPath))
        XCTAssertEqual(picker.add("2.0"), .added)
    }

    func testNoTokenWorksWhileTheNewListIsBeingScanned() {
        var work: [() -> Void] = []
        let picker = AppPicker(
            catalog: catalog, store: store, sender: sender(), schedule: { work.append($0) },
            scheduleAfter: { _, run in run() })
        catalog.apps = [app("One")]

        _ = picker.list()
        work[0]()
        _ = picker.list()

        XCTAssertEqual(picker.add("1.0"), .stale)
        XCTAssertEqual(picker.add("2.0"), .stale)
    }

    func testAFullListStaysAsItIs() throws {
        store.save(macros(MacroStore.maxMacros))
        let before = try fileBytes()
        let picker = makePicker()
        catalog.apps = [app("One")]
        _ = picker.list()

        XCTAssertEqual(picker.add("1.0"), .full)

        XCTAssertEqual(try fileBytes(), before)
    }

    func testTheLastFreeSlotCanBeFilled() {
        store.save(macros(MacroStore.maxMacros - 1))
        let picker = makePicker()
        catalog.apps = [app("One")]
        _ = picker.list()

        XCTAssertEqual(picker.add("1.0"), .added)

        XCTAssertEqual(store.macros.count, MacroStore.maxMacros)
    }

    func testATargetAlreadyInTheListIsADuplicateWhateverItsCase() throws {
        store.save([Macro(name: "Kept", target: "/Apps/Tool.APP", arguments: "-X")])
        let before = try fileBytes()
        let picker = makePicker()
        catalog.apps = [AppEntry(name: "Tool", target: "/apps/tool.app", arguments: "-x")]
        _ = picker.list()

        XCTAssertEqual(picker.add("1.0"), .duplicate)

        XCTAssertEqual(try fileBytes(), before)
    }

    func testTheSameTargetWithOtherArgumentsIsAnotherMacro() {
        store.save([Macro(name: "Kept", target: "tool.app", arguments: "-a")])
        let picker = makePicker()
        catalog.apps = [AppEntry(name: "Tool", target: "tool.app")]
        _ = picker.list()

        XCTAssertEqual(picker.add("1.0"), .added)
    }

    func testAWriteThatFailsIsReportedAndChangesNothing() throws {
        let picker = makePicker()
        catalog.apps = [app("One")]
        _ = picker.list()
        // A file where the folder should be, so nothing can be created under it.
        try "in the way".write(to: directory, atomically: true, encoding: .utf8)

        XCTAssertEqual(picker.add("1.0"), .failed)

        XCTAssertEqual(store.macros, [])
    }

    func testAnEntryWithNothingToOpenFails() {
        let picker = makePicker()
        catalog.apps = [AppEntry(name: "Blank", target: "  ")]
        _ = picker.list()

        XCTAssertEqual(picker.add("1.0"), .failed)
        XCTAssertFalse(FileManager.default.fileExists(atPath: macrosPath))
    }

    func testAnAddedAppPersistsAndIsAnnouncedToThePhone() {
        let picker = makePicker()
        catalog.apps = [
            AppEntry(name: "Visual Studio Code Insiders", target: "code.app", arguments: "--new", iconPath: "code.icns")
        ]
        _ = picker.list()
        var seen: [String] = []
        let watch = store.watch { seen.append($0) }
        defer { watch.cancel() }

        XCTAssertEqual(picker.add("1.0"), .added)

        let expected = Macro(name: "Visual Studio Co", target: "code.app", arguments: "--new", icon: "code.icns")
        XCTAssertEqual(store.macros, [expected])
        XCTAssertEqual(MacroStore(path: macrosPath).macros, [expected])
        XCTAssertEqual(seen, ["", "Visual Studio Co"])
        XCTAssertEqual(launcher.launched, [])
    }
}
