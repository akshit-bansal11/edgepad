/// Wire scroll units are 120 a notch and arrive in fractions of one, but a scroll event carries whole pixels.
/// Rounding each frame would lose every slow scroll, so the fraction left over is carried to the next frame.
public struct ScrollAccumulator {
    public static let wireUnitsPerNotch = 120.0

    private let scale: Double
    private var remainderX = 0.0
    private var remainderY = 0.0

    public init(pixelsPerNotch: Double) {
        scale = pixelsPerNotch / Self.wireUnitsPerNotch
    }

    /// Whole pixels to scroll for these wire units, same signs as given; the fraction is carried to the next call.
    public mutating func pixels(dx: Int, dy: Int) -> (x: Int32, y: Int32) {
        let x = Self.whole(Double(dx) * scale + remainderX, remainder: &remainderX)
        let y = Self.whole(Double(dy) * scale + remainderY, remainder: &remainderY)
        return (x, y)
    }

    /// Truncates toward zero and keeps the rest; the clamp stops an absurd value from trapping `Int32(_:)`.
    private static func whole(_ wanted: Double, remainder: inout Double) -> Int32 {
        let truncated = wanted.rounded(.towardZero)
        remainder = wanted - truncated
        return Int32(min(max(truncated, Double(Int32.min)), Double(Int32.max)))
    }
}
