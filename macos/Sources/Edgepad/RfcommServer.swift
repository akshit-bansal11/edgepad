import EdgepadCore
import Foundation
import IOBluetooth

/// Advertises Edgepad's RFCOMM service and hands each phone that connects, once Bluetooth has authenticated it, to
/// the `SessionHost`. Everything here runs on the main thread: IOBluetooth delivers its callbacks on the main run loop
/// and `Session` is single-threaded.
final class RfcommServer: NSObject {
    private static let l2capUuid16: UInt16 = 0x0100
    private static let rfcommUuid16: UInt16 = 0x0003
    private static let publicBrowseRootUuid16: UInt16 = 0x1002

    private let host: SessionHost
    private let onStatus: (String) -> Void

    private var serviceRecord: IOBluetoothSDPServiceRecord?
    private var notification: IOBluetoothUserNotification?
    /// The last start failure written to the log: start is retried, and must not repeat itself every time.
    private var lastStartFailure: String?

    init(host: SessionHost, onStatus: @escaping (String) -> Void) {
        self.host = host
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
        onStatus(SessionHost.waiting)
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
        notification?.unregister()
        notification = nil
        withdrawRecord()
    }

    /// Takes the SDP record down, so the Mac never advertises a service nothing listens on.
    private func withdrawRecord() {
        _ = serviceRecord?.remove()
        serviceRecord = nil
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
        // Turned away before authentication, the slow step: the session would refuse it after one anyway.
        if host.refuses(address) {
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
        // Held by the host until it ends; the transport only points at it.
        let session = host.accept(
            address: address, phone: PhoneLabel.describe(name: device.name, address: address), shownAs: nil,
            transport: transport)
        transport.session = session
        channel.setDelegate(transport)
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
