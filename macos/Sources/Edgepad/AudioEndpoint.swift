import AudioToolbox
import CoreAudio
import EdgepadCore

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
        guard AudioObjectHasProperty(device, volume),
            let level: Float32 = get(device, volume)
        else { return nil }
        let mute = muteAddress()
        var muted = false
        if AudioObjectHasProperty(device, mute), let value: UInt32 = get(device, mute) {
            muted = value != 0
        }
        return Level(percent: Int((level * 100).rounded()), muted: muted)
    }

    func setLevel(_ percent: Int) -> Bool {
        guard let device = defaultDevice() else { return false }
        let volume = volumeAddress()
        guard AudioObjectHasProperty(device, volume) else { return false }
        let clampedPercent = min(max(percent, 0), 100)
        return set(device, volume, Float32(clampedPercent) / 100)
    }

    func toggleMute() -> Bool {
        guard let device = defaultDevice() else { return false }
        let mute = muteAddress()
        var settable = DarwinBoolean(false)
        guard AudioObjectHasProperty(device, mute),
            AudioObjectIsPropertySettable(device, mute, &settable) == noErr, settable.boolValue,
            let current: UInt32 = get(device, mute)
        else { return false }
        return set(device, mute, current == 0 ? 1 : 0)
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

    private func defaultDevice() -> AudioObjectID? {
        let selector =
            direction == .output ? kAudioHardwarePropertyDefaultOutputDevice : kAudioHardwarePropertyDefaultInputDevice
        let system = AudioObjectID(kAudioObjectSystemObject)
        guard let device: AudioObjectID = get(system, address(selector, kAudioObjectPropertyScopeGlobal)),
            device != kAudioObjectUnknown
        else { return nil }
        return device
    }

    private func get<T: FixedWidthInteger>(_ object: AudioObjectID, _ address: AudioObjectPropertyAddress) -> T? {
        var address = address
        var value: T = 0
        var size = UInt32(MemoryLayout<T>.size)
        let status = AudioObjectGetPropertyData(object, &address, 0, nil, &size, &value)
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
        let status = AudioObjectSetPropertyData(
            object, &address, 0, nil, UInt32(MemoryLayout<T>.size), &value)
        return status == noErr
    }
}
