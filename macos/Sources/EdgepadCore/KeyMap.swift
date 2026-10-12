/// What a Windows virtual-key code means on this Mac: an ordinary key, or one of the system's media keys.
public enum KeyTarget: Equatable {
    case key(MacKey)
    case media(MediaKey)
}

extension MacKey {
    public static let command = MacKey(0x37)
    public static let shift = MacKey(0x38)
    public static let control = MacKey(0x3B)
    public static let option = MacKey(0x3A)
    public static let tab = MacKey(0x30)
    public static let space = MacKey(0x31)
    public static let q = MacKey(0x0C)
    public static let zero = MacKey(0x1D)
    public static let equal = MacKey(0x18)
    public static let minus = MacKey(0x1B)
    public static let upArrow = MacKey(0x7E)
    public static let leftArrow = MacKey(0x7B)
    public static let rightArrow = MacKey(0x7C)
    public static let f11 = MacKey(0x67)
    public static let delete = MacKey(0x33)
    public static let returnKey = MacKey(0x24)
    public static let rightCommand = MacKey(0x36)
    public static let rightShift = MacKey(0x3C)
    public static let rightControl = MacKey(0x3E)
    public static let rightOption = MacKey(0x3D)
    public static let downArrow = MacKey(0x7D)
    public static let home = MacKey(0x73)
    public static let end = MacKey(0x77)
    public static let pageUp = MacKey(0x74)
    public static let pageDown = MacKey(0x79)
    public static let forwardDelete = MacKey(0x75)
    public static let help = MacKey(0x72)
}

/// The four modifier families; the left and right keys of each count the same.
public enum Modifier {
    case command
    case shift
    case control
    case option
}

extension MacKey {
    /// The modifier this key holds down, or nil for an ordinary key.
    public var modifier: Modifier? {
        switch self {
        case .command, .rightCommand: return .command
        case .shift, .rightShift: return .shift
        case .control, .rightControl: return .control
        case .option, .rightOption: return .option
        default: return nil
        }
    }

    public var isArrow: Bool {
        self == .leftArrow || self == .rightArrow || self == .downArrow || self == .upArrow
    }

    /// F1 to F12 and the navigation cluster; real ones carry the Fn flag, so synthetic ones must too.
    public var isFunctionOrNavigation: Bool {
        KeyMap.functionKeyCodes.contains(code)
            || [MacKey.home, .end, .pageUp, .pageDown, .forwardDelete, .help].contains(self)
    }
}

/// The wire carries Windows virtual-key codes whatever the laptop is, so the Mac turns them into its own.
/// Modifiers are mapped by position, as on an Apple keyboard: Win is Command, Ctrl is Control, Alt is Option.
public enum KeyMap {
    /// The Mac key a Windows virtual-key code means, or nil for a key a Mac does not have.
    public static func target(forWindows code: UInt16) -> KeyTarget? {
        table[code]
    }

    /// The macOS codes of F1 to F12, in order; the table maps Windows F1 to F12 onto them.
    static let functionKeyCodes: [UInt16] = [0x7A, 0x78, 0x63, 0x76, 0x60, 0x61, 0x62, 0x64, 0x65, 0x6D, 0x67, 0x6F]

    private static let table: [UInt16: KeyTarget] = makeTable()

    private static func makeTable() -> [UInt16: KeyTarget] {
        var table: [UInt16: KeyTarget] = [:]

        // Windows code to macOS code, one key each.
        let singles: [UInt16: UInt16] = [
            0x5B: 0x37, 0x5C: 0x36,
            0x11: 0x3B, 0xA2: 0x3B, 0xA3: 0x3E,
            0x12: 0x3A, 0xA4: 0x3A, 0xA5: 0x3D,
            0x10: 0x38, 0xA0: 0x38, 0xA1: 0x3C,
            0x08: 0x33, 0x09: 0x30, 0x0D: 0x24, 0x1B: 0x35, 0x20: 0x31, 0x14: 0x39,
            0x21: MacKey.pageUp.code, 0x22: MacKey.pageDown.code, 0x23: MacKey.end.code, 0x24: MacKey.home.code,
            0x25: 0x7B, 0x26: 0x7E, 0x27: 0x7C, 0x28: 0x7D,
            0x2D: MacKey.help.code, 0x2E: MacKey.forwardDelete.code,
            0x6A: 0x43, 0x6B: 0x45, 0x6D: 0x4E, 0x6E: 0x41, 0x6F: 0x4B,
            0xBA: 0x29, 0xBB: 0x18, 0xBC: 0x2B, 0xBD: 0x1B, 0xBE: 0x2F, 0xBF: 0x2C,
            0xC0: 0x32, 0xDB: 0x21, 0xDC: 0x2A, 0xDD: 0x1E, 0xDE: 0x27,
        ]
        for (windows, mac) in singles {
            table[windows] = .key(MacKey(mac))
        }

        // Runs of consecutive Windows codes, given as the macOS codes in order.
        func addRun(from first: UInt16, _ macCodes: [UInt16]) {
            for (offset, mac) in macCodes.enumerated() {
                table[first + UInt16(offset)] = .key(MacKey(mac))
            }
        }
        let digits: [UInt16] = [0x1D, 0x12, 0x13, 0x14, 0x15, 0x17, 0x16, 0x1A, 0x1C, 0x19]
        let letters: [UInt16] = [
            0x00, 0x0B, 0x08, 0x02, 0x0E, 0x03, 0x05, 0x04, 0x22, 0x26, 0x28, 0x25, 0x2E,
            0x2D, 0x1F, 0x23, 0x0C, 0x0F, 0x01, 0x11, 0x20, 0x09, 0x0D, 0x07, 0x10, 0x06,
        ]
        let functionKeys = functionKeyCodes
        let numpadDigits: [UInt16] = [0x52, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5B, 0x5C]
        addRun(from: 0x30, digits)
        addRun(from: 0x41, letters)
        addRun(from: 0x70, functionKeys)
        addRun(from: 0x60, numpadDigits)

        table[0xAD] = .media(.mute)
        table[0xAE] = .media(.soundDown)
        table[0xAF] = .media(.soundUp)
        table[0xB0] = .media(.next)
        table[0xB1] = .media(.previous)
        table[0xB3] = .media(.play)
        return table
    }
}
