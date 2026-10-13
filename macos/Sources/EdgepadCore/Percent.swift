/// Every level on the wire (volume, brightness, a SET_VALUE) is 0 to 100. That one fact used to be written
/// out in five places; it lives here so a fix to it is made once.
public enum Percent {
    public static let max = 100

    /// `value` brought into 0...max.
    public static func clamp(_ value: Int) -> Int {
        Swift.min(max, Swift.max(0, value))
    }
}
