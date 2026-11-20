import Foundation
import IOKit

/// The GPU numbers macOS gives without a driver: what each `IOAccelerator` in the registry publishes. Which keys a GPU
/// publishes depends on its driver, so a number it does not publish is -1, never 0. No temperature or power is read:
/// macOS has no public source for either. HW-unverified.
enum MacGpu {
    private static let mebibyte = 1_048_576

    /// Utilisation (the busiest accelerator) and the share of VRAM in use, each 0 to 100 or -1.
    static func load() -> (gpu: Int, vram: Int) {
        var gpu = -1
        var vram = -1
        forEachAccelerator { service in
            guard let stats = property(service, "PerformanceStatistics") as? [String: Any] else { return }
            if let busy = number(stats["Device Utilization %"]) {
                gpu = max(gpu, min(max(busy, 0), 100))
            }
            if vram < 0 {
                vram = vramPercent(service, stats)
            }
        }
        return (gpu, vram)
    }

    /// The first accelerator's model name ("" when none says) and VRAM size in MiB (-1 for unified memory or none).
    static func info() -> (name: String, vramMiB: Int) {
        var name = ""
        var vram = -1
        forEachAccelerator { service in
            if name.isEmpty {
                name = text(nearest(service, "model"))
            }
            if vram < 0, let mib = vramMiB(service) {
                vram = mib
            }
        }
        return (name, vram)
    }

    private static func vramPercent(_ service: io_registry_entry_t, _ stats: [String: Any]) -> Int {
        guard let used = number(stats["vramUsedBytes"]), used >= 0 else { return -1 }
        let total = vramMiB(service).map { $0 * mebibyte } ?? number(stats["vramFreeBytes"]).map { used + $0 } ?? 0
        guard total > 0 else { return -1 }
        return min(100, used * 100 / total)
    }

    private static func vramMiB(_ service: io_registry_entry_t) -> Int? {
        guard let mib = number(nearest(service, "VRAM,totalMB")), mib > 0 else { return nil }
        return mib
    }

    private static func forEachAccelerator(_ body: (io_registry_entry_t) -> Void) {
        var iterator: io_iterator_t = 0
        let matching = IOServiceMatching("IOAccelerator")
        guard IOServiceGetMatchingServices(kIOMainPortDefault, matching, &iterator) == KERN_SUCCESS else { return }
        defer { IOObjectRelease(iterator) }
        while true {
            let service = IOIteratorNext(iterator)
            guard service != 0 else { break }
            defer { IOObjectRelease(service) }
            body(service)
        }
    }

    /// The key on the accelerator or one of its three nearest ancestors, where a PCI GPU keeps "model" and
    /// "VRAM,totalMB". Never further up: the platform's own "model", the Mac's model name, sits at the top.
    private static func nearest(_ service: io_registry_entry_t, _ key: String) -> Any? {
        var entry = service
        IOObjectRetain(entry)
        defer { IOObjectRelease(entry) }
        for _ in 0...3 {
            if let value = property(entry, key) { return value }
            var parent: io_registry_entry_t = 0
            guard IORegistryEntryGetParentEntry(entry, kIOServicePlane, &parent) == KERN_SUCCESS else { return nil }
            IOObjectRelease(entry)
            entry = parent
        }
        return nil
    }

    private static func property(_ entry: io_registry_entry_t, _ key: String) -> Any? {
        IORegistryEntryCreateCFProperty(entry, key as CFString, kCFAllocatorDefault, 0)?.takeRetainedValue()
    }

    private static func number(_ value: Any?) -> Int? {
        (value as? NSNumber)?.intValue
    }

    /// A registry string, which a PCI device stores as NUL-terminated bytes.
    private static func text(_ value: Any?) -> String {
        if let string = value as? String { return string.trimmingCharacters(in: .whitespacesAndNewlines) }
        guard let data = value as? Data else { return "" }
        return String(decoding: data.prefix { $0 != 0 }, as: UTF8.self).trimmingCharacters(in: .whitespacesAndNewlines)
    }
}
