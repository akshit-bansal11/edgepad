/// macOS does not count clicks for synthetic events, so a double or triple click has to be counted here and
/// stamped on each event. A click continues the run only for the same button, soon enough and close enough.
public struct ClickCounter {
    private let interval: Double
    private let radius: Double
    private var lastButton: UInt8?
    private var lastTime = 0.0
    private var lastX = 0.0
    private var lastY = 0.0

    /// The count the matching button-up carries: whatever the last down returned, 1 before any.
    public private(set) var count = 1

    /// `interval` is in seconds and `radius` in points; both limits are inclusive.
    public init(interval: Double, radius: Double) {
        self.interval = interval
        self.radius = radius
    }

    /// The click count for a button going down at `time` (seconds, any monotonic clock) and position.
    public mutating func down(button: UInt8, time: Double, x: Double, y: Double) -> Int {
        let distance = ((x - lastX) * (x - lastX) + (y - lastY) * (y - lastY)).squareRoot()
        if lastButton == button, time - lastTime <= interval, distance <= radius {
            count += 1
        } else {
            count = 1
        }
        lastButton = button
        lastTime = time
        lastX = x
        lastY = y
        return count
    }
}
