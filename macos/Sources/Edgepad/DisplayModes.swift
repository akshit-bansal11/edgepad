import CoreGraphics
import EdgepadCore
import Foundation

/// The refresh rates the main display offers at its current resolution. Switching a mode blanks the
/// panel for a moment, so it runs off the main thread, and only the last wanted mode is applied.
final class DisplayModes: DisplayRates {
    private var modes: [(rate: Int, mode: CGDisplayMode)] = []
    private var currentRate: Int?
    private var display = CGMainDisplayID()
    private let lock = NSLock()
    private var pending: (display: CGDirectDisplayID, mode: CGDisplayMode)?
    private let queue = DispatchQueue(label: "edgepad.display-modes")

    var rates: [Int] { modes.map(\.rate) }

    var current: Int? {
        guard let currentRate else { return nil }
        return modes.firstIndex { $0.rate == currentRate }
    }

    func refresh() {
        display = CGMainDisplayID()
        modes = []
        currentRate = nil
        guard let now = CGDisplayCopyDisplayMode(display),
            let all = CGDisplayCopyAllDisplayModes(display, nil) as? [CGDisplayMode]
        else { return }
        currentRate = Int(now.refreshRate.rounded())
        var byRate: [Int: CGDisplayMode] = [:]
        for mode in all
        where mode.width == now.width && mode.height == now.height && mode.pixelWidth == now.pixelWidth
            && mode.pixelHeight == now.pixelHeight && mode.isUsableForDesktopGUI()
        {
            // Many panels report 0, and then there are no rates to offer.
            let rate = Int(mode.refreshRate.rounded())
            if rate > 0, byRate[rate] == nil {
                byRate[rate] = mode
            }
        }
        modes = byRate.keys.sorted().compactMap { rate in byRate[rate].map { (rate, $0) } }
    }

    func select(_ index: Int) -> Bool {
        guard modes.indices.contains(index) else { return false }
        lock.lock()
        pending = (display, modes[index].mode)
        lock.unlock()
        queue.async { [self] in
            lock.lock()
            let wanted = pending
            pending = nil
            lock.unlock()
            // nil means an earlier block already applied the latest wish.
            guard let wanted else { return }
            // A refused switch leaves the panel as it was; the next refresh reports what is in force.
            _ = CGDisplaySetDisplayMode(wanted.display, wanted.mode, nil)
        }
        return true
    }
}
