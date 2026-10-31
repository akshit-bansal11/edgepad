import EdgepadCore
import Foundation

/// The folders on this Mac's disk, as `AppBundles` walks them. A link to a folder counts as a folder, so an app linked
/// into /Applications from another volume is found.
final class DiskFolders: AppFolders {
    func entries(in path: String) -> [AppFolderEntry]? {
        let manager = FileManager.default
        guard let names = try? manager.contentsOfDirectory(atPath: path) else { return nil }
        return names.map { name in
            var isFolder: ObjCBool = false
            let exists = manager.fileExists(atPath: path + "/" + name, isDirectory: &isFolder)
            return AppFolderEntry(name: name, isFolder: exists && isFolder.boolValue)
        }
    }
}

/// The apps the phone may pick from: the `.app` bundles in this Mac's application folders. The rules for which and what
/// they are called are `AppBundles`, tested on literal listings; this adds only the disk, each bundle's Info.plist and
/// the picture. A pick's macro target is the bundle's own path, which `WorkspaceMacroLauncher` opens, so the toggle
/// that follows a macro's app follows it too.
///
/// Main thread only: the icons are drawn with AppKit, through the same cache the macro grid uses.
final class ApplicationsCatalog: AppCatalog {
    /// The side of an icon on the phone's list. The list is many small pictures, not a grid of big buttons.
    static let iconSize = 64

    private let icons: MacroIconSource
    private let roots: [String]
    private let folders: AppFolders

    init(
        icons: MacroIconSource,
        roots: [String] = AppBundles.roots(home: NSHomeDirectory()),
        folders: AppFolders = DiskFolders()
    ) {
        self.icons = icons
        self.roots = roots
        self.folders = folders
    }

    func scan() throws -> [AppEntry] {
        AppBundles.scan(
            roots: roots,
            folders: folders,
            names: { path in
                let bundle = Bundle(path: path)
                return (
                    display: bundle?.object(forInfoDictionaryKey: "CFBundleDisplayName") as? String,
                    bundle: bundle?.object(forInfoDictionaryKey: "CFBundleName") as? String
                )
            },
            canonical: { MacroTarget.canonical(URL(fileURLWithPath: $0)) })
    }

    func icon(for app: AppEntry) throws -> [UInt8]? {
        let macro = Macro(name: app.name, target: app.target, arguments: app.arguments, icon: app.iconPath)
        return icons.png(for: macro, size: ApplicationsCatalog.iconSize)
    }
}
