import CoreAudio
import EdgepadCore
import Foundation
import IOKit
import IOKit.ps

/// This Mac's batteries and default output for TEXT 19 and TEXT 20. One per connection, like the injector, because
/// the session sets and clears `changed`. Everything runs on the main thread, as `Session` requires: the power
/// source run-loop source is on the main run loop and the Core Audio listener on the main queue.
final class MacPowerSource: PowerSource {
    /// Where the registry keeps a Bluetooth peripheral's charge: `AppleDeviceManagementHIDEventService` for a Magic
    /// Keyboard, Mouse or Trackpad. A class that is not in this Mac's registry just finds nothing.
    private static let peripheralClasses = ["AppleDeviceManagementHIDEventService", "IOBluetoothDevice"]

    private let speakers: AudioEndpoint
    private var batteryEvents: CFRunLoopSource?
    private var outputEvents: AudioObjectPropertyListenerBlock?

    init(speakers: AudioEndpoint) {
        self.speakers = speakers
    }

    deinit {
        stopListening()
    }

    /// Listening only while a session wants to hear: set on admission, cleared by `Session.end()`.
    var changed: (() -> Void)? {
        didSet {
            if changed == nil { stopListening() } else { startListening() }
        }
    }

    func batteries() -> [BatteryEntry] {
        var entries = laptop().map { [$0] } ?? []
        var seen = Set<String>()
        for entry in peripherals() where seen.insert(entry.name).inserted {
            entries.append(entry)
        }
        return entries
    }

    func output() -> (OutputKind, String)? {
        speakers.outputDevice()
    }

    /// The internal battery, or nil for a Mac without one.
    private func laptop() -> BatteryEntry? {
        guard let info = IOPSCopyPowerSourcesInfo()?.takeRetainedValue(),
            let list = IOPSCopyPowerSourcesList(info)?.takeRetainedValue() as? [CFTypeRef]
        else { return nil }
        for source in list {
            guard
                let description = IOPSGetPowerSourceDescription(info, source)?.takeUnretainedValue()
                    as? [String: Any],
                description[kIOPSTypeKey] as? String == kIOPSInternalBatteryType,
                description[kIOPSIsPresentKey] as? Bool ?? true,
                let current = description[kIOPSCurrentCapacityKey] as? Int,
                let full = description[kIOPSMaxCapacityKey] as? Int, full > 0
            else { continue }
            let charging = description[kIOPSIsChargingKey] as? Bool ?? false
            let name = description[kIOPSNameKey] as? String ?? ""
            return BatteryEntry(kind: .laptop, level: current * 100 / full, charging: charging, name: name)
        }
        return nil
    }

    /// Bluetooth peripherals that publish `BatteryPercent`, in registry order. One with no level is left out.
    private func peripherals() -> [BatteryEntry] {
        var entries: [BatteryEntry] = []
        for name in Self.peripheralClasses {
            var iterator: io_iterator_t = 0
            guard IOServiceGetMatchingServices(kIOMainPortDefault, IOServiceMatching(name), &iterator) == KERN_SUCCESS
            else { continue }
            defer { IOObjectRelease(iterator) }
            while true {
                let service = IOIteratorNext(iterator)
                guard service != 0 else { break }
                defer { IOObjectRelease(service) }
                guard let level = property(service, "BatteryPercent") as? Int else { continue }
                let product = property(service, "Product") as? String ?? ""
                let type = property(service, "DeviceTypeDescription") as? String ?? ""
                // No charging flag is read: the registry key for it is not one this app has verified.
                let kind = Self.kind(of: "\(type) \(product)")
                entries.append(BatteryEntry(kind: kind, level: level, charging: false, name: product))
            }
        }
        return entries
    }

    private func property(_ service: io_registry_entry_t, _ key: String) -> Any? {
        IORegistryEntryCreateCFProperty(service, key as CFString, kCFAllocatorDefault, 0)?.takeRetainedValue()
    }

    /// From the words in the device's own description; anything unrecognised is `other`.
    private static func kind(of text: String) -> BatteryKind {
        let text = text.lowercased()
        if text.contains("keyboard") { return .keyboard }
        if text.contains("mouse") || text.contains("trackpad") { return .mouse }
        let headphones = ["airpods", "headphone", "headset", "beats", "buds"]
        if headphones.contains(where: { text.contains($0) }) { return .headphones }
        if text.contains("speaker") { return .speaker }
        return .other
    }

    /// A C callback, so it cannot capture: the source arrives as the context.
    private static let onPowerEvent: IOPowerSourceCallbackType = { context in
        guard let context else { return }
        Unmanaged<MacPowerSource>.fromOpaque(context).takeUnretainedValue().changed?()
    }

    private func startListening() {
        if batteryEvents == nil {
            let context = Unmanaged.passUnretained(self).toOpaque()
            if let source = IOPSNotificationCreateRunLoopSource(Self.onPowerEvent, context)?.takeRetainedValue() {
                CFRunLoopAddSource(CFRunLoopGetMain(), source, .defaultMode)
                batteryEvents = source
            }
        }
        if outputEvents == nil {
            outputEvents = speakers.watchDefault { [weak self] in self?.changed?() }
        }
    }

    /// Removed before this object can go, so the unretained context above never outlives it.
    private func stopListening() {
        if let source = batteryEvents {
            CFRunLoopRemoveSource(CFRunLoopGetMain(), source, .defaultMode)
            CFRunLoopSourceInvalidate(source)
            batteryEvents = nil
        }
        if let block = outputEvents {
            speakers.stopWatching(block)
            outputEvents = nil
        }
    }
}
