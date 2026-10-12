import EdgepadCore
import Foundation
import IOBluetooth

/// Advertises Edgepad's RFCOMM service and hands each phone that connects to a `Session`. Everything here runs
/// on the main thread: IOBluetooth delivers its callbacks on the main run loop and `Session` is single-threaded.
final class RfcommServer: NSObject {
    private static let l2capUuid16: UInt16 = 0x0100
    private static let rfcommUuid16: UInt16 = 0x0003
    private static let publicBrowseRootUuid16: UInt16 = 0x1002
    private static let pollInterval: TimeInterval = 0.5
    private static let helloTimeout: TimeInterval = 10
    private static let waiting = "Waiting for your phone"
    private static let encryptionDisabled: BluetoothHCIEncryptionMode = 0

    private let trust: TrustStore
    private let askTrust: (String) -> Bool
    private let onStatus: (String) -> Void

    private let speakers = AudioEndpoint(direction: .output)
    private let microphone = AudioEndpoint(direction: .input)
    private let brightness = DisplayBrightness()
    private let display = DisplayModes()

    private var serviceRecord: IOBluetoothSDPServiceRecord?
    private var notification: IOBluetoothUserNotification?
    private var pollTimer: Timer?
    private var current: Session?
    /// Held for as long as its session is current: the channel only keeps its delegate weakly.
    private var currentTransport: RfcommTransport?

    init(trust: TrustStore, askTrust: @escaping (String) -> Bool, onStatus: @escaping (String) -> Void) {
        self.trust = trust
        self.askTrust = askTrust
        self.onStatus = onStatus
        super.init()
    }

    /// False when the service could not be published or the channel could not be listened on; the log says which.
    func start() -> Bool {
        guard let uuid = UUID(uuidString: ProtocolConstants.serviceId) else {
            Log.write("RFCOMM start failed: the service id is not a UUID")
            return false
        }
        let serviceUuid = IOBluetoothSDPUUID(data: withUnsafeBytes(of: uuid.uuid) { Data($0) })
        let record: [String: Any] = [
            "0001 - ServiceClassIDList": [serviceUuid as Any],
            "0004 - ProtocolDescriptorList": [
                [IOBluetoothSDPUUID(uuid16: Self.l2capUuid16) as Any],
                [
                    IOBluetoothSDPUUID(uuid16: Self.rfcommUuid16) as Any,
                    ["DataElementSize": 1, "DataElementType": 1, "DataElementValue": 1],
                ],
            ],
            "0005 - BrowseGroupList*": [IOBluetoothSDPUUID(uuid16: Self.publicBrowseRootUuid16) as Any],
            "0100 - ServiceName*": "Edgepad",
        ]
        guard let published = IOBluetoothSDPServiceRecord.publishedServiceRecord(with: record) else {
            Log.write("RFCOMM start failed: the service record could not be published")
            return false
        }
        serviceRecord = published

        // macOS may reassign the channel, so ask the record what it was really given.
        var channelID: BluetoothRFCOMMChannelID = 0
        guard published.getRFCOMMChannelID(&channelID) == kIOReturnSuccess else {
            Log.write("RFCOMM start failed: the published record has no RFCOMM channel")
            return false
        }
        guard
            let registered = IOBluetoothRFCOMMChannel.register(
                forChannelOpenNotifications: self,
                selector: #selector(channelOpened(_:channel:)),
                withChannelID: channelID,
                direction: kIOBluetoothUserNotificationChannelDirectionIncoming)
        else {
            Log.write("RFCOMM start failed: could not listen for channel \(channelID)")
            return false
        }
        notification = registered

        Log.write("RFCOMM service advertising")
        onStatus(Self.waiting)
        pollTimer = Timer.scheduledTimer(withTimeInterval: Self.pollInterval, repeats: true) { [weak self] _ in
            self?.current?.poll()
        }
        return true
    }

    func stop() {
        pollTimer?.invalidate()
        pollTimer = nil
        notification?.unregister()
        notification = nil
        _ = serviceRecord?.removeServiceRecord()
        serviceRecord = nil
        current?.end()
    }

    /// Hangs up on the connected phone, if there is one.
    func disconnect() {
        current?.end()
    }

    @objc private func channelOpened(_ notification: IOBluetoothUserNotification, channel: IOBluetoothRFCOMMChannel) {
        let device = channel.getDevice()
        let address = device?.addressString ?? "unknown"

        // Windows demands an authenticated, encrypted socket; macOS has no such switch, so these are the checks.
        // Being paired only says the address is in this Mac's list, and a device can spoof an address. An
        // encrypted link can only exist with a device that holds the link key made at pairing, which an address
        // alone cannot prove.
        guard let device, device.isPaired() else {
            Log.write("Refused \(address): it is not paired with this Mac")
            _ = channel.close()
            return
        }
        guard device.getEncryptionMode() != Self.encryptionDisabled else {
            Log.write("Refused \(address): the link is not encrypted")
            _ = channel.close()
            return
        }
        // A device certain to be refused must never displace the connected phone.
        if trust.refuses(address) {
            let trusted = trust.trusted ?? "nobody"
            Log.write("Refused \(address) without disturbing the connected phone: this Mac trusts \(trusted)")
            _ = channel.close()
            return
        }

        let transport = RfcommTransport(channel: channel)
        let injector = InputInjector()
        let dispatcher = Dispatcher(
            input: injector, speakers: speakers, microphone: microphone, brightness: brightness, display: display)
        let phone = PhoneLabel.describe(name: device.name, address: address)
        let ask = askTrust
        let session = Session(
            address: address,
            transport: transport,
            trust: trust,
            askTrust: { _ in ask(phone) },
            dispatcher: dispatcher,
            input: injector,
            speakers: speakers,
            microphone: microphone,
            brightness: brightness,
            display: display,
            onStatus: onStatus,
            onEnded: { [weak self] ended in
                guard let self, self.current === ended else { return }
                self.current = nil
                self.currentTransport = nil
                self.onStatus(Self.waiting)
            })
        transport.session = session
        channel.setDelegate(transport)

        // A new connection replaces the old one: after a dropped link the phone reconnects before the old
        // channel has noticed it is dead.
        let previous = current
        current = session
        currentTransport = transport
        previous?.end()

        Timer.scheduledTimer(withTimeInterval: Self.helloTimeout, repeats: false) { [weak session] _ in
            session?.helloTimedOut()
        }
    }
}

/// One RFCOMM channel as the session's link to the phone.
final class RfcommTransport: NSObject, Transport, IOBluetoothRFCOMMChannelDelegate {
    weak var session: Session?
    private let channel: IOBluetoothRFCOMMChannel
    private var isClosed = false

    init(channel: IOBluetoothRFCOMMChannel) {
        self.channel = channel
        super.init()
    }

    /// Writes in chunks because an RFCOMM write may not exceed the channel's MTU.
    func send(_ bytes: [UInt8]) -> Bool {
        let mtu = Int(channel.getMTU())
        guard mtu > 0 else { return false }
        var offset = 0
        while offset < bytes.count {
            var chunk = Array(bytes[offset..<min(offset + mtu, bytes.count)])
            guard channel.writeSync(&chunk, length: UInt16(chunk.count)) == kIOReturnSuccess else { return false }
            offset += chunk.count
        }
        return true
    }

    func close() {
        guard !isClosed else { return }
        isClosed = true
        channel.setDelegate(nil)
        _ = channel.close()
    }

    func rfcommChannelData(
        _ rfcommChannel: IOBluetoothRFCOMMChannel!, data dataPointer: UnsafeMutableRawPointer!, length dataLength: Int
    ) {
        guard let dataPointer, dataLength > 0 else { return }
        let bytes = [UInt8](UnsafeRawBufferPointer(start: dataPointer, count: dataLength))
        session?.receive(bytes)
    }

    func rfcommChannelClosed(_ rfcommChannel: IOBluetoothRFCOMMChannel!) {
        session?.end()
    }
}
