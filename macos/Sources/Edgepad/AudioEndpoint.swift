import AudioToolbox
import CoreAudio
import EdgepadCore
import Foundation

/// The default speakers or the default microphone through Core Audio. The default device is looked up on
/// every call because the user can switch devices at any time. Every answer may be "no".
final class AudioEndpoint: LevelControl {
    enum Direction {
        case output
        case input
    }

    private let direction: Direction

    init(direction: Direction) {
        self.direction = direction
    }

    func read() -> Level? {
        guard let device = defaultDevice() else { return nil }
        let volume = volumeAddress()
        guard has(device, volume),
            let level: Float32 = get(device, volume)
        else { return nil }
        // A driver can answer NaN or infinity, and converting that to Int traps.
        guard level.isFinite else { return nil }
        let mute = muteAddress()
        var muted = false
        if has(device, mute), let value: UInt32 = get(device, mute) {
            muted = value != 0
        }
        return Level(percent: Percent.clamp(Int((level * Float32(Percent.max)).rounded())), muted: muted)
    }

    func setLevel(_ percent: Int) -> Bool {
        guard let device = defaultDevice() else { return false }
        let volume = volumeAddress()
        guard has(device, volume) else { return false }
        let scalar: Float32 = Float32(Percent.clamp(percent)) / Float32(Percent.max)
        return set(device, volume, scalar)
    }

    func toggleMute() -> Bool {
        guard let device = defaultDevice() else { return false }
        let mute = muteAddress()
        guard has(device, mute), isSettable(device, mute),
            let current: UInt32 = get(device, mute)
        else { return false }
        // Core Audio wants exactly four bytes here, so the value must be a UInt32, not a defaulted Int.
        let next: UInt32 = current == 0 ? 1 : 0
        return set(device, mute, next)
    }

    /// The default device's kind and name for TEXT 20, or nil when there is none. Built-in shows the data source
    /// ("Internal Speakers", "Headphones") where it has one; Bluetooth and a monitor show the device's own name.
    func outputDevice() -> (OutputKind, String)? {
        guard let device = defaultDevice() else { return nil }
        let name = string(device, address(kAudioObjectPropertyName, kAudioObjectPropertyScopeGlobal)) ?? ""
        let transport: UInt32 =
            get(device, address(kAudioDevicePropertyTransportType, kAudioObjectPropertyScopeGlobal)) ?? 0
        switch transport {
        case UInt32(kAudioDeviceTransportTypeBuiltIn):
            let source = dataSource(device)
            let shown = source?.name ?? name
            let headphones = source?.id == Self.headphonesSource || shown.localizedCaseInsensitiveContains("headphone")
            return (headphones ? .headphones : .speaker, shown)
        case UInt32(kAudioDeviceTransportTypeBluetooth), UInt32(kAudioDeviceTransportTypeBluetoothLE):
            return (.headphones, name)
        case UInt32(kAudioDeviceTransportTypeHDMI), UInt32(kAudioDeviceTransportTypeDisplayPort):
            return (.monitor, name)
        default:
            return (.other, name)
        }
    }

    /// `kIOAudioOutputPortSubTypeHeadphones`, the data source id of a built-in headphone jack.
    private static let headphonesSource: UInt32 = 0x6864_706E  // 'hdpn'

    /// Calls `onChange` on the main queue whenever the default device changes. Nil when Core Audio refused; otherwise
    /// hand the result to `stopWatching` on teardown.
    func watchDefault(_ onChange: @escaping () -> Void) -> AudioObjectPropertyListenerBlock? {
        var address = defaultAddress()
        let block: AudioObjectPropertyListenerBlock = { _, _ in onChange() }
        let status = AudioObjectAddPropertyListenerBlock(
            AudioObjectID(kAudioObjectSystemObject), &address, DispatchQueue.main, block)
        return status == noErr ? block : nil
    }

    func stopWatching(_ block: @escaping AudioObjectPropertyListenerBlock) {
        var address = defaultAddress()
        _ = AudioObjectRemovePropertyListenerBlock(
            AudioObjectID(kAudioObjectSystemObject), &address, DispatchQueue.main, block)
    }

    /// The current data source's id and name, or nil for a device without one.
    private func dataSource(_ device: AudioObjectID) -> (id: UInt32, name: String)? {
        guard var id: UInt32 = get(device, address(kAudioDevicePropertyDataSource, scope)) else { return nil }
        var translate = address(kAudioDevicePropertyDataSourceNameForIDCFString, scope)
        var name: Unmanaged<CFString>?
        var size = UInt32(MemoryLayout<AudioValueTranslation>.size)
        let status = withUnsafeMutablePointer(to: &id) { input in
            withUnsafeMutablePointer(to: &name) { output in
                var translation = AudioValueTranslation(
                    mInputData: input, mInputDataSize: UInt32(MemoryLayout<UInt32>.size),
                    mOutputData: output, mOutputDataSize: UInt32(MemoryLayout<Unmanaged<CFString>?>.size))
                return AudioObjectGetPropertyData(device, &translate, 0, nil, &size, &translation)
            }
        }
        guard status == noErr, let name else { return nil }
        return (id, name.takeRetainedValue() as String)
    }

    /// A property Core Audio answers with a retained CFString, such as a device's name.
    private func string(_ object: AudioObjectID, _ address: AudioObjectPropertyAddress) -> String? {
        var address = address
        var value: Unmanaged<CFString>?
        var size = UInt32(MemoryLayout<Unmanaged<CFString>?>.size)
        let status = withUnsafeMutablePointer(to: &value) {
            AudioObjectGetPropertyData(object, &address, 0, nil, &size, $0)
        }
        guard status == noErr, let value else { return nil }
        return value.takeRetainedValue() as String
    }

    /// The C calls take the address by pointer, so each wrapper copies it into a `var`.
    private func has(_ object: AudioObjectID, _ address: AudioObjectPropertyAddress) -> Bool {
        var address = address
        return AudioObjectHasProperty(object, &address)
    }

    private func isSettable(_ object: AudioObjectID, _ address: AudioObjectPropertyAddress) -> Bool {
        var address = address
        var settable = DarwinBoolean(false)
        return AudioObjectIsPropertySettable(object, &address, &settable) == noErr && settable.boolValue
    }

    private var scope: AudioObjectPropertyScope {
        switch direction {
        case .output: return kAudioDevicePropertyScopeOutput
        case .input: return kAudioDevicePropertyScopeInput
        }
    }

    private func address(_ selector: AudioObjectPropertySelector, _ scope: AudioObjectPropertyScope)
        -> AudioObjectPropertyAddress
    {
        AudioObjectPropertyAddress(
            mSelector: selector, mScope: scope, mElement: kAudioObjectPropertyElementMain)
    }

    private func volumeAddress() -> AudioObjectPropertyAddress {
        address(kAudioHardwareServiceDeviceProperty_VirtualMainVolume, scope)
    }

    private func muteAddress() -> AudioObjectPropertyAddress {
        address(kAudioDevicePropertyMute, scope)
    }

    private func defaultAddress() -> AudioObjectPropertyAddress {
        let selector =
            direction == .output ? kAudioHardwarePropertyDefaultOutputDevice : kAudioHardwarePropertyDefaultInputDevice
        return address(selector, kAudioObjectPropertyScopeGlobal)
    }

    private func defaultDevice() -> AudioObjectID? {
        let system = AudioObjectID(kAudioObjectSystemObject)
        guard let device: AudioObjectID = get(system, defaultAddress()),
            device != kAudioObjectUnknown
        else { return nil }
        return device
    }

    private func get<T: FixedWidthInteger>(_ object: AudioObjectID, _ address: AudioObjectPropertyAddress) -> T? {
        var address = address
        var value: T = 0
        var size = UInt32(MemoryLayout<T>.size)
        // A typed pointer, because `&value` straight into a raw pointer is rejected for a generic T.
        let status = withUnsafeMutablePointer(to: &value) {
            AudioObjectGetPropertyData(object, &address, 0, nil, &size, $0)
        }
        return status == noErr ? value : nil
    }

    private func get(_ object: AudioObjectID, _ address: AudioObjectPropertyAddress) -> Float32? {
        var address = address
        var value: Float32 = 0
        var size = UInt32(MemoryLayout<Float32>.size)
        let status = AudioObjectGetPropertyData(object, &address, 0, nil, &size, &value)
        return status == noErr ? value : nil
    }

    private func set<T>(_ object: AudioObjectID, _ address: AudioObjectPropertyAddress, _ newValue: T) -> Bool {
        var address = address
        var value = newValue
        let size = UInt32(MemoryLayout<T>.size)
        let status = withUnsafePointer(to: &value) {
            AudioObjectSetPropertyData(object, &address, 0, nil, size, $0)
        }
        return status == noErr
    }
}
