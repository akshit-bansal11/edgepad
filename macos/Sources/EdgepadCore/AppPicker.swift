import Foundation

/// Lets the phone add a macro by picking from a list this Mac generated, and is the only path from phone text to the
/// `MacroStore`. The phone never supplies a target: a token is `epoch.index`, the rows it indexes stay on this side of
/// the link, and `add` has nothing else to go on. Each APPS_LIST starts a new epoch and only the latest is valid, so a
/// token that names no row of it is stale and changes nothing. One per session, so a list dies with it. The Swift
/// mirror of the shared core's `AppPicker`.
///
/// A list is scanned on a turn of its own, not inline with the frame that asked, and sent through the session's paced
/// sender. Its blob is one `token TAB name` line per app, sorted by name. An icon follows each app that has one,
/// labelled with its token, one per tick so a full list is not one burst. Main thread only, like everything in the
/// session. The scan alone runs on a background queue; everything else, and every result, is back on the main thread.
public final class AppPicker {
    public static let maxApps = 400
    public static let maxNameChars = 40
    public static let maxIconBytes = 8 * 1024

    /// The session's sender, which gets its link from the session like the clipboard's does.
    public let sender: BlobSender

    private let catalog: AppCatalog
    private let store: MacroStore
    private let schedule: (@escaping () -> Void) -> Void
    private let background: (@escaping () -> Void) -> Void
    private let scheduleAfter: (TimeInterval, @escaping () -> Void) -> Void

    /// The last epoch handed out, for the whole process: every picker draws from it, so a token from an earlier
    /// session or picker can never match a later one's list. Guarded, since a picker may be made off the main thread.
    private static let epochGate = NSLock()
    private static var lastEpoch = 0

    private static func nextEpoch() -> Int {
        epochGate.lock()
        defer { epochGate.unlock() }
        lastEpoch += 1
        return lastEpoch
    }

    /// Tests only: starts the process-wide count over so their tokens are predictable.
    static func resetEpochs() {
        epochGate.lock()
        defer { epochGate.unlock() }
        lastEpoch = 0
    }

    /// The rows of `epoch`. Empty until its scan is done, so no token works meanwhile.
    private var apps: [AppEntry] = []
    private var epoch = 0
    /// True while a scan runs off the main thread, so a second list never starts a second scan beside it.
    private var scanning = false
    /// The epoch still being sent, or 0 after a cancel or the session's end. A cancel stops the sending and leaves the
    /// list usable.
    private var sending = 0

    /// - Parameters:
    ///   - catalog: This Mac's apps.
    ///   - store: Where a picked app goes.
    ///   - sender: The session's sender.
    ///   - schedule: Runs a scan on a later turn of the main queue; tests pass `{ $0() }`.
    ///   - background: Runs the scan off the main thread; tests pass `{ $0() }`.
    ///   - scheduleAfter: Runs the next icon after a tick; tests hold the ticks, or pass one that runs at once.
    public init(
        catalog: AppCatalog,
        store: MacroStore,
        sender: BlobSender,
        schedule: @escaping (@escaping () -> Void) -> Void = { DispatchQueue.main.async(execute: $0) },
        background: @escaping (@escaping () -> Void) -> Void = {
            DispatchQueue.global(qos: .utility).async(execute: $0)
        },
        scheduleAfter: @escaping (TimeInterval, @escaping () -> Void) -> Void = { delay, work in
            DispatchQueue.main.asyncAfter(deadline: .now() + delay, execute: work)
        }
    ) {
        self.catalog = catalog
        self.store = store
        self.sender = sender
        self.schedule = schedule
        self.background = background
        self.scheduleAfter = scheduleAfter
    }

    /// APPS_LIST. Starts a new epoch, which also stops whatever the last one was still sending.
    public func list() -> Bool {
        epoch = AppPicker.nextEpoch()
        sending = epoch
        apps = []
        schedule { [weak self] in self?.beginScan() }
        return true
    }

    /// APPS_CANCEL. A row already sent can still be added: the phone may leave the list and the add follow it.
    public func cancel() -> Bool {
        sending = 0
        return true
    }

    /// The session ended: nothing more goes, and what the sender was holding is dropped.
    public func end() {
        sending = 0
        apps = []
        sender.end()
    }

    /// APP_ADD: adds the row `token` names. Anything that names no row of the latest list is stale and changes nothing.
    public func add(_ token: String) -> AppAddStatus {
        guard let named = AppPicker.parse(token), named.epoch == epoch, apps.indices.contains(named.index) else {
            return .stale
        }
        let app = apps[named.index]
        return store.tryAdd(Macro(name: app.name, target: app.target, arguments: app.arguments, icon: app.iconPath))
    }

    private static func token(_ epoch: Int, _ index: Int) -> String {
        "\(epoch).\(index)"
    }

    /// Digits, a dot, digits, and nothing else: no sign, no space, no second dot. `Int(_:)` alone would take "+1".
    private static func parse(_ token: String) -> (epoch: Int, index: Int)? {
        let parts = token.split(separator: ".", omittingEmptySubsequences: false)
        guard parts.count == 2, let epoch = number(parts[0]), let index = number(parts[1]) else { return nil }
        return (epoch, index)
    }

    private static func number(_ digits: Substring) -> Int? {
        guard !digits.isEmpty, digits.utf8.allSatisfy({ $0 >= UInt8(ascii: "0") && $0 <= UInt8(ascii: "9") }) else {
            return nil
        }
        return Int(digits)
    }

    /// The rows the phone is offered. Sorted before the cap, so the same Mac always loses the same apps.
    static func rows(_ scanned: [AppEntry]) -> [AppEntry] {
        let named = scanned.enumerated().compactMap { position, app -> (position: Int, key: String, app: AppEntry)? in
            let name = shortName(MacroStore.plain(app.name))
            guard !name.isEmpty else { return nil }
            let row = AppEntry(name: name, target: app.target, arguments: app.arguments, iconPath: app.iconPath)
            return (position, name.uppercased(), row)
        }
        let sorted = named.sorted { left, right in
            // Ties keep the scan's order, so a sort that is not stable cannot change which row has which token.
            left.key.utf16.elementsEqual(right.key.utf16)
                ? left.position < right.position
                : left.key.utf16.lexicographicallyPrecedes(right.key.utf16)
        }
        return sorted.prefix(maxApps).map { $0.app }
    }

    /// Cut on a whole character: half a surrogate pair is an invalid name, not a short one. Counted in UTF-16 units, as
    /// the shared core counts, so every laptop offers the same names.
    static func shortName(_ name: String) -> String {
        var kept = String.UnicodeScalarView()
        var units = 0
        for scalar in name.unicodeScalars {
            let width = String(scalar).utf16.count
            if units + width > maxNameChars {
                break
            }
            units += width
            kept.append(scalar)
        }
        var cut = String(kept)
        while let last = cut.last, last.isWhitespace {
            cut.removeLast()
        }
        return cut
    }

    static func listBytes(_ epoch: Int, _ rows: [AppEntry]) -> [UInt8] {
        var text = ""
        for (index, row) in rows.enumerated() {
            text += token(epoch, index) + "\t" + row.name + "\n"
        }
        return Array(text.utf8)
    }

    /// Starts the scan of the list now wanted, off the main thread, unless a cancel came or one is already running:
    /// that one hands over to a newer list when it ends, so two scans never run at once.
    private func beginScan() {
        // A cancel since this turn was queued: the scan is the dear part, so it is not started.
        guard sending != 0, !scanning else { return }
        scanning = true
        let current = sending
        let catalog = catalog
        let deliver = schedule
        background { [weak self] in
            let rows = AppPicker.rows(AppPicker.scan(catalog))
            deliver { [weak self] in self?.scanned(current, rows) }
        }
    }

    /// The scan is done, back on the main thread.
    private func scanned(_ current: Int, _ rows: [AppEntry]) {
        scanning = false
        guard sending == current else {
            // Asked again meanwhile: that list's scan was held back, and starts now.
            if sending != 0 {
                beginScan()
            }
            return
        }
        apps = rows

        let abandoned: () -> Bool = { [weak self] in
            guard let self else { return true }
            return self.sending != current
        }
        sender.send(.appList, label: "", data: AppPicker.listBytes(current, rows), abandoned: abandoned) {
            [weak self] sent in
            if sent {
                self?.sendIcons(current, rows, from: 0, abandoned)
            }
        }
    }

    /// The icons from `first` on, one blob per tick. An app with none is skipped without a tick, since nothing went.
    private func sendIcons(_ current: Int, _ rows: [AppEntry], from first: Int, _ abandoned: @escaping () -> Bool) {
        var index = first
        while index < rows.count, !abandoned() {
            let row = rows[index]
            let label = AppPicker.token(current, index)
            index += 1
            // After the picture is had, since drawing it can take long enough for the phone to cancel or ask again.
            guard let png = icon(of: row), !abandoned() else { continue }
            let next = index
            sender.send(.appIcon, label: label, data: png, abandoned: abandoned) { [weak self] sent in
                guard sent, next < rows.count else { return }
                // The sender paces inside one blob and an icon can fit in one budget, so without a tick here a list of
                // small icons would go in one burst.
                self?.scheduleAfter(BlobSender.tick) { [weak self] in
                    self?.sendIcons(current, rows, from: next, abandoned)
                }
            }
            return
        }
    }

    /// The apps, or none when the scan failed: an empty list answers the phone, and silence would not.
    private static func scan(_ catalog: AppCatalog) -> [AppEntry] {
        do {
            return try catalog.scan()
        } catch {
            Log.write("App list: the scan failed: \(error.localizedDescription)")
            return []
        }
    }

    /// An icon the phone can take, or nil. One that fails or is too big costs that picture, not the rest.
    private func icon(of app: AppEntry) -> [UInt8]? {
        do {
            guard let png = try catalog.icon(for: app), !png.isEmpty, png.count <= AppPicker.maxIconBytes else {
                return nil
            }
            return png
        } catch {
            Log.write("App list: no icon for \(app.name): \(error.localizedDescription)")
            return nil
        }
    }
}
