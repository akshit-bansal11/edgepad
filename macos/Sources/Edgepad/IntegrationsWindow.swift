import AppKit
import EdgepadCore

/// What the Integrations window needs from the rest of the app, filled by `MacDashboard`: the settings, and who to tell
/// that a switch moved.
struct IntegrationsHooks {
    let settings: IntegrationSettings
    /// An integration (an `IntegrationSettings` key) was switched on or off, or its settings changed.
    let switched: (String, Bool) -> Void
}

/// The Integrations window: system stats and weather, every one off until the owner turns it on, the twin of the
/// Windows and Linux windows. The AI tools and git hosts live in `DevelopersWindow`. Switches write
/// `IntegrationSettings` at once. No network call is made from here. Non-modal. Main thread only.
final class IntegrationsWindow: NSObject, NSWindowDelegate, NSTextFieldDelegate {
    private static let width: CGFloat = 480

    private let hooks: IntegrationsHooks
    private var window: NSWindow?
    private let stats = NSButton(checkboxWithTitle: "", target: nil, action: nil)
    private let weather = NSButton(checkboxWithTitle: "", target: nil, action: nil)
    private let city = NSTextField()

    init(hooks: IntegrationsHooks) {
        self.hooks = hooks
        super.init()
    }

    /// Shows the window, or brings it to the front when it is open already.
    func show() {
        if window == nil {
            window = makeWindow()
        }
        NSApp.activate(ignoringOtherApps: true)
        window?.makeKeyAndOrderFront(nil)
        refresh()
    }

    /// Closes the window if it is open.
    func close() {
        window?.close()
    }

    func windowWillClose(_ notification: Notification) {
        window = nil
    }

    // ---- building

    private func makeWindow() -> NSWindow {
        stats.title = tr("Integrations_Stats", "Show system stats on the phone")
        stats.target = self
        stats.action = #selector(statsChanged)
        weather.title = tr("Integrations_Weather", "Show weather on the phone")
        weather.target = self
        weather.action = #selector(weatherChanged)
        city.stringValue = hooks.settings.text(IntegrationSettings.city)
        city.placeholderString = tr("Integrations_City", "City")
        city.delegate = self
        city.widthAnchor.constraint(equalToConstant: 240).isActive = true
        let cityRow = NSStackView(views: [NSTextField(labelWithString: tr("Integrations_City", "City")), city])
        cityRow.spacing = 8

        let close = NSButton(title: tr("Common_Close", "Close"), target: self, action: #selector(closePressed))
        close.keyEquivalent = "\r"
        let intro = NSTextField(
            wrappingLabelWithString: tr("Integrations_Intro", "Everything here is off until you turn it on."))
        let note = NSTextField(
            wrappingLabelWithString: tr("Integrations_WeatherNote", "Sends only the city name to open-meteo.com."))
        note.textColor = .secondaryLabelColor
        let stack = NSStackView(views: [intro, stats, weather, cityRow, note, close])
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 10
        stack.edgeInsets = NSEdgeInsets(top: 20, left: 20, bottom: 20, right: 20)
        stack.setCustomSpacing(18, after: note)

        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: Self.width + 40, height: 300), styleMask: [.titled, .closable],
            backing: .buffered, defer: false)
        window.title = tr("Integrations_Title", "Integrations")
        window.isReleasedWhenClosed = false
        window.delegate = self
        window.contentView = stack
        window.center()
        return window
    }

    // ---- showing

    func refresh() {
        let settings = hooks.settings
        stats.state = settings.flag(IntegrationSettings.stats) ? .on : .off
        weather.state = settings.flag(IntegrationSettings.weather) ? .on : .off
        window?.contentView?.layoutSubtreeIfNeeded()
    }

    // ---- switches

    @objc private func statsChanged() {
        let on = stats.state == .on
        hooks.settings.setFlag(IntegrationSettings.stats, on)
        hooks.switched(IntegrationSettings.stats, on)
    }

    @objc private func weatherChanged() {
        let on = weather.state == .on
        saveCity()
        hooks.settings.setFlag(IntegrationSettings.weather, on)
        hooks.switched(IntegrationSettings.weather, on)
    }

    func controlTextDidEndEditing(_ notification: Notification) {
        guard saveCity() else { return }
        hooks.switched(IntegrationSettings.weather, hooks.settings.flag(IntegrationSettings.weather))
    }

    /// Stores the typed city and forgets the place looked up for the old one. True when it changed.
    @discardableResult
    private func saveCity() -> Bool {
        let settings = hooks.settings
        let typed = city.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        guard typed != settings.text(IntegrationSettings.city) else { return false }
        settings.set(IntegrationSettings.city, typed)
        for key in [IntegrationSettings.lat, IntegrationSettings.lon, IntegrationSettings.place] {
            settings.set(key, "")
        }
        return true
    }

    @objc private func closePressed() {
        window?.performClose(nil)
    }
}
