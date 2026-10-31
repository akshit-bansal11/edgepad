import AppKit
import EdgepadCore
import UniformTypeIdentifiers

/// The owner's way to edit the macro list, the twin of the Windows `MacroWindow`. Only ever opened by the menu, under
/// `RfcommServer.holdOff()`, because a connected phone is a keyboard and could otherwise answer it.
///
/// It edits a working copy and hands it to the one shared `MacroStore` on Save, which is what publishes the new
/// names to a connected phone; Cancel therefore has nothing to undo. The phone never edits this list: it only sends
/// a slot number, so every target here was typed or browsed to by the owner, in front of this Mac.
final class MacroEditorWindow: NSObject, NSTextFieldDelegate, NSTableViewDataSource, NSTableViewDelegate {
    private static let width: CGFloat = 520

    private let store: MacroStore
    private var working: [Macro]

    private let table = NSTableView()
    private let name = NSTextField()
    private let target = NSTextField()
    private let arguments = NSTextField()
    private let icon = NSTextField()
    private let count = NSTextField(labelWithString: "")
    private let addButton = NSButton(title: "Add macro", target: nil, action: nil)
    private let removeButton = NSButton(title: "Remove", target: nil, action: nil)
    private let upButton = NSButton(title: "Move up", target: nil, action: nil)
    private let downButton = NSButton(title: "Move down", target: nil, action: nil)
    private let browseButton = NSButton(title: "Browse…", target: nil, action: nil)
    private let iconButton = NSButton(title: "Choose…", target: nil, action: nil)
    private let ownIconButton = NSButton(title: "Use the program's own", target: nil, action: nil)
    private var fields: [NSTextField] { [name, target, arguments, icon] }

    /// Set while the fields are written from the list, so the write is not read back as an edit.
    private var filling = false

    init(store: MacroStore) {
        self.store = store
        working = store.macros
        super.init()
    }

    func run() {
        let alert = NSAlert()
        alert.messageText = "Macros"
        alert.informativeText =
            "Buttons on your phone's macro screen, up to \(MacroStore.maxMacros). The phone sends a button's number, "
            + "never a program, so it can only run what you add here."
        alert.accessoryView = makeBody()
        // Cancel is first, so it is the default: a stray Return changes nothing.
        alert.addButton(withTitle: "Cancel")
        alert.addButton(withTitle: "Save")
        NSApp.activate(ignoringOtherApps: true)
        while true {
            let answer = alert.runModal()
            alert.window.orderOut(nil)
            guard answer == .alertSecondButtonReturn else { return }
            if save() { return }
        }
    }

    /// True when the list was handed to the store. The store drops a row with nothing to open without a word, so a
    /// macro the owner filled in would vanish on the way to the phone; point at it and keep the window open instead.
    private func save() -> Bool {
        let missing = MacroStore.withoutTarget(working)
        if missing >= 0 {
            select(missing)
            let label = working[missing].name.isEmpty ? "A macro" : "\"\(working[missing].name)\""
            warn("\(label) has nothing to open. Browse to an app or file, or type a web address.")
            return false
        }
        if let failure = store.save(working) {
            // The edits stay in the window, so the owner can fix the cause and press Save again, or cancel.
            warn("The macros could not be saved to \(store.filePath): \(failure).")
            return false
        }
        return true
    }

    private func warn(_ text: String) {
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = "Edgepad"
        alert.informativeText = text
        alert.addButton(withTitle: "OK")
        alert.runModal()
        alert.window.orderOut(nil)
    }

    private func makeBody() -> NSView {
        let column = NSTableColumn(identifier: NSUserInterfaceItemIdentifier("name"))
        column.width = 160
        table.addTableColumn(column)
        table.headerView = nil
        table.dataSource = self
        table.delegate = self
        table.setAccessibilityLabel("Macros")
        let scroll = NSScrollView()
        scroll.documentView = table
        scroll.hasVerticalScroller = true
        scroll.borderType = .bezelBorder
        scroll.translatesAutoresizingMaskIntoConstraints = false
        scroll.widthAnchor.constraint(equalToConstant: 170).isActive = true
        scroll.heightAnchor.constraint(equalToConstant: 200).isActive = true

        for (field, label) in [(name, "Name"), (target, "Opens"), (arguments, "Arguments"), (icon, "Icon")] {
            field.delegate = self
            field.setAccessibilityLabel(label)
            field.translatesAutoresizingMaskIntoConstraints = false
            field.widthAnchor.constraint(equalToConstant: 220).isActive = true
        }
        arguments.placeholderString = "None, split on spaces (apps only)"
        icon.placeholderString = "The program's own"
        target.placeholderString = "An app, file, folder or web address"

        wire(addButton, #selector(add))
        wire(removeButton, #selector(remove))
        wire(upButton, #selector(moveUp))
        wire(downButton, #selector(moveDown))
        wire(browseButton, #selector(browse))
        wire(iconButton, #selector(chooseIcon))
        wire(ownIconButton, #selector(useOwnIcon))

        let order = NSStackView(views: [upButton, downButton])
        let listColumn = NSStackView(views: [scroll, count, addButton, removeButton, order])
        listColumn.orientation = .vertical
        listColumn.alignment = .leading
        listColumn.spacing = 8

        let targetRow = NSStackView(views: [target, browseButton])
        let iconRow = NSStackView(views: [icon, iconButton])
        let nameCaption = caption("Name, up to \(MacroStore.maxNameBytes) bytes")
        let first: [NSView] = [nameCaption, name, caption("Opens"), targetRow]
        let second: [NSView] = [caption("Arguments"), arguments, caption("Icon"), iconRow, ownIconButton]
        let fieldColumn = NSStackView(views: first + second)
        fieldColumn.orientation = .vertical
        fieldColumn.alignment = .leading
        fieldColumn.spacing = 6

        let body = NSStackView(views: [listColumn, fieldColumn])
        body.orientation = .horizontal
        body.alignment = .top
        body.spacing = 16
        body.setFrameSize(NSSize(width: Self.width, height: body.fittingSize.height))
        refresh()
        return body
    }

    private func wire(_ button: NSButton, _ action: Selector) {
        button.target = self
        button.action = action
    }

    private func caption(_ text: String) -> NSTextField {
        let label = NSTextField(labelWithString: text)
        label.font = .systemFont(ofSize: NSFont.smallSystemFontSize)
        label.textColor = .secondaryLabelColor
        return label
    }

    private var selected: Int? {
        let row = table.selectedRow
        return working.indices.contains(row) ? row : nil
    }

    private func select(_ row: Int) {
        table.reloadData()
        table.selectRowIndexes(IndexSet(integer: row), byExtendingSelection: false)
        refresh()
    }

    /// Writes the selected macro into the fields and enables only what it makes sense to press.
    private func refresh() {
        filling = true
        let macro = selected.map { working[$0] }
        name.stringValue = macro?.name ?? ""
        target.stringValue = macro?.target ?? ""
        arguments.stringValue = macro?.arguments ?? ""
        icon.stringValue = macro?.icon ?? ""
        filling = false
        for field in fields {
            field.isEnabled = macro != nil
        }
        browseButton.isEnabled = macro != nil
        iconButton.isEnabled = macro != nil
        ownIconButton.isEnabled = macro?.icon != nil
        removeButton.isEnabled = macro != nil
        upButton.isEnabled = (selected ?? 0) > 0
        downButton.isEnabled = selected.map { $0 < working.count - 1 } ?? false
        addButton.isEnabled = working.count < MacroStore.maxMacros
        count.stringValue = "\(working.count) of \(MacroStore.maxMacros)"
    }

    func numberOfRows(in tableView: NSTableView) -> Int {
        working.count
    }

    func tableView(_ tableView: NSTableView, objectValueFor tableColumn: NSTableColumn?, row: Int) -> Any? {
        let macro = working[row]
        if !macro.name.isEmpty { return macro.name }
        return macro.target.isEmpty ? "New macro" : (macro.target as NSString).lastPathComponent
    }

    func tableViewSelectionDidChange(_ notification: Notification) {
        refresh()
    }

    /// Keeps the name to what survives the trip to the phone as the owner types, rather than letting the store cut
    /// a name they watched themselves type. Every field is written back to the list as it changes.
    func controlTextDidChange(_ notification: Notification) {
        guard !filling, let row = selected else { return }
        while name.stringValue.utf8.count > MacroStore.maxNameBytes {
            name.stringValue = String(name.stringValue.dropLast())
        }
        let extra = arguments.stringValue.isEmpty ? nil : arguments.stringValue
        let own = icon.stringValue.isEmpty ? nil : icon.stringValue
        let edited = Macro(name: name.stringValue, target: target.stringValue, arguments: extra, icon: own)
        working[row] = edited
        table.reloadData(forRowIndexes: IndexSet(integer: row), columnIndexes: IndexSet(integer: 0))
        ownIconButton.isEnabled = own != nil
    }

    @objc private func add() {
        guard working.count < MacroStore.maxMacros else { return }
        working.append(Macro(name: "", target: ""))
        select(working.count - 1)
        // A field that has just been enabled cannot take focus in the same turn, so ask on the next one.
        DispatchQueue.main.async { [name] in
            name.window?.makeFirstResponder(name)
        }
    }

    @objc private func remove() {
        guard let row = selected else { return }
        working.remove(at: row)
        table.reloadData()
        if working.isEmpty {
            refresh()
        } else {
            select(min(row, working.count - 1))
        }
    }

    @objc private func moveUp() {
        move(by: -1)
    }

    @objc private func moveDown() {
        move(by: 1)
    }

    private func move(by step: Int) {
        guard let row = selected, working.indices.contains(row + step) else { return }
        working.swapAt(row, row + step)
        select(row + step)
    }

    @objc private func browse() {
        guard let row = selected, let path = choose(files: true, applications: true, types: []) else { return }
        let old = working[row]
        // A name left blank takes the app's name, so a browse alone gives a usable button.
        let fallback = old.name.isEmpty ? Self.label(for: path) : old.name
        working[row] = Macro(name: fallback, target: path, arguments: old.arguments, icon: old.icon)
        select(row)
    }

    @objc private func chooseIcon() {
        guard let row = selected, let path = choose(files: true, applications: false, types: [.image]) else { return }
        let old = working[row]
        working[row] = Macro(name: old.name, target: old.target, arguments: old.arguments, icon: path)
        select(row)
    }

    @objc private func useOwnIcon() {
        guard let row = selected else { return }
        let old = working[row]
        working[row] = Macro(name: old.name, target: old.target, arguments: old.arguments, icon: nil)
        select(row)
    }

    /// The default name for a browsed path: the file name without `.app` or any other extension, cut to the limit.
    private static func label(for path: String) -> String {
        var text = ((path as NSString).lastPathComponent as NSString).deletingPathExtension
        while text.utf8.count > MacroStore.maxNameBytes {
            text = String(text.dropLast())
        }
        return text
    }

    private func choose(files: Bool, applications: Bool, types: [UTType]) -> String? {
        let panel = NSOpenPanel()
        panel.canChooseFiles = files
        panel.canChooseDirectories = applications
        panel.allowsMultipleSelection = false
        if !types.isEmpty {
            panel.allowedContentTypes = types
        }
        if applications {
            panel.directoryURL = URL(fileURLWithPath: "/Applications")
        }
        guard panel.runModal() == .OK else { return nil }
        return panel.url?.path
    }
}
