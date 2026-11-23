import Foundation

/// Reading JSON that came from a file or the network, the way the laptop core's `JsonDocument` code reads it: a depth
/// cap checked before parsing, numbers that must really be numbers (a JSON `true` is not 1), and integers that must be
/// whole. Every function answers nil for anything else, never throws.
enum JsonRead {
    /// The parsed top-level object or array, or nil when `data` is not JSON or nests deeper than `maxDepth`.
    static func parse(_ data: Data, maxDepth: Int) -> Any? {
        guard depthWithin(data, maxDepth) else { return nil }
        return try? JSONSerialization.jsonObject(with: data)
    }

    /// The object `data` holds, or nil.
    static func object(_ data: Data, maxDepth: Int) -> [String: Any]? {
        parse(data, maxDepth: maxDepth) as? [String: Any]
    }

    /// True when no `{` or `[` outside a string nests deeper than `max`. A cheap pass over the bytes, so a bomb of
    /// brackets is refused before JSONSerialization recurses into it.
    static func depthWithin(_ data: Data, _ max: Int) -> Bool {
        var depth = 0
        var inString = false
        var escaped = false
        for byte in data {
            if inString {
                if escaped {
                    escaped = false
                } else if byte == UInt8(ascii: "\\") {
                    escaped = true
                } else if byte == UInt8(ascii: "\"") {
                    inString = false
                }
                continue
            }
            switch byte {
            case UInt8(ascii: "\""):
                inString = true
            case UInt8(ascii: "{"), UInt8(ascii: "["):
                depth += 1
                if depth > max { return false }
            case UInt8(ascii: "}"), UInt8(ascii: "]"):
                depth -= 1
            default:
                break
            }
        }
        return true
    }

    static func string(_ value: Any?) -> String? {
        value as? String
    }

    /// A JSON number that is not a boolean.
    static func number(_ value: Any?) -> NSNumber? {
        guard let number = value as? NSNumber, CFGetTypeID(number) != CFBooleanGetTypeID() else { return nil }
        return number
    }

    /// A finite JSON number as a Double.
    static func double(_ value: Any?) -> Double? {
        guard let number = number(value) else { return nil }
        let double = number.doubleValue
        return double.isFinite ? double : nil
    }

    /// A whole JSON number that fits an Int64; 12.5 is not one.
    static func int64(_ value: Any?) -> Int64? {
        guard let number = number(value) else { return nil }
        let double = number.doubleValue
        guard double.isFinite, abs(double) < 9.2e18 else { return nil }
        let whole = number.int64Value
        return Double(whole) == double ? whole : nil
    }

    /// A whole JSON number that fits an Int32.
    static func int32(_ value: Any?) -> Int? {
        guard let whole = int64(value), whole >= Int64(Int32.min), whole <= Int64(Int32.max) else { return nil }
        return Int(whole)
    }

    /// True only for a JSON `true`.
    static func isTrue(_ value: Any?) -> Bool {
        guard let number = value as? NSNumber, CFGetTypeID(number) == CFBooleanGetTypeID() else { return false }
        return number.boolValue
    }
}
