import EdgepadCore
import Foundation
import Network

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

    private let identity: LinkIdentity
    private let paired: PairedLinks
    private let trust: TrustStore
    private let prompt: PairingPrompt
    private let host: SessionHost
    private let onPaired: (String) -> Void
    /// Outlives a stop and a start, so turning the listener off and on again does not lift a pairing lock.
    private let limiter = PairingLimiter()

    private var listener: NWListener?
    /// Connections still in EDGL1, each with the responder its bytes go to.
    private var unproved: [ObjectIdentifier: (link: NetworkTransport, responder: LinkResponder)] = [:]
    /// Links handed on, so stopping closes them too: the owner turning Wi-Fi and USB off means those phones as well.
    private var live: [NetworkTransport] = []

    init(
        identity: LinkIdentity,
        paired: PairedLinks,
        trust: TrustStore,
        prompt: PairingPrompt,
        host: SessionHost,
        onPaired: @escaping (String) -> Void
    ) {
        self.identity = identity
        self.paired = paired
        self.trust = trust
        self.prompt = prompt
        self.host = host
        self.onPaired = onPaired
    }

    /// Listens on `port`, or on a free one when that is taken, and announces this Mac; whatever ran before is stopped
    /// first. `report` is told, once listening is settled, what the menu's hint should say.
    func start(port: Int, report: @escaping (String) -> Void) {
        stop()
        listen(on: NWEndpoint.Port(rawValue: UInt16(clamping: port)), preferred: port, report: report)
    }

    /// Stops listening and announcing, and closes every connection it took.
    func stop() {
        let listening = listener
        listener = nil
        listening?.cancel()
        // Copied first: each responder's end removes its own entry.
        let pending = unproved
        unproved = [:]
        for entry in pending.values {
            entry.responder.cancel()
            entry.link.close()
        }
        for link in live {
            link.close()
        }
        live = []
        if listening != nil {
            Log.write("Stopped listening for Wi-Fi and USB phones")
        }
    }

    private func listen(on port: NWEndpoint.Port?, preferred: Int, report: @escaping (String) -> Void) {
        let opened: NWListener
        do {
            if let port {
                opened = try NWListener(using: .tcp, on: port)
            } else {
                opened = try NWListener(using: .tcp)
            }
        } catch {
            Log.write("Could not listen for Wi-Fi and USB phones: \(error)")
            report("Could not listen; see the log")
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
                    report("Port in use, using \(got)")
                } else {
                    Log.write("Listening for Wi-Fi and USB phones on port \(got)")
                    report("Listening on \(got)")
                }
            case .failed(let error):
                self.listener = nil
                opened.cancel()
                if port != nil, case .posix(.EADDRINUSE) = error {
                    self.listen(on: nil, preferred: preferred, report: report)
                } else {
                    Log.write("Stopped listening for Wi-Fi and USB phones: \(error)")
                    report("Could not listen; see the log")
                }
            default:
                break
            }
        }
        listener = opened
        opened.start(queue: .main)
    }

    private func accept(_ connection: NWConnection) {
        guard unproved.count < Self.maxUnproved else {
            Log.write("Refused a network connection: \(Self.maxUnproved) others are still proving themselves")
            connection.cancel()
            return
        }
        let link = NetworkTransport(connection: connection)
        let key = ObjectIdentifier(link)
        let responder = LinkResponder(
            identity: identity,
            paired: paired,
            limiter: limiter,
            prompt: prompt,
            trust: trust,
            send: { [weak link] bytes in link?.send(bytes) ?? false },
            schedule: { delay, run in
                _ = Timer.scheduledTimer(withTimeInterval: delay, repeats: false) { _ in run() }
            },
            onDone: { [weak self, weak link] result in
                guard let self, let link else { return }
                self.unproved[key] = nil
                self.proved(link, result)
            })
        unproved[key] = (link: link, responder: responder)
        link.onBytes = { [weak responder] bytes in responder?.receive(bytes) }
        link.onClosed = { [weak responder] in responder?.cancel() }
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
        let shownAs = "\(name.isEmpty ? "A phone" : name) · over Wi-Fi or USB"
        let secure = SecureTransport(inner: link, prk: result.prk)
        let session = host.accept(address: address, phone: shownAs, shownAs: shownAs, transport: secure)
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
        live.removeAll { $0.isClosed }
        live.append(link)
        if !result.leftover.isEmpty {
            link.onBytes?(result.leftover)
        }
    }
}

/// One TCP connection from a phone, first carrying EDGL1's cleartext messages and then its records. Writes queue in
/// Network.framework; past 64 KiB queued the phone has stopped reading and the link is treated as dead, as over
/// RFCOMM. Closing lets what is queued go out first, so a refused phone still reads the status byte that says why.
final class NetworkTransport: Transport {
    /// Far more than a healthy link ever queues: frames are at most 258 bytes, and records add 18 to each write.
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
