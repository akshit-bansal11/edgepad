import CoreGraphics
import Darwin
import EdgepadCore

/// The built-in panel's brightness. macOS has no public API for it, so this loads the private
/// DisplayServices framework at run time. Failing closed is right: if Apple renames or removes the
/// framework, the app must say "no brightness" rather than refuse to launch or guess at an address.
final class DisplayBrightness: BrightnessControl {
    private typealias GetFunction = @convention(c) (CGDirectDisplayID, UnsafeMutablePointer<Float>) -> Int32
    private typealias SetFunction = @convention(c) (CGDirectDisplayID, Float) -> Int32

    private let getBrightness: GetFunction?
    private let setBrightness: SetFunction?

    init() {
        let path = "/System/Library/PrivateFrameworks/DisplayServices.framework/DisplayServices"
        if let handle = dlopen(path, RTLD_LAZY),
            let getter = dlsym(handle, "DisplayServicesGetBrightness"),
            let setter = dlsym(handle, "DisplayServicesSetBrightness")
        {
            getBrightness = unsafeBitCast(getter, to: GetFunction.self)
            setBrightness = unsafeBitCast(setter, to: SetFunction.self)
        } else {
            getBrightness = nil
            setBrightness = nil
        }
    }

    func read() -> Int? {
        guard let getBrightness, let display = builtInDisplay() else { return nil }
        var value: Float = 0
        guard getBrightness(display, &value) == 0 else { return nil }
        return Int((value * 100).rounded())
    }

    func set(_ percent: Int) -> Bool {
        guard let setBrightness, let display = builtInDisplay() else { return false }
        let clampedPercent = min(max(percent, 0), 100)
        return setBrightness(display, Float(clampedPercent) / 100) == 0
    }

    /// None on a Mac mini or with the lid closed.
    private func builtInDisplay() -> CGDirectDisplayID? {
        var ids = [CGDirectDisplayID](repeating: 0, count: 16)
        var count: UInt32 = 0
        guard CGGetActiveDisplayList(UInt32(ids.count), &ids, &count) == .success else { return nil }
        return ids.prefix(Int(count)).first { CGDisplayIsBuiltin($0) != 0 }
    }
}
