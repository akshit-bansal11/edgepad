import XCTest

@testable import EdgepadCore

/// The sanitising is what is worth pinning: a name that kept a "/" or a tab would split into two on the phone, and
/// every button after it would run the wrong macro. Nothing here launches anything real.
final class MacroStoreTests: XCTestCase {
    private let directory = URL(fileURLWithPath: NSTemporaryDirectory())
        .appendingPathComponent("edgepad-tests-" + UUID().uuidString)

    private var macrosPath: String {
        directory.appendingPathComponent("macros.txt").path
    }

    override func tearDown() {
        try? FileManager.default.removeItem(at: directory)
        super.tearDown()
    }

    private func newStore(launcher: MacroLauncher? = nil, icons: MacroIconSource? = nil) -> MacroStore {
        MacroStore(path: macrosPath, launcher: launcher, icons: icons, schedule: { $0() })
    }

    private func storeWith(_ windows: FakeWindows, launcher: FakeLauncher = FakeLauncher()) -> MacroStore {
        let store = MacroStore(
            path: macrosPath, launcher: launcher, windows: windows, schedule: { $0() }, delay: { _, work in work() })
        store.save([Macro(name: "One", target: "one.app"), Macro(name: "Two", target: "two.app", arguments: "-x")])
        return store
    }

    private func writeFile(_ lines: [String]) throws {
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        try lines.map { $0 + "\n" }.joined().write(toFile: macrosPath, atomically: true, encoding: .utf8)
    }

    private func fileLines() throws -> [String] {
        let text = try String(contentsOfFile: macrosPath, encoding: .utf8)
        return text.split(separator: "\n", omittingEmptySubsequences: false).dropLast().map(String.init)
    }

    func testAFailedWriteIsReportedAndLeavesTheListAsItWas() throws {
        let store = newStore()
        XCTAssertNil(store.save([Macro(name: "Mail", target: "Mail.app")]))
        try FileManager.default.removeItem(at: directory)
        // A file where the folder should be, so nothing can be created under it.
        try "in the way".write(to: directory, atomically: true, encoding: .utf8)

        XCTAssertNotNil(store.save([Macro(name: "Code", target: "Code.app")]))
        XCTAssertEqual(store.names(), "Mail")
    }

    func testNamesAreTheButtonLabelsJoinedForOneTextFrame() {
        let store = newStore()
        store.save([Macro(name: "Mail", target: "https://mail.google.com"), Macro(name: "Code", target: "Code.app")])

        XCTAssertEqual(store.names(), "Mail/Code")
    }

    func testANameCannotKeepWhatSeparatesOneNameFromTheNext() {
        let store = newStore()
        store.save([Macro(name: "In/box\tnow", target: "Mail.app")])

        XCTAssertEqual(store.macros[0].name, "In-box now")
        XCTAssertEqual(store.names(), "In-box now")
    }

    func testALongNameIsCutToWhatAPhoneButtonCanShow() {
        let store = newStore()
        store.save([Macro(name: "Visual Studio Code Insiders", target: "Code.app")])

        XCTAssertEqual(store.macros[0].name, "Visual Studio Co")
    }

    func testABlankNameKeepsItsSlotUnderADullLabel() {
        let store = newStore()
        store.save([Macro(name: "  ", target: "one.app"), Macro(name: "Two", target: "two.app")])

        XCTAssertEqual(store.names(), "Macro/Two")
    }

    func testAMacroWithNothingToLaunchIsDroppedRatherThanTakingAnIndex() {
        let store = newStore()
        let first = Macro(name: "First", target: "one.app")
        let blank = Macro(name: "Blank", target: "  ")
        let last = Macro(name: "Last", target: "two.app")
        store.save([first, blank, last])

        XCTAssertEqual(store.macros.count, 2)
        XCTAssertEqual(store.names(), "First/Last")
    }

    func testTheEditorIsToldWhichMacroHasNothingToOpen() {
        let one = Macro(name: "One", target: "one.app")
        let two = Macro(name: "Two", target: "two.app")
        let blank = Macro(name: "Blank", target: "  ")
        let tab = Macro(name: "Tab", target: "\t")
        let none = Macro(name: "", target: "")

        XCTAssertEqual(MacroStore.withoutTarget([one, two]), -1)
        XCTAssertEqual(MacroStore.withoutTarget([one, blank]), 1)
        XCTAssertEqual(MacroStore.withoutTarget([tab, none]), 0)
        XCTAssertEqual(MacroStore.withoutTarget([]), -1)
    }

    func testAMacroWithNoArgumentsHasNoneRatherThanAnEmptyCommandLine() {
        let store = newStore()
        store.save([Macro(name: "Files", target: "Finder.app", arguments: "   ")])

        XCTAssertNil(store.macros[0].arguments)
        XCTAssertNil(newStore().macros[0].arguments)
    }

    func testMacrosSurviveARestart() {
        let saved = Macro(name: "Code", target: "Code.app", arguments: "/Users/me/projects")
        newStore().save([saved])

        XCTAssertEqual(newStore().macros, [saved])
    }

    func testNoMoreThanTheCapIsKept() {
        let store = newStore()
        store.save((0..<(MacroStore.maxMacros + 8)).map { Macro(name: "M\($0)", target: "one.app") })

        XCTAssertEqual(store.macros.count, MacroStore.maxMacros)
        XCTAssertEqual(store.macros[0].name, "M0")
    }

    func testSavingTellsAWatcherTheNewNames() {
        let store = newStore()
        var seen: [String] = []
        let watch = store.watch { seen.append($0) }
        let chrome = Macro(name: "Chrome", target: "Chrome.app")

        store.save([chrome])
        store.save([chrome, Macro(name: "Notes", target: "Notes.app")])

        XCTAssertEqual(seen, ["", "Chrome", "Chrome/Notes"])
        watch.cancel()
    }

    func testACancelledWatchHearsNothingMore() {
        let store = newStore()
        var gone: [String] = []
        var kept: [String] = []
        let live = store.watch { kept.append($0) }
        store.watch { gone.append($0) }.cancel()

        store.save([Macro(name: "Chrome", target: "Chrome.app")])

        XCTAssertEqual(kept, ["", "Chrome"])
        XCTAssertEqual(gone, [""])
        live.cancel()
    }

    func testAFullGridOfLongestNamesStillFitsOneFrame() {
        // The whole reason the grid is fifteen. If this fails, either the grid grew or the name budget did, and
        // some macro is about to reach the phone as a blank button.
        let store = newStore()
        let full = String(repeating: "n", count: MacroStore.maxNameBytes)
        store.save((0..<MacroStore.maxMacros).map { _ in Macro(name: full, target: "one.app") })

        let names = store.names()
        let parts = names.split(separator: "/").map(String.init)

        XCTAssertEqual(parts.count, MacroStore.maxMacros)
        XCTAssertTrue(parts.allSatisfy { $0 == full })
        XCTAssertLessThanOrEqual(names.utf8.count, MacroStore.maxNamesBytes)
    }

    func testANameIsCutByItsBytesAndNeverThroughACharacter() {
        // Sixteen emoji are sixteen characters and sixty-four bytes. Four whole ones are the budget exactly.
        let store = newStore()
        store.save([Macro(name: String(repeating: "\u{1F600}", count: 16), target: "one.app")])

        let name = store.macros[0].name

        XCTAssertEqual(name.utf8.count, MacroStore.maxNameBytes)
        XCTAssertEqual(name.unicodeScalars.count, 4)
        XCTAssertFalse(String(decoding: Array(name.utf8), as: UTF8.self).contains("\u{FFFD}"))
    }

    func testAHandEditedFileIsHeldToTheSameRulesAsTheEditor() throws {
        try writeFile(["", "no tabs at all", "Way too long a name\tone.app", "  Spaced  \ttwo.app\t-x  "])

        let store = newStore()

        let cut = Macro(name: "Way too long a n", target: "one.app")
        let spaced = Macro(name: "Spaced", target: "two.app", arguments: "-x")
        XCTAssertEqual(store.macros, [cut, spaced])
    }

    func testAFileOfAThousandLinesIsStillCappedAtFifteen() throws {
        try writeFile((0..<1000).map { "M\($0)\tone.app" })

        XCTAssertEqual(newStore().macros.count, MacroStore.maxMacros)
    }

    func testReloadPicksUpAFileChangedUnderneathIt() throws {
        let store = newStore()
        store.save([Macro(name: "First", target: "one.app")])

        try writeFile(["Second\ttwo.app\t"])
        store.reload()

        XCTAssertEqual(store.names(), "Second")
    }

    func testAnUnreadableFileGivesAnEmptyListAndNeverAGuess() throws {
        // A folder where the file should be: present, and not readable as text. Failing closed means no macros,
        // not whatever an earlier read happened to see.
        try FileManager.default.createDirectory(atPath: macrosPath, withIntermediateDirectories: true)

        XCTAssertEqual(newStore().macros, [])
    }

    func testAMissingFileIsAnEmptyList() {
        XCTAssertEqual(newStore().macros, [])
        XCTAssertEqual(newStore().names(), "")
    }

    func testAnIndexOutsideTheListRunsNothing() {
        let launcher = FakeLauncher()
        let store = newStore(launcher: launcher)
        store.save([Macro(name: "Only", target: "one.app")])

        XCTAssertFalse(store.run(-1))
        XCTAssertFalse(store.run(1))
        XCTAssertEqual(launcher.launched, [])
    }

    func testARunHandsTheLauncherTheStoredTargetAndArgumentsOnly() {
        let launcher = FakeLauncher()
        let store = newStore(launcher: launcher)
        let code = Macro(name: "Code", target: "Code.app", arguments: "-n")
        store.save([code, Macro(name: "Mail", target: "Mail.app")])

        XCTAssertTrue(store.run(1))
        XCTAssertTrue(store.run(0))
        XCTAssertEqual(launcher.launched, ["Mail.app|-", "Code.app|-n"])
    }

    func testAFailedLaunchIsNotAnError() {
        let launcher = FakeLauncher()
        launcher.accepts = false
        let store = newStore(launcher: launcher)
        store.save([Macro(name: "Gone", target: "Gone.app")])

        XCTAssertTrue(store.run(0))
        XCTAssertEqual(launcher.launched, ["Gone.app|-"])
    }

    func testTheDefaultSchedulerDoesNotLaunchOnTheCallersThread() {
        let launcher = FakeLauncher()
        let store = MacroStore(path: macrosPath, launcher: launcher)
        store.save([Macro(name: "Only", target: "one.app")])

        XCTAssertTrue(store.run(0))
        XCTAssertEqual(launcher.launched, [])

        let ran = expectation(description: "the launch ran on the main queue")
        DispatchQueue.main.async { ran.fulfill() }
        wait(for: [ran], timeout: 5)
        XCTAssertEqual(launcher.launched, ["one.app|-"])
    }

    func testWithoutALauncherNothingRuns() {
        let store = newStore()
        store.save([Macro(name: "Only", target: "one.app")])

        XCTAssertFalse(store.run(0))
    }

    func testTheFileAndItsFolderArePrivateToTheUser() throws {
        newStore().save([Macro(name: "Code", target: "Code.app")])
        let manager = FileManager.default

        let folder = try manager.attributesOfItem(atPath: directory.path)[.posixPermissions] as? Int
        let file = try manager.attributesOfItem(atPath: macrosPath)[.posixPermissions] as? Int

        XCTAssertEqual(folder, 0o700)
        XCTAssertEqual(file, 0o600)
    }

    func testAnIconSurvivesARestart() {
        let saved = Macro(name: "Code", target: "Code.app", arguments: "/Users/me", icon: "/Users/me/code.png")
        newStore().save([saved])

        XCTAssertEqual(newStore().macros, [saved])
    }

    func testAMacroWithNoIconWritesTheSameLineThatWindowsDoes() throws {
        newStore().save([Macro(name: "Files", target: "Finder.app")])

        XCTAssertEqual(try fileLines(), ["Files\tFinder.app\t"])
    }

    func testAFileWrittenBeforeIconsExistedStillReads() throws {
        try writeFile(["Chrome\tchrome.app\t", "Notes\tnotes.app"])

        let store = newStore()

        let chrome = Macro(name: "Chrome", target: "chrome.app")
        let notes = Macro(name: "Notes", target: "notes.app")
        XCTAssertEqual(store.macros, [chrome, notes])
    }

    func testAnIconPathIsScrubbedLikeEveryOtherField() throws {
        let store = newStore()
        store.save([Macro(name: "Art", target: "one.app", icon: "  /a\tb.png  ")])

        XCTAssertEqual(store.macros[0].icon, "/a b.png")
        let fields = try fileLines()[0].split(separator: "\t", omittingEmptySubsequences: false)
        XCTAssertEqual(fields.count, 4)
    }

    func testAnIconThatIsOnlySpacesIsNoIcon() {
        let store = newStore()
        store.save([Macro(name: "Files", target: "Finder.app", icon: "   ")])

        XCTAssertNil(store.macros[0].icon)
        XCTAssertNil(newStore().macros[0].icon)
    }

    func testAnIconOverTheCapIsNotOffered() {
        let big = [UInt8](repeating: 1, count: MacroIconChunks.maxBytes(size: 128) + 1)
        let icons = FakeIcons(bytes: big)
        let store = newStore(icons: icons)
        store.save([Macro(name: "Big", target: "one.app")])

        XCTAssertNil(store.icon(at: 0))
        XCTAssertNil(store.icon(at: 5))
        XCTAssertNotNil(store.icon(at: 0, size: 256))
        XCTAssertEqual(icons.sizes, [128, 256])
    }

    func testSnapRoundsUpClampsAndDefaults() {
        let cases: [(String, Int, Int)] = [
            ("", 512, 128), ("abc", 512, 128), ("0", 512, 128), ("-5", 512, 128), ("96", 512, 128),
            ("64", 512, 64), ("65", 512, 128), ("200", 512, 256), ("300", 512, 384), ("512", 512, 512),
            ("9999", 512, 512), ("9999", 256, 256),
        ]
        for (payload, max, expected) in cases {
            XCTAssertEqual(MacroIconChunks.snap(payload, max: max), expected, "\(payload) max \(max)")
        }
    }

    func testTheChainDescendsFromTheRequestToSixtyFour() {
        XCTAssertEqual(MacroIconChunks.fallbackChain(size: 512), [512, 384, 256, 128, 64])
        XCTAssertEqual(MacroIconChunks.fallbackChain(size: 128), [128, 64])
        XCTAssertEqual(MacroIconChunks.fallbackChain(size: 64), [64])
    }

    func testTheByteBudgetGrowsWithTheSide() {
        XCTAssertEqual(MacroIconChunks.maxBytes(size: 128), 24 * 1024)
        XCTAssertEqual(MacroIconChunks.maxBytes(size: 64), 24 * 1024)
        XCTAssertEqual(MacroIconChunks.maxBytes(size: 512), 96 * 1024)
    }

    func testTheLargestIconStillHasAShortHeader() throws {
        let png = [UInt8](repeating: 9, count: MacroIconChunks.maxBytes(size: 512))

        let frames = MacroIconChunks.chunks(slot: 99, png: png)

        XCTAssertLessThan(frames.count, 1000)
        for frame in frames {
            let body = try XCTUnwrap(frame.split(separator: "/", maxSplits: 3, omittingEmptySubsequences: false).last)
            XCTAssertLessThanOrEqual(frame.utf8.count - body.utf8.count, 11)
            XCTAssertLessThanOrEqual(frame.utf8.count, FrameCodec.maxTextBytes)
        }
    }

    func testChunksRoundTripAndEachFitsOneFrame() throws {
        let png = (0..<1000).map { UInt8($0 % 251) }

        let frames = MacroIconChunks.chunks(slot: 7, png: png)

        var joined = ""
        for (index, frame) in frames.enumerated() {
            let parts = frame.split(separator: "/", maxSplits: 3, omittingEmptySubsequences: false)
            XCTAssertEqual(parts[0], "7")
            XCTAssertEqual(parts[1], "\(index)")
            XCTAssertEqual(parts[2], "\(frames.count)")
            XCTAssertLessThanOrEqual(frame.utf8.count, FrameCodec.maxTextBytes)
            joined += parts[3]
        }
        XCTAssertEqual([UInt8](try XCTUnwrap(Data(base64Encoded: joined))), png)
    }

    func testNoBytesMeansNoFramesRatherThanOneEmptyOne() {
        XCTAssertEqual(MacroIconChunks.chunks(slot: 0, png: []), [])
    }

    // The toggle: the cases of the core's MacroStoreTests, against a fake desktop.

    func testAnAppInFrontIsMinimizedAndNeverLaunched() {
        let windows = FakeWindows()
        windows.state = { _ in .front }
        let launcher = FakeLauncher()

        XCTAssertTrue(storeWith(windows, launcher: launcher).run(1))

        XCTAssertEqual(windows.calls, ["minimize Two"])
        XCTAssertEqual(launcher.launched, [])
    }

    func testAnAppBehindOthersIsRaisedAndNeverLaunched() {
        let windows = FakeWindows()
        windows.state = { _ in .open }
        let launcher = FakeLauncher()

        XCTAssertTrue(storeWith(windows, launcher: launcher).run(0))

        XCTAssertEqual(windows.calls, ["raise One"])
        XCTAssertEqual(launcher.launched, [])
    }

    func testAClosedAppLaunches() {
        let windows = FakeWindows()
        let launcher = FakeLauncher()

        XCTAssertTrue(storeWith(windows, launcher: launcher).run(1))

        XCTAssertEqual(launcher.launched, ["two.app|-x"])
        XCTAssertEqual(windows.calls, [])
    }

    func testAnUntrackedTargetLaunchesAndIsNotFollowed() {
        let windows = FakeWindows()
        windows.state = { _ in .untracked }
        let launcher = FakeLauncher()

        XCTAssertTrue(storeWith(windows, launcher: launcher).run(0))

        XCTAssertEqual(launcher.launched, ["one.app|-"])
        XCTAssertEqual(windows.surveys, 1)
    }

    func testAMacThatCannotSeeAppsNeverSurveysAndLaunches() {
        let windows = FakeWindows()
        windows.available = false
        windows.state = { _ in .front }
        let launcher = FakeLauncher()

        XCTAssertTrue(storeWith(windows, launcher: launcher).run(0))

        XCTAssertEqual(windows.surveys, 0)
        XCTAssertEqual(launcher.launched, ["one.app|-"])
    }

    func testAMinimizeThatDidNotActFallsThroughToALaunch() {
        let windows = FakeWindows()
        windows.state = { _ in .front }
        windows.acts = false
        let launcher = FakeLauncher()

        XCTAssertTrue(storeWith(windows, launcher: launcher).run(0))

        XCTAssertEqual(windows.calls, ["minimize One"])
        XCTAssertEqual(launcher.launched, ["one.app|-"])
    }

    func testARaiseThatDidNotActFallsThroughToALaunch() {
        let windows = FakeWindows()
        windows.state = { _ in .open }
        windows.acts = false
        let launcher = FakeLauncher()

        XCTAssertTrue(storeWith(windows, launcher: launcher).run(0))

        XCTAssertEqual(windows.calls, ["raise One"])
        XCTAssertEqual(launcher.launched, ["one.app|-"])
    }

    func testALaunchFromClosedRaisesAnAppThatComesUpBehind() {
        let windows = FakeWindows()
        var surveys = 0
        windows.state = { _ in
            surveys += 1
            return surveys == 1 ? .closed : .open
        }
        let launcher = FakeLauncher()

        XCTAssertTrue(storeWith(windows, launcher: launcher).run(0))

        XCTAssertEqual(launcher.launched, ["one.app|-"])
        XCTAssertEqual(windows.calls, ["raise One"])
    }

    func testALaunchThatFailedIsNotFollowed() {
        let windows = FakeWindows()
        let launcher = FakeLauncher()
        launcher.accepts = false

        XCTAssertTrue(storeWith(windows, launcher: launcher).run(0))

        XCTAssertEqual(windows.surveys, 1)
    }

    func testCloseActsOnTheMacroInThatSlotAndLaunchesNothing() {
        let windows = FakeWindows()
        let launcher = FakeLauncher()

        XCTAssertTrue(storeWith(windows, launcher: launcher).close(1))

        XCTAssertEqual(windows.calls, ["close Two"])
        XCTAssertEqual(launcher.launched, [])
    }

    func testCloseRefusesABadSlotAndAMacWithoutWindows() {
        let windows = FakeWindows()
        let store = storeWith(windows)
        let blind = FakeWindows()
        blind.available = false

        XCTAssertFalse(store.close(-1))
        XCTAssertFalse(store.close(2))
        XCTAssertFalse(storeWith(blind).close(0))
        XCTAssertFalse(newStore().close(0))
        XCTAssertEqual(windows.calls, [])
        XCTAssertEqual(blind.calls, [])
    }

    func testOpenStatesAreOneWireCharacterPerSlot() {
        let windows = FakeWindows()
        let store = storeWith(windows)
        let states: [String: OpenState] = ["One": .untracked, "Two": .closed, "Three": .open, "Four": .front]
        windows.state = { states[$0.name] ?? .closed }
        store.save(["One", "Two", "Three", "Four"].map { Macro(name: $0, target: $0 + ".app") })

        XCTAssertEqual(store.openStates(), "-012")
    }

    func testOnlyAnAppBundleWithNoArgumentsIsFollowed() {
        let folder: (String) -> Bool = { _ in true }

        XCTAssertEqual(
            MacroTarget.bundlePath(of: Macro(name: "Code", target: "/Applications/Code.app"), isFolder: folder),
            "/Applications/Code.app")
        XCTAssertNil(
            MacroTarget.bundlePath(
                of: Macro(name: "Code", target: "/Applications/Code.app", arguments: "~/work"), isFolder: folder))
        XCTAssertNil(MacroTarget.bundlePath(of: Macro(name: "Work", target: "/Users/me/work"), isFolder: folder))
        XCTAssertNil(
            MacroTarget.bundlePath(of: Macro(name: "Code", target: "/Applications/Code.app"), isFolder: { _ in false }))
    }

    func testOnlyAMacThatCanSeeAppsSaysSo() {
        let blind = FakeWindows()
        blind.available = false

        XCTAssertTrue(storeWith(FakeWindows()).seesApps)
        XCTAssertFalse(storeWith(blind).seesApps)
        XCTAssertFalse(newStore().seesApps)
        XCTAssertEqual(newStore().openStates(), "")
    }

    func testWindowActionsNeverTouchTheFile() throws {
        let windows = FakeWindows()
        windows.state = { _ in .front }
        let store = storeWith(windows)
        let before = try fileLines()

        _ = store.run(0)
        windows.state = { _ in .open }
        _ = store.run(1)
        windows.state = { _ in .closed }
        _ = store.run(0)
        _ = store.close(1)

        XCTAssertEqual(try fileLines(), before)
    }

    func testAnAppThatComesUpInFrontIsNeverRaised() {
        var states: [OpenState] = [.closed, .front]
        var raises = 0
        var logged: [String] = []

        MacroLaunchFollow.run(
            survey: { states.removeFirst() },
            raise: {
                raises += 1
                return true
            },
            delay: { _, work in work() },
            log: { logged.append($0) })

        XCTAssertEqual(raises, 0)
        XCTAssertEqual(logged.count, 1)
        XCTAssertTrue(logged[0].contains("in front: true, raised: false"))
    }

    func testAnAppThatComesUpBehindIsRaisedOnce() {
        var states: [OpenState] = [.closed, .closed, .open, .open]
        var raises = 0
        var logged: [String] = []

        MacroLaunchFollow.run(
            survey: { states.removeFirst() },
            raise: {
                raises += 1
                return true
            },
            delay: { _, work in work() },
            log: { logged.append($0) })

        XCTAssertEqual(raises, 1)
        XCTAssertEqual(logged.count, 1)
        XCTAssertTrue(logged[0].contains("in front: false, raised: true"))
    }

    func testAnAppThatNeverComesUpEndsTheFollowAndSaysSo() {
        var delays = 0
        var logged: [String] = []

        MacroLaunchFollow.run(
            survey: { .closed },
            raise: { true },
            delay: { _, work in
                delays += 1
                work()
            },
            log: { logged.append($0) })

        XCTAssertGreaterThan(delays, 0)
        XCTAssertEqual(logged.count, 1)
        XCTAssertTrue(logged[0].contains("not running"))
    }

    func testTryAddAppendsThroughTheSameCleaningAsSaveAndTellsTheWatchers() throws {
        let store = newStore()
        store.save([Macro(name: "Mail", target: "Mail.app")])
        var seen: [String] = []
        let watch = store.watch { seen.append($0) }
        defer { watch.cancel() }

        XCTAssertEqual(store.tryAdd(Macro(name: "In/box\tnow", target: " Code.app ", arguments: "")), .added)

        XCTAssertEqual(store.macros.map(\.name), ["Mail", "In-box now"])
        XCTAssertEqual(store.macros[1].target, "Code.app")
        XCTAssertNil(store.macros[1].arguments)
        XCTAssertEqual(try fileLines(), ["Mail\tMail.app\t", "In-box now\tCode.app\t"])
        XCTAssertEqual(seen, ["Mail", "Mail/In-box now"])
    }

    func testTryAddRefusesAFullListADuplicateAndABlankTargetWithoutTouchingTheFile() throws {
        let store = newStore()
        store.save((0..<MacroStore.maxMacros).map { Macro(name: "M\($0)", target: "m\($0).app") })
        let before = try fileLines()

        XCTAssertEqual(store.tryAdd(Macro(name: "New", target: "new.app")), .full)
        store.save([Macro(name: "One", target: "One.APP", arguments: "-x")])
        XCTAssertEqual(store.tryAdd(Macro(name: "Again", target: "one.app", arguments: "-X")), .duplicate)
        XCTAssertEqual(store.tryAdd(Macro(name: "Blank", target: " \t ")), .failed)

        XCTAssertEqual(before.count, MacroStore.maxMacros)
        XCTAssertEqual(try fileLines(), ["One\tOne.APP\t-x"])
    }

    func testTryAddReportsAFailedWriteAndLeavesTheListAsItWas() throws {
        let store = newStore()
        store.save([Macro(name: "Mail", target: "Mail.app")])
        try FileManager.default.removeItem(at: directory)
        try "in the way".write(to: directory, atomically: true, encoding: .utf8)

        XCTAssertEqual(store.tryAdd(Macro(name: "Code", target: "Code.app")), .failed)

        XCTAssertEqual(store.names(), "Mail")
    }
}
