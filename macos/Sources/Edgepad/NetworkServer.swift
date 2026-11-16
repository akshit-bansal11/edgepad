import EdgepadCore
import Foundation
import Network

/// What the Wi-Fi row knows about the listener. Only the last three are worth the owner's eye.
enum NetworkHint: Equatable {
    case off
    case starting
    case listening
    case portInUse(Int)
    case waiting
    case failed

    /// The row's hint, or nil when there is nothing to say.
    var problem: String? {
        switch self {
        case .off, .starting, .listening:
            return nil
        case .portInUse(let port):
            return tr("Net_PortInUse", "Port in use, using %1$@", "\(port)")
        case .waiting:
            return tr("Net_WaitingForNetwork", "Waiting for a network")
        case .failed:
            return tr("Net_CouldNotListen", "Could not listen; see the log")
        }
    }
}

/// Takes Wi-Fi and USB phones, the twin of the Windows and Linux `NetworkServer`: an `NWListener` for their
/// connections, announced over Bonjour as `_edgepad._tcp` so they can find this Mac. Every connection runs a
/// `LinkResponder`, and only a link it lets through, a phone paired here or pairing now with the owner typing its code,
/// is handed to the `SessionHost`, encrypted. Everything else is closed. The menu decides when this runs, from
/// `network.txt`. The listener and every connection deliver their callbacks on the main queue, so all of this runs on
/// the main thread, as `Session` requires.
final class NetworkServer {
    static let serviceType = "_edgepad._tcp"
    /// Connections still proving themselves, at most, as on Windows and Linux: past this a new one is closed at once.
    private static let maxUnproved = 4
    /// Of those, at most this many from one address, so one machine cannot take every slot.
    private static let maxUnprovedPerHost = 2

    /// TCP keep-alive on every accepted connection: a phone that left the network without a FIN (Wi-Fi dropped, USB
    /// tether pulled) otherwise keeps its session and any held key or button for ever. Idle 10 s, then 3 probes 5 s
    /// apart: a vanished phone fails the connection in about 25 s, which ends the session through the usual path.
    private static func listenerParameters() -> NWParameters {
        let tcp = NWProtocolTCP.Options()
        tcp.enableKeepalive = true
        tcp.keepaliveIdle = 10
        tcp.keepaliveInterval = 5
        tcp.keepaliveCount = 3
        return NWParameters(tls: nil, tcp: tcp)
    }

    private let identity: LinkIdentity
    private let paired: PairedLinks
    private let trust: TrustStore
    private let prompt: PairingPanel
    private let host: SessionHost
    private let onPaired: (String) -> Void
    /// Outlives a stop and a start, so turning the listener off and on again does not lift a pairing lock.
    private let limiter = PairingLimiter()

    private var listener: NWListener?
    /// Connections still in EDGL1, each with the responder its bytes go to.
    private var unproved: [ObjectIdentifier: (link: NetworkTransport, responder: LinkResponder, host: String)] = [:]
    /// Links that proved themselves and have not yet sent a first frame, which says whether they are a phone (HELLO) or
    /// its transfer socket (BULK_HELLO). Counted with `unproved` in the caps, and closed after the HELLO timeout if
    /// silent.
    private var awaiting: [ObjectIdentifier: (link: NetworkTransport, host: String)] = [:]
    /// A flood of refused connections logs one line per address per minute, not one per connection.
    private var refusals = RefusalThrottle()
    /// Links handed on, so stopping closes them too: the owner turning Wi-Fi and USB off means those phones as well.
    private var live: [NetworkTransport] = []

    init(
        identity: LinkIdentity,
        paired: PairedLinks,
        trust: TrustStore,
        prompt: PairingPanel,
        host: SessionHost,
        onPaired: @escaping (String) -> Void
    ) {
        self.identity = identity
        self.paired = paired
        self.trust = trust
        self.prompt = prompt
        self.host = host
        self.onPaired = onPaired
        // A Forget or another owner dialog ends every pairing still under way, so none can trust a phone after it.
        host.onHold = { [weak self] in self?.dropUnproved() }
    }

    /// Listens on `port`, or on a free one when that is taken, and announces this Mac; whatever ran before is stopped
    /// first. `report` is told, once listening is settled, what the menu's hint should say.
    func start(port: Int, report: @escaping (NetworkHint) -> Void) {
        stop()
        listen(on: NWEndpoint.Port(rawValue: UInt16(clamping: port)), preferred: port, report: report)
    }

    /// Stops listening and announcing, and closes every connection it took.
    func stop() {
        let listening = listener
        listener = nil
        listening?.cancel()
        dropUnproved()
        for link in live {
            link.close()
        }
        live = []
        if listening != nil {
            Log.write("Stopped listening for Wi-Fi and USB phones")
        }
    }

    /// Ends every connection still proving itself, a pairing question included, without counting it against the lock:
    /// this Mac stopped it, not the phone.
    private func dropUnproved() {
        // Copied first: each responder's end removes its own entry.
        let pending = unproved
        unproved = [:]
        for entry in pending.values {
            entry.responder.cancel(byPeer: false)
            entry.link.close()
        }
        let waiting = awaiting
        awaiting = [:]
        for entry in waiting.values {
            entry.link.close()
        }
    }

    private func listen(on port: NWEndpoint.Port?, preferred: Int, report: @escaping (NetworkHint) -> Void) {
        let opened: NWListener
        do {
            if let port {
                opened = try NWListener(using: Self.listenerParameters(), on: port)
            } else {
                opened = try NWListener(using: Self.listenerParameters())
            }
        } catch {
            Log.write("Could not listen for Wi-Fi and USB phones: \(error)")
            report(.failed)
            return
        }
        // A TXT record is length-prefixed strings; NetService's encoder is deprecated, and this is all it would do. The
        // name is cut well inside a string's 255 bytes.
        let entries = [
            "id=\(identity.idHex)", "n=\(identity.name.prefix(60))", "v=\(ProtocolConstants.version)", "lv=1",
        ]
        let txt = entries.flatMap { entry -> [UInt8] in
            let bytes = Array(entry.utf8.prefix(255))
            return [UInt8(bytes.count)] + bytes
        }
        opened.service = NWListener.Service(
            name: identity.name, type: Self.serviceType, domain: nil, txtRecord: Data(txt))
        opened.newConnectionHandler = { [weak self] connection in self?.accept(connection) }
        opened.stateUpdateHandler = { [weak self, weak opened] state in
            guard let self, let opened, self.listener === opened else { return }
            switch state {
            case .ready:
                let got = Int(opened.port?.rawValue ?? 0)
                if port == nil {
                    // Bonjour carries the real port, so Wi-Fi still finds it; a phone probing over USB tethering tries
                    // only the usual one.
                    Log.write(
                        "Listening for Wi-Fi and USB phones on port \(got), not \(preferred): a phone looking over "
                            + "USB tethering will not find this Mac")
                    report(.portInUse(got))
                } else {
                    Log.write("Listening for Wi-Fi and USB phones on port \(got)")
                    report(.listening)
                }
            case .waiting(let error):
                // No network yet: said, rather than left at Starting. It moves to ready by itself when one comes.
                Log.write("Waiting for a network to listen on: \(error)")
                report(.waiting)
            case .failed(let error):
                self.listener = nil
                opened.cancel()
                if port != nil, case .posix(.EADDRINUSE) = error {
                    self.listen(on: nil, preferred: preferred, report: report)
                } else {
                    Log.write("Stopped listening for Wi-Fi and USB phones: \(error)")
                    report(.failed)
                }
            default:
                break
            }
        }
        listener = opened
        opened.start(queue: .main)
    }

    /// "from 192.168.1.23, on your network". USB tethering is not supported on a Mac, and the Mac does not look up
    /// which adapter the address arrived on, so it names the network rather than guess Wi-Fi.
    static func sourceLine(_ endpoint: NWEndpoint) -> String {
        guard case .hostPort = endpoint else { return "" }
        let address = Self.address(endpoint)
        return StatusText.networkSource(address)
    }

    /// The address a connection came from, without a `%scope` suffix or an IPv4-mapped `::ffff:` prefix, so one
    /// machine counts as one host however it reaches this Mac.
    private static func address(_ endpoint: NWEndpoint) -> String {
        guard case .hostPort(let host, _) = endpoint else { return "" }
        let address = "\(host)".split(separator: "%").first.map(String.init) ?? ""
        return address.hasPrefix("::ffff:") ? String(address.dropFirst(7)) : address
    }

    /// Closes a connection refused for a cap; the log line goes out off the main thread, and only once per address per
    /// minute, so a flood stalls neither the pointer nor the disk.
    private func refuse(_ connection: NWConnection, from address: String, because reason: String) {
        connection.cancel()
        if refusals.shouldLog(host: address, now: ProcessInfo.processInfo.systemUptime) {
            Log.writeAsync("Refused a network connection from \(address): \(reason)")
        }
    }

    private func accept(_ connection: NWConnection) {
        // A connection the listener handed over just before it was stopped.
        guard listener != nil else {
            connection.cancel()
            return
        }
        let address = Self.address(connection.endpoint)
        guard unproved.count + awaiting.count < Self.maxUnproved else {
            refuse(connection, from: address, because: "\(Self.maxUnproved) others are still proving themselves")
            return
        }
        // Counted from the table itself, so nothing can drift when a connection ends.
        let fromHost =
            unproved.values.filter({ $0.host == address }).count + awaiting.values.filter({ $0.host == address }).count
        guard fromHost < Self.maxUnprovedPerHost else {
            refuse(
                connection, from: address,
                because: "\(Self.maxUnprovedPerHost) others from it are still proving themselves")
            return
        }
        let link = NetworkTransport(connection: connection)
        let key = ObjectIdentifier(link)
        let responder = LinkResponder(
            identity: identity,
            paired: paired,
            limiter: limiter,
            prompt: SourcedPrompt(panel: prompt, source: Self.sourceLine(connection.endpoint)),
            trust: trust,
            host: address.isEmpty ? nil : address,
            // While the owner is forgetting phones or has a dialog open, no pairing may even start; INFO is still
            // answered and a resume is refused by the session host, as on Windows.
            refusing: { [host] in host.isRefusing },
            send: { [weak link] bytes in link?.send(bytes) ?? false },
            schedule: { delay, run in
                // .common, so the timeout still fires while a menu is tracked or a modal is up.
                RunLoop.main.add(Timer(timeInterval: delay, repeats: false) { _ in run() }, forMode: .common)
            },
            onDone: { [weak self, weak link] result in
                guard let self, let link else { return }
                self.unproved[key] = nil
                self.proved(link, result)
            })
        unproved[key] = (link: link, responder: responder, host: address)
        link.onBytes = { [weak responder] bytes in responder?.receive(bytes) }
        link.onClosed = { [weak responder] in responder?.cancel(byPeer: true) }
        link.start()
    }

    /// The responder is done with `link`: on a result the phone is handed to the session host over the encrypted
    /// link, with whatever it sent after its last cleartext message; on nil the link is closed.
    private func proved(_ link: NetworkTransport, _ result: LinkResult?) {
        guard let result else {
            link.close()
            return
        }
        guard listener != nil else {
            Log.write("Refused \(result.address): Wi-Fi and USB connections were turned off")
            link.close()
            return
        }
        if result.justPaired {
            onPaired(PhoneLabel.clean(result.phoneName))
        }
        let address = result.address
        if host.refuses(address) {
            link.close()
            return
        }
        let name = PhoneLabel.clean(result.phoneName)
        let shownAs = name.isEmpty ? tr("Net_Phone", "A phone") : name
        let secure = SecureTransport(inner: link, prk: result.prk)
        live.removeAll { $0.isClosed }
        live.append(link)
        awaitFirstFrame(link, secure, address: address, shownAs: shownAs, leftover: result.leftover)
    }

    /// The link has proved itself, but what it is for is not known until its first frame: a HELLO makes a session, a
    /// BULK_HELLO attaches a transfer socket to the phone in use. Until then it counts among the connections still
    /// proving themselves (D-BULK), and one that says nothing is closed after the HELLO timeout.
    private func awaitFirstFrame(
        _ link: NetworkTransport, _ secure: SecureTransport, address: String, shownAs: String, leftover: [UInt8]
    ) {
        let key = ObjectIdentifier(link)
        awaiting[key] = (link: link, host: address)
        let timeout = Timer(timeInterval: Session.helloTimeoutSeconds, repeats: false) { [weak self, weak link] _ in
            guard let self, let link, self.awaiting.removeValue(forKey: key) != nil else { return }
            Log.write("Refused \(address): nothing came within \(Int(Session.helloTimeoutSeconds)) s of its handshake")
            link.close()
        }
        // .common, as every timeout here: a default-mode timer does not fire while a menu or a modal is up.
        RunLoop.main.add(timeout, forMode: .common)
        link.onClosed = { [weak self] in
            self?.awaiting[key] = nil
            timeout.invalidate()
        }
        var first: [UInt8] = []
        link.onBytes = { [weak self, weak link, weak secure] bytes in
            guard let self, let link, let secure, self.awaiting[key] != nil else { return }
            guard let plain = secure.open(bytes) else {
                Log.write("Refused \(address): a record failed its check, or was too long")
                self.awaiting[key] = nil
                timeout.invalidate()
                link.close()
                return
            }
            first += plain
            let kind = FirstFrame.classify(first)
            if kind == .needMore {
                return
            }
            self.awaiting[key] = nil
            timeout.invalidate()
            switch kind {
            case .needMore:
                break
            case .invalid:
                Log.write("Refused \(address): its first frame is not a HELLO or a BULK_HELLO")
                link.close()
            case .other:
                self.startSession(link, secure, address: address, shownAs: shownAs, first: first)
            case .bulk(let version, let rest):
                self.attachTransferSocket(link, secure, address: address, version: version, rest: rest)
            }
        }
        if !leftover.isEmpty {
            link.onBytes?(leftover)
        }
    }

    /// A HELLO's connection becomes a session, which is given the bytes already read.
    private func startSession(
        _ link: NetworkTransport, _ secure: SecureTransport, address: String, shownAs: String, first: [UInt8]
    ) {
        let session = host.accept(
            address: address, phone: shownAs, shownAs: shownAs, link: "Wi-Fi", transport: secure)
        link.onBytes = { [weak session, weak secure] bytes in
            guard let session, let secure else { return }
            guard let plain = secure.open(bytes) else {
                Log.write("Session with \(address) ended: a record failed its check, or was too long")
                session.end()
                return
            }
            if !plain.isEmpty {
                session.receive(plain)
            }
        }
        link.onClosed = { [weak session] in session?.end() }
        session.receive(first)
    }

    /// A BULK_HELLO's connection is handed to the phone in use, never made a session: the host attaches it only to the
    /// live session of this address that has no transfer socket yet, and it is closed here when none will take it.
    private func attachTransferSocket(
        _ link: NetworkTransport, _ secure: SecureTransport, address: String, version: UInt8, rest: [UInt8]
    ) {
        guard let bulk = host.attachBulk(address: address, version: version, transport: secure) else {
            Log.write("Refused \(address): a transfer socket with no connected phone of this address to attach to")
            link.close()
            return
        }
        link.onBytes = { [weak bulk, weak secure] bytes in
            guard let bulk, let secure else { return }
            guard let plain = secure.open(bytes) else {
                Log.write("Transfer socket of \(address) closed: a record failed its check, or was too long")
                bulk.close()
                return
            }
            if !plain.isEmpty {
                bulk.receive(plain)
            }
        }
        link.onClosed = { [weak bulk] in bulk?.close() }
        if !rest.isEmpty {
            bulk.receive(rest)
        }
    }
}

/// One TCP connection from a phone, first carrying EDGL1's cleartext messages and then its records. Writes queue in
/// Network.framework; past 64 KiB queued the phone has stopped reading and the link is treated as dead, as over
/// RFCOMM. Closing lets what is queued go out first, so a refused phone still reads the status byte that says why.
final class NetworkTransport: Transport {
    /// Twice what a sender of many frames lets queue (`CaptureToPhone.maxBacklog`), so a healthy link never gets here.
    private static let maxQueuedBytes = 64 * 1024
    /// Long enough for what is queued to leave over a healthy link, short enough that a stalled phone does not keep
    /// the connection open.
    private static let drainSeconds: TimeInterval = 2
    private static let readSize = 64 * 1024

    var onBytes: (([UInt8]) -> Void)?
    /// Called once, when the connection is gone, whichever end closed it.
    var onClosed: (() -> Void)?
    /// Set once nothing more may be sent.
    private(set) var isClosed = false

    private let connection: NWConnection
    private var queuedBytes = 0
    /// Set once the connection itself is cancelled.
    private var hungUp = false

    init(connection: NWConnection) {
        self.connection = connection
    }

    func start() {
        connection.stateUpdateHandler = { [weak self] state in
            switch state {
            case .failed(let error):
                Log.write("A network link failed: \(error)")
                self?.hangUp()
            case .waiting(let error):
                // An accepted connection with no usable path left: nothing will come over it.
                Log.write("A network link lost its path: \(error)")
                self?.hangUp()
            case .cancelled:
                self?.hangUp()
            default:
                break
            }
        }
        connection.start(queue: .main)
        receive()
    }

    private func receive() {
        connection.receive(minimumIncompleteLength: 1, maximumLength: Self.readSize) {
            [weak self] content, _, isComplete, error in
            guard let self, !self.hungUp else { return }
            if let content, !content.isEmpty {
                self.onBytes?(Array(content))
            }
            if isComplete || error != nil {
                self.hangUp()
            } else if !self.hungUp {
                self.receive()
            }
        }
    }

    var backlog: Int { queuedBytes }

    /// False means the link is dead, or so far behind that it may as well be.
    func send(_ bytes: [UInt8]) -> Bool {
        guard !isClosed, queuedBytes + bytes.count <= Self.maxQueuedBytes else { return false }
        queuedBytes += bytes.count
        connection.send(
            content: Data(bytes),
            completion: .contentProcessed { [weak self] error in
                guard let self else { return }
                self.queuedBytes -= bytes.count
                if let error, !self.hungUp {
                    Log.write("A network write failed: \(error)")
                    self.hangUp()
                }
            })
        return true
    }

    func close() {
        guard !isClosed else { return }
        isClosed = true
        guard !hungUp else { return }
        connection.send(
            content: nil, contentContext: .finalMessage, isComplete: true,
            completion: .contentProcessed { [weak self] _ in self?.hangUp() })
        // Strong on purpose: the session and the server drop their references the moment the session ends, so this is
        // what keeps the transport alive until the connection is really closed.
        DispatchQueue.main.asyncAfter(deadline: .now() + Self.drainSeconds) { self.hangUp() }
    }

    private func hangUp() {
        guard !hungUp else { return }
        hungUp = true
        isClosed = true
        connection.cancel()
        onBytes = nil
        let closed = onClosed
        onClosed = nil
        closed?()
    }
}

/// The shared pairing window, asked on behalf of one connection so its question can say where the request came from.
private final class SourcedPrompt: PairingPrompt {
    private let panel: PairingPanel
    private let source: String

    init(panel: PairingPanel, source: String) {
        self.panel = panel
        self.source = source
    }

    func show(
        phoneName: String, onTyped: @escaping (String) -> Void, onCancelled: @escaping () -> Void
    ) -> PairingQuestion {
        panel.show(phoneName: phoneName, source: source, onTyped: onTyped, onCancelled: onCancelled)
    }
}
