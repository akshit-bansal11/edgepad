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
    /// Outlasts the phone's own twenty seconds of automatic retries, so after a Forget the trust question can only
    /// come back from someone deliberately tapping this Mac on the phone.
    private static let quietAfterForget: TimeInterval = 30
    /// Outlasts the phone's own automatic retries (ten attempts, two seconds apart), so a phone another one just took
    /// over from stops by itself instead of taking this Mac straight back, and two trusted phones cannot displace
    /// each other for ever through their reconnects.
    private static let quietAfterHandOff: TimeInterval = 30

    private let trust: TrustStore
    private let pin: PinStore
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
    /// Set while the owner is deciding whether to forget; separate from the quiet period so that ending one cannot
    /// end the other.
    private var held = false
    private var current: Session?
    /// Held for as long as its session is current: the channel only keeps its delegate weakly.
    private var currentTransport: RfcommTransport?
    /// Connections still in their handshake, held the same way. None is current until trust has admitted it, so a
    /// phone that merely connects never disturbs the one in use.
    private var candidates: [(session: Session, transport: RfcommTransport)] = []
    /// Every phone that lost this Mac to another one within the quiet period, by lowercased address, and the uptime
    /// at which it may connect again. One entry each, so three trusted phones cannot take turns displacing each other.
    private var handedOff: [String: TimeInterval] = [:]
    /// Held while a session is current, so macOS does not throttle the timers and I/O of a windowless app.
    private var activity: NSObjectProtocol?
    /// The last start failure written to the log: start is retried, and must not repeat itself every time.
    private var lastStartFailure: String?

    /// `pin` defaults to the owner's own settings; a caller that also changes them hands in the same store, so the
    /// lockout counts and the check that a PIN changed mid-answer see one another.
    init(
        trust: TrustStore,
        pin: PinStore = .forCurrentUser(),
        askTrust: @escaping (String) -> Bool,
        onStatus: @escaping (String) -> Void
    ) {
        self.trust = trust
        self.pin = pin
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
        endAll()
    }

    /// Ends the phone in use and every connection still in its handshake.
    private func endAll() {
        current?.end()
        for candidate in candidates {
            candidate.session.end()
        }
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

    /// Hangs up and refuses every new connection until `resume()`. A phone that is still trusted reconnects
    /// within seconds, so hanging up alone would not keep it out while the owner decides.
    func holdOff() {
        held = true
        endAll()
    }

    /// Ends a `holdOff()`; a quiet period started by `stayQuiet()` runs on.
    func resume() {
        held = false
    }

    /// Whether new connections are being refused, by a hold or a quiet period.
    private var refusing: Bool {
        held || quietUntil.map { $0 > ProcessInfo.processInfo.systemUptime } ?? false
    }

    /// Refuses every new connection for a while: a phone just forgotten would otherwise reconnect by itself and
    /// raise the trust question under the cursor of an owner still clicking in the Forget dialog.
    func stayQuiet() {
        // Nothing should be connected after `holdOff()`, and if something is it must not outlive the Forget. Ended
        // first because it posts the waiting status, which the status below must replace.
        endAll()
        // Uptime, not the wall clock: a clock stepped backwards must not stretch the quiet period. Never shortens
        // one already running.
        quietUntil = max(quietUntil ?? 0, ProcessInfo.processInfo.systemUptime + Self.quietAfterForget)
        onStatus("Forgot the trusted phones; ready for a new one in \(Int(Self.quietAfterForget)) seconds")
        Timer.scheduledTimer(withTimeInterval: Self.quietAfterForget, repeats: false) { [weak self] _ in
            guard let self, self.current == nil else { return }
            self.onStatus(Self.waiting)
        }
    }

    @objc private func channelOpened(_ notification: IOBluetoothUserNotification, channel: IOBluetoothRFCOMMChannel) {
        let device = channel.getDevice()

        // A paired address alone is not proof of identity: authenticate the live link before starting a session.
        // Android requests an authenticated, encrypted RFCOMM socket. Do not gate on getEncryptionMode(): on
        // macOS 27 it returns 0 even when bluetoothd reports this same RFCOMM link as AES-CCM encrypted.
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
        if refusing {
            Log.write("Refused \(address): this Mac is forgetting its trusted phones, or just forgot them")
            _ = channel.close()
            return
        }
        if refusesHandedOff(address) {
            _ = channel.close()
            return
        }
        // Turned away before the handshake: the session would refuse it after one anyway.
        if trust.refuses(address) {
            Log.write(
                "Refused \(address) without disturbing the connected phone: the owner is being asked about a phone "
                    + "or just declined this one, or the trust file cannot be read")
            _ = channel.close()
            return
        }

        // Timed on uptime, as the quiet periods are: this is the slow step before a session starts.
        let started = ProcessInfo.processInfo.systemUptime
        let authentication = device.requestAuthentication()
        let milliseconds = Int((ProcessInfo.processInfo.systemUptime - started) * 1000)
        guard authentication == kIOReturnSuccess else {
            Log.write("Refused \(address): Bluetooth authentication failed (\(authentication))")
            _ = channel.close()
            return
        }
        Log.write("Authenticated RFCOMM connection from \(address) in \(milliseconds) ms")

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
            pin: pin,
            askTrust: { _ in ask(phone) },
            // Beside the hello timer below, and for each PIN_REQUEST: the session ignores one whose question was
            // already answered.
            schedulePinTimeout: { timedOut in
                _ = Timer.scheduledTimer(withTimeInterval: Session.pinTimeoutSeconds, repeats: false) { _ in
                    timedOut()
                }
            },
            dispatcher: dispatcher,
            input: injector,
            speakers: speakers,
            microphone: microphone,
            brightness: brightness,
            display: display,
            onStatus: onStatus,
            onAdmitted: { [weak self] admitted in self?.admit(admitted) ?? false },
            onEnded: { [weak self] ended in self?.sessionEnded(ended) })
        transport.session = session
        channel.setDelegate(transport)
        // Not current yet: the phone in use is left alone until trust has admitted this one.
        candidates.append((session: session, transport: transport))

        Timer.scheduledTimer(withTimeInterval: Session.helloTimeoutSeconds, repeats: false) { [weak session] _ in
            session?.helloTimedOut()
        }
    }

    /// True, and logged, when `address` is a phone another one took over from within the quiet period. Expired
    /// entries are dropped first, so the record never outgrows the phones displaced in the last half minute.
    private func refusesHandedOff(_ address: String) -> Bool {
        let now = ProcessInfo.processInfo.systemUptime
        handedOff = handedOff.filter { $0.value > now }
        guard handedOff[address.lowercased()] != nil else { return false }
        Log.write(
            "Refused \(address): another phone took over this Mac less than \(Int(Self.quietAfterHandOff)) seconds "
                + "ago")
        return true
    }

    /// Trust has said yes to `candidate`, which now takes over from the phone in use, if any. False when this Mac
    /// started refusing connections while the owner was being asked, or when the candidate is a phone just taken
    /// over from: a connection it opened just before the hand-off must not take this Mac straight back.
    private func admit(_ candidate: Session) -> Bool {
        guard !refusing, !refusesHandedOff(candidate.address) else { return false }
        guard let index = candidates.firstIndex(where: { $0.session === candidate }) else { return false }
        let transport = candidates.remove(at: index).transport
        let previous = current
        current = candidate
        currentTransport = transport
        // The same address is the phone replacing its own link: after a drop it reconnects before the old channel
        // has noticed it is dead, and there is no other phone to keep out.
        if let previous, previous.address.caseInsensitiveCompare(candidate.address) != .orderedSame {
            handedOff[previous.address.lowercased()] = ProcessInfo.processInfo.systemUptime + Self.quietAfterHandOff
        }
        // AllowingIdleSystemSleep on purpose, so a connected phone does not keep the Mac awake.
        if activity == nil {
            activity = ProcessInfo.processInfo.beginActivity(
                options: [.userInitiatedAllowingIdleSystemSleep, .latencyCritical],
                reason: "A phone is controlling this Mac")
        }
        previous?.end()
        return true
    }

    /// The phone in use ending leaves this Mac waiting. A candidate ending puts back the status of the phone still
    /// in use, which the candidate's question or refusal replaced.
    private func sessionEnded(_ ended: Session) {
        if current === ended {
            current = nil
            endActivity()
            currentTransport = nil
            onStatus(Self.waiting)
            return
        }
        guard let index = candidates.firstIndex(where: { $0.session === ended }) else { return }
        candidates.remove(at: index)
        onStatus(current.map { "Connected to \($0.address)" } ?? Self.waiting)
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
