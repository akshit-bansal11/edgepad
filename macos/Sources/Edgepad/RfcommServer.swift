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
    private static let waiting = "Waiting for your phone"
    private static let encryptionDisabled: BluetoothHCIEncryptionMode = 0
    /// Outlasts the phone's own twenty seconds of automatic retries, so after a Forget the trust question can only
    /// come back from someone deliberately tapping this Mac on the phone.
    private static let quietAfterForget: TimeInterval = 30

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
    private var quietUntil: TimeInterval?
    private var current: Session?
    /// Held for as long as its session is current: the channel only keeps its delegate weakly.
    private var currentTransport: RfcommTransport?
    /// Held while a session is current, so macOS does not throttle the timers and I/O of a windowless app.
    private var activity: NSObjectProtocol?
    /// The last start failure written to the log: start is retried, and must not repeat itself every time.
    private var lastStartFailure: String?

    init(trust: TrustStore, askTrust: @escaping (String) -> Bool, onStatus: @escaping (String) -> Void) {
        self.trust = trust
        self.askTrust = askTrust
        self.onStatus = onStatus
        super.init()
    }

    /// False when the service could not be published or the channel could not be listened on; the log says which.
    func start() -> Bool {
        guard let uuid = UUID(uuidString: ProtocolConstants.serviceId) else {
            return startFailed("the service id is not a UUID")
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
            return startFailed("the service record could not be published")
        }
        serviceRecord = published

        // macOS may reassign the channel, so ask the record what it was really given.
        var channelID: BluetoothRFCOMMChannelID = 0
        guard published.getRFCOMMChannelID(&channelID) == kIOReturnSuccess else {
            withdrawRecord()
            return startFailed("the published record has no RFCOMM channel")
        }
        guard
            let registered = IOBluetoothRFCOMMChannel.register(
                forChannelOpenNotifications: self,
                selector: #selector(channelOpened(_:channel:)),
                withChannelID: channelID,
                direction: kIOBluetoothUserNotificationChannelDirectionIncoming)
        else {
            withdrawRecord()
            return startFailed("could not listen for channel \(channelID)")
        }
        notification = registered

        lastStartFailure = nil
        Log.write("RFCOMM service advertising")
        onStatus(Self.waiting)
        pollTimer = Timer.scheduledTimer(withTimeInterval: Self.pollInterval, repeats: true) { [weak self] _ in
            self?.current?.poll()
        }
        return true
    }

    private func startFailed(_ reason: String) -> Bool {
        if reason != lastStartFailure {
            Log.write("RFCOMM start failed: \(reason)")
            lastStartFailure = reason
        }
        return false
    }

    func stop() {
        endActivity()
        pollTimer?.invalidate()
        pollTimer = nil
        notification?.unregister()
        notification = nil
        withdrawRecord()
        current?.end()
    }

    /// Takes the SDP record down, so the Mac never advertises a service nothing listens on.
    private func withdrawRecord() {
        _ = serviceRecord?.remove()
        serviceRecord = nil
    }

    private func endActivity() {
        if let held = activity {
            ProcessInfo.processInfo.endActivity(held)
            activity = nil
        }
    }

    /// Hangs up on the connected phone, if there is one.
    func disconnect() {
        current?.end()
    }

    /// Refuses every new connection for a while: a phone just forgotten would otherwise reconnect by itself and
    /// raise the trust question under the cursor of an owner still clicking in the Forget dialog.
    func stayQuiet() {
        // Uptime, not the wall clock: a clock stepped backwards must not stretch the quiet period.
        quietUntil = ProcessInfo.processInfo.systemUptime + Self.quietAfterForget
        onStatus("Forgot the phone; ready for a new one in \(Int(Self.quietAfterForget)) seconds")
        Timer.scheduledTimer(withTimeInterval: Self.quietAfterForget, repeats: false) { [weak self] _ in
            guard let self, self.current == nil else { return }
            self.onStatus(Self.waiting)
        }
    }

    @objc private func channelOpened(_ notification: IOBluetoothUserNotification, channel: IOBluetoothRFCOMMChannel) {
        let device = channel.getDevice()

        // Windows demands an authenticated, encrypted socket; macOS has no such switch, so these are the checks.
        // Being paired only says the address is in this Mac's list, and a device can spoof an address. An
        // encrypted link can only exist with a device that holds the link key made at pairing, which an address
        // alone cannot prove.
        guard let device, let address = device.addressString, !address.isEmpty else {
            Log.write("Refused a device with no address")
            _ = channel.close()
            return
        }
        guard device.isPaired() else {
            Log.write("Refused \(address): it is not paired with this Mac")
            _ = channel.close()
            return
        }
        let mode = device.getEncryptionMode()
        guard mode != Self.encryptionDisabled else {
            Log.write("Refused \(address): the link is not encrypted (mode \(mode))")
            _ = channel.close()
            return
        }
        if let quietUntil, quietUntil > ProcessInfo.processInfo.systemUptime {
            Log.write("Refused \(address): this Mac was told to forget its phone a moment ago")
            _ = channel.close()
            return
        }
        // A device certain to be refused must never displace the connected phone.
        if trust.refuses(address) {
            Log.write(
                "Refused \(address) without disturbing the connected phone: it is not the trusted phone, "
                    + "or the owner is being asked about one or just declined it")
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
                self.endActivity()
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
        // AllowingIdleSystemSleep on purpose, so a connected phone does not keep the Mac awake.
        if activity == nil {
            activity = ProcessInfo.processInfo.beginActivity(
                options: [.userInitiatedAllowingIdleSystemSleep, .latencyCritical],
                reason: "A phone is controlling this Mac")
        }
        previous?.end()

        Timer.scheduledTimer(withTimeInterval: Session.helloTimeoutSeconds, repeats: false) { [weak session] _ in
            session?.helloTimedOut()
        }
    }
}

/// One RFCOMM channel as the session's link to the phone. Writes are asynchronous and queue in a bounded outbox,
/// so a phone that stops reading cannot block the main thread that also runs input, timers and new connections.
/// Closing drains first: what is queued gets a couple of seconds to go out before the channel is really closed.
final class RfcommTransport: NSObject, Transport, IOBluetoothRFCOMMChannelDelegate {
    /// 64 KiB is far more than a healthy link ever queues (frames are at most 258 bytes); a phone that has let this
    /// much pile up has stopped reading, and the link is treated as dead.
    private static let maxQueuedBytes = 64 * 1024

    /// The channel is already closed by then, so the abandoned write has long since stopped.
    private static let inFlightGraceSeconds: TimeInterval = 10
    /// Long enough for a few small frames to leave over a healthy link, short enough that a stalled phone does not
    /// keep the channel open.
    private static let drainSeconds: TimeInterval = 2

    weak var session: Session?
    private let channel: IOBluetoothRFCOMMChannel
    private var isClosed = false
    /// Set once the channel itself is closed; `isClosed` alone means only that no more may be sent.
    private var hungUp = false
    private var drainTimer: Timer?
    /// Chunks, each no larger than the MTU, oldest first.
    private var outbox: [Data] = []
    /// Outbox plus the chunk in flight.
    private var queuedBytes = 0
    /// Kept alive until IOBluetooth reports the write complete: it is given a pointer into it.
    private var inFlight: NSMutableData?

    init(channel: IOBluetoothRFCOMMChannel) {
        self.channel = channel
        super.init()
    }

    /// Queues in chunks because an RFCOMM write may not exceed the channel's MTU. False means the link is dead.
    func send(_ bytes: [UInt8]) -> Bool {
        guard !isClosed else { return false }
        let mtu = Int(channel.getMTU())
        guard mtu > 0, queuedBytes + bytes.count <= Self.maxQueuedBytes else { return false }
        var offset = 0
        while offset < bytes.count {
            let end = min(offset + mtu, bytes.count)
            outbox.append(Data(bytes[offset..<end]))
            offset = end
        }
        queuedBytes += bytes.count
        return pump()
    }

    /// Starts the next write unless one is already in flight; its completion starts the one after.
    private func pump() -> Bool {
        guard inFlight == nil, !outbox.isEmpty else { return true }
        let chunk = NSMutableData(data: outbox.removeFirst())
        inFlight = chunk
        let result = channel.writeAsync(chunk.mutableBytes, length: UInt16(chunk.length), refcon: nil)
        guard result == kIOReturnSuccess else {
            Log.write("A Bluetooth write was refused (\(result))")
            inFlight = nil
            return false
        }
        return true
    }

    /// Lets what is already queued go out for a moment before hanging up: on a version mismatch the session sends
    /// its HELLO_ACK and ends at once, and the phone needs that ack to say which side must be updated.
    func close() {
        guard !isClosed else { return }
        isClosed = true
        // A write error leaves nothing in flight, and nothing else would pump what is still queued.
        if inFlight == nil && !outbox.isEmpty {
            if !pump() {
                hangUp()
                return
            }
        }
        guard inFlight != nil || !outbox.isEmpty else {
            hangUp()
            return
        }
        // Strong on purpose: the session and the server drop their references the moment the session ends, so
        // this timer is what keeps the transport alive until the channel is really closed.
        drainTimer = Timer.scheduledTimer(withTimeInterval: Self.drainSeconds, repeats: false) { _ in
            self.hangUp()
        }
    }

    private func hangUp() {
        guard !hungUp else { return }
        hungUp = true
        drainTimer?.invalidate()
        drainTimer = nil
        channel.setDelegate(nil)
        _ = channel.close()
        if let abandoned = inFlight {
            // IOBluetooth holds a raw pointer into this buffer until the write ends, and with the delegate gone it
            // will never say when. The closure owns the buffer until the grace period is over, whatever happens
            // to this transport in the meantime.
            DispatchQueue.main.asyncAfter(deadline: .now() + Self.inFlightGraceSeconds) {
                withExtendedLifetime(abandoned) {}
            }
        }
        inFlight = nil
        outbox.removeAll()
        queuedBytes = 0
    }

    func rfcommChannelWriteComplete(
        _ rfcommChannel: IOBluetoothRFCOMMChannel!, refcon: UnsafeMutableRawPointer!, status error: IOReturn
    ) {
        guard !hungUp else { return }
        if error != kIOReturnSuccess {
            Log.write("A Bluetooth write failed (\(error))")
        }
        queuedBytes -= inFlight?.length ?? 0
        inFlight = nil
        if isClosed {
            if error != kIOReturnSuccess || !pump() || (inFlight == nil && outbox.isEmpty) {
                hangUp()
            }
        } else if error != kIOReturnSuccess || !pump() {
            session?.end()
        }
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
        hangUp()
    }
}
