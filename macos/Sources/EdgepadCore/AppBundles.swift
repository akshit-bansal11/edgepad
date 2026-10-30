/// Which `.app` bundles this Mac offers the phone as macros, and what each is called. The rules are here, away from
/// `FileManager`, so they are tests over literal listings; the app's `ApplicationsCatalog` only supplies the folders,
/// the names from each bundle's Info.plist and the icons.
public enum AppBundles {
    /// The folders apps live in, in the order they win a duplicate. The user's own comes last: it is the one place an
    /// app can be put without an administrator, so a copy there never hides the system's.
    public static func roots(home: String) -> [String] {
        let user = home.hasSuffix("/") ? String(home.dropLast()) : home
        return [
            "/Applications",
            "/System/Applications",
            "/Applications/Utilities",
            "/System/Applications/Utilities",
            user + "/Applications",
        ]
    }

    /// A folder name that is an application bundle: ends in `.app`, whatever its case, has a name before that, and is
    /// not hidden.
    public static func isBundleName(_ name: String) -> Bool {
        name.count > 4 && !name.hasPrefix(".") && name.lowercased().hasSuffix(".app")
    }

    /// Whether `path` lies inside another bundle, such as a helper app in `Foo.app/Contents/Library`. Those are parts
    /// of an app, not apps to put on a button.
    public static func isNested(_ path: String) -> Bool {
        path.split(separator: "/").dropLast().contains { isBundleName(String($0)) }
    }

    /// The name the phone shows: the bundle's display name, else its name, else the file's name without `.app`. Blank
    /// ones count as missing, since a button with no label is worse than a dull one.
    public static func name(fileName: String, displayName: String?, bundleName: String?) -> String {
        for candidate in [displayName, bundleName] {
            if let text = candidate?.trimmingCharacters(in: .whitespacesAndNewlines), !text.isEmpty {
                return text
            }
        }
        return isBundleName(fileName) ? String(fileName.dropLast(4)) : fileName
    }

    /// The `.app` bundles directly in each root and one folder level below it, never inside another bundle, once each.
    /// A bundle is not entered, so a helper app in its Contents is never met, and a loop of links cannot go on:
    /// nothing is read deeper than the second level. `canonical` names the file a path really is, so a bundle reached
    /// through two links counts once; the first path found is the one kept.
    public static func bundlePaths(
        roots: [String], folders: AppFolders, canonical: (String) -> String = { $0 }
    ) -> [String] {
        var seen = Set<String>()
        var found: [String] = []

        func take(_ path: String) {
            if !isNested(path), seen.insert(canonical(path)).inserted {
                found.append(path)
            }
        }

        for root in roots {
            for entry in sorted(folders.entries(in: root)) where entry.isFolder && !entry.name.hasPrefix(".") {
                let path = join(root, entry.name)
                if isBundleName(entry.name) {
                    take(path)
                    continue
                }
                for inner in sorted(folders.entries(in: path)) where inner.isFolder && isBundleName(inner.name) {
                    take(join(path, inner.name))
                }
            }
        }
        return found
    }

    /// The apps on offer. The macro's target is the bundle's own path, which `MacroLauncher` opens and the
    /// app-following toggle recognises. `names` reads the bundle at a path for its `CFBundleDisplayName` and
    /// `CFBundleName`.
    public static func scan(
        roots: [String],
        folders: AppFolders,
        names: (String) -> (display: String?, bundle: String?),
        canonical: (String) -> String = { $0 }
    ) -> [AppEntry] {
        bundlePaths(roots: roots, folders: folders, canonical: canonical).map { path in
            let read = names(path)
            let file = path.split(separator: "/").last.map(String.init) ?? path
            let shown = name(fileName: file, displayName: read.display, bundleName: read.bundle)
            return AppEntry(name: shown, target: path)
        }
    }

    /// Sorted by name, so which of two links to one bundle is kept does not depend on the order the disk lists them.
    private static func sorted(_ entries: [AppFolderEntry]?) -> [AppFolderEntry] {
        (entries ?? []).sorted { $0.name < $1.name }
    }

    private static func join(_ folder: String, _ name: String) -> String {
        folder.hasSuffix("/") ? folder + name : folder + "/" + name
    }
}
