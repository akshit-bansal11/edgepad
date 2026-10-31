import XCTest

@testable import EdgepadCore

/// Which bundles are offered and what they are called, on literal inputs: no disk, no Info.plist.
final class AppBundlesTests: XCTestCase {
    private func folder(_ name: String) -> AppFolderEntry {
        AppFolderEntry(name: name, isFolder: true)
    }

    private func file(_ name: String) -> AppFolderEntry {
        AppFolderEntry(name: name, isFolder: false)
    }

    func testTheRootsAreTheFiveApplicationFoldersWithTheUsersLast() {
        let expected = [
            "/Applications",
            "/System/Applications",
            "/Applications/Utilities",
            "/System/Applications/Utilities",
            "/Users/sam/Applications",
        ]

        XCTAssertEqual(AppBundles.roots(home: "/Users/sam"), expected)
        XCTAssertEqual(AppBundles.roots(home: "/Users/sam/"), expected)
    }

    func testABundleNameEndsInDotAppWhateverItsCaseAndIsNotHidden() {
        XCTAssertTrue(AppBundles.isBundleName("Safari.app"))
        XCTAssertTrue(AppBundles.isBundleName("Safari.APP"))
        XCTAssertTrue(AppBundles.isBundleName("A.app"))
        XCTAssertFalse(AppBundles.isBundleName(".app"))
        XCTAssertFalse(AppBundles.isBundleName(".Hidden.app"))
        XCTAssertFalse(AppBundles.isBundleName("app"))
        XCTAssertFalse(AppBundles.isBundleName("Safari"))
        XCTAssertFalse(AppBundles.isBundleName("Safari.app.zip"))
        XCTAssertFalse(AppBundles.isBundleName("Utilities"))
    }

    func testAPathInsideAnotherBundleIsNested() {
        XCTAssertTrue(AppBundles.isNested("/Applications/Foo.app/Contents/Library/Helper.app"))
        XCTAssertTrue(AppBundles.isNested("/Applications/Foo.APP/Helper.app"))
        XCTAssertFalse(AppBundles.isNested("/Applications/Foo.app"))
        XCTAssertFalse(AppBundles.isNested("/Applications/Adobe/Foo.app"))
        XCTAssertFalse(AppBundles.isNested("/Applications/Foo.app/"))
    }

    func testTheDisplayNameWinsThenTheBundleNameThenTheFileName() {
        XCTAssertEqual(AppBundles.name(fileName: "Mail.app", displayName: "Mail Pro", bundleName: "Mail"), "Mail Pro")
        XCTAssertEqual(AppBundles.name(fileName: "Mail.app", displayName: nil, bundleName: "Mail X"), "Mail X")
        XCTAssertEqual(AppBundles.name(fileName: "Mail.app", displayName: "  ", bundleName: ""), "Mail")
        XCTAssertEqual(AppBundles.name(fileName: "Mail.app", displayName: nil, bundleName: nil), "Mail")
        XCTAssertEqual(AppBundles.name(fileName: "Mail.APP", displayName: nil, bundleName: nil), "Mail")
        XCTAssertEqual(AppBundles.name(fileName: "Mail", displayName: nil, bundleName: nil), "Mail")
        XCTAssertEqual(AppBundles.name(fileName: "Mail.app", displayName: " Mail Pro \n", bundleName: nil), "Mail Pro")
    }

    func testBundlesAreFoundInTheRootAndOneFolderBelowItAndNowhereDeeper() {
        let folders = FakeFolders([
            "/Applications": [
                folder("Safari.app"), folder("Adobe"), file("readme.txt"), file("Fake.app"), folder(".Trash"),
                folder("Utilities"),
            ],
            "/Applications/Adobe": [folder("Photoshop.app"), folder("Deep"), file("note.app")],
            "/Applications/Adobe/Deep": [folder("TooDeep.app")],
            "/Applications/Utilities": [folder("Terminal.app")],
            "/Applications/.Trash": [folder("Gone.app")],
        ])

        let roots = ["/Applications", "/Applications/Utilities", "/Nowhere"]

        let found = AppBundles.bundlePaths(roots: roots, folders: folders)

        XCTAssertEqual(
            found,
            [
                "/Applications/Adobe/Photoshop.app",
                "/Applications/Safari.app",
                "/Applications/Utilities/Terminal.app",
            ])
        XCTAssertFalse(folders.reads.contains("/Applications/Adobe/Deep"))
        XCTAssertFalse(folders.reads.contains("/Applications/.Trash"))
    }

    func testABundleIsNeverEntered() {
        let folders = FakeFolders([
            "/Applications": [folder("Foo.app")],
            "/Applications/Foo.app": [folder("Helper.app"), folder("Contents")],
        ])

        let found = AppBundles.bundlePaths(roots: ["/Applications"], folders: folders)

        XCTAssertEqual(found, ["/Applications/Foo.app"])
        XCTAssertEqual(folders.reads, ["/Applications"])
    }

    func testABundleInsideAnotherBundleIsSkippedWhereverTheWalkMetIt() {
        let folders = FakeFolders(["/Applications/Foo.app/Contents/Library": [folder("Helper.app")]])

        let found = AppBundles.bundlePaths(roots: ["/Applications/Foo.app/Contents/Library"], folders: folders)

        XCTAssertEqual(found, [])
    }

    func testABundleReachedTwiceCountsOnceAndTheFirstPathIsKept() {
        let folders = FakeFolders(["/Applications": [folder("Real.app"), folder("Link.app")]])
        let canonical: (String) -> String = { $0 == "/Applications/Link.app" ? "/Applications/Real.app" : $0 }

        let found = AppBundles.bundlePaths(roots: ["/Applications"], folders: folders, canonical: canonical)

        XCTAssertEqual(found, ["/Applications/Link.app"])
    }

    func testAFolderThatCannotBeReadIsSkipped() {
        let folders = FakeFolders(["/Applications": [folder("Mail.app")]])

        let found = AppBundles.bundlePaths(roots: ["/Missing", "/Applications"], folders: folders)

        XCTAssertEqual(found, ["/Applications/Mail.app"])
    }

    func testEachAppIsNamedFromItsBundleAndTargetsItsOwnPath() {
        let folders = FakeFolders(["/Applications": [folder("Safari.app"), folder("Mail.app"), folder("Notes.app")]])
        let plists: [String: (display: String?, bundle: String?)] = [
            "/Applications/Safari.app": ("Safari Browser", "Safari"),
            "/Applications/Mail.app": (nil, "Mail X"),
        ]

        let apps = AppBundles.scan(
            roots: ["/Applications"], folders: folders, names: { plists[$0] ?? (nil, nil) })

        XCTAssertEqual(
            apps,
            [
                AppEntry(name: "Mail X", target: "/Applications/Mail.app"),
                AppEntry(name: "Notes", target: "/Applications/Notes.app"),
                AppEntry(name: "Safari Browser", target: "/Applications/Safari.app"),
            ])
    }
}
