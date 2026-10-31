import CryptoKit
import Foundation

/// The Mac's half of pairing: a window that names the phone and asks the owner to type the code the phone shows. It
/// must not block. At most one of the callbacks is called, on the main thread: the digits typed, or the owner
/// cancelling. The question it returns is then told how the typed code went.
public protocol PairingPrompt: AnyObject {
    func show(
        phoneName: String, onTyped: @escaping (String) -> Void, onCancelled: @escaping () -> Void
    ) -> PairingQuestion
}

/// One pairing question on screen, the twin of the C# `IPairingQuestion`. After the owner types a code it is told
/// either `mismatched`, or `finished` (with `matched` before it when the code matched), each at most once, on the main
/// thread. `withdraw` takes down a question still asking for the code, and may be called more than once; once the
/// question has been told an outcome, the window stays up to show it.
public protocol PairingQuestion: AnyObject {
    /// The code matched and the phone was told so; now the phone's owner confirms on the phone.
    func matched()
    /// The code typed is not the one the phone shows; nothing was paired.
    func mismatched()
    /// The pairing ended after the code was typed: paired, or not (no confirmation, or a Forget).
    func finished(paired: Bool)
    func withdraw()
}

/// A network link that came through: the secret its records are keyed from, who is on the other end, and whatever the
/// phone sent after its last cleartext message, which is already the first of its records.
public struct LinkResult {
    public let prk: [UInt8]
    /// `link:<phone id hex>`, what the trust file, the PIN and the log call the phone.
    public let address: String
    public let phoneName: String
    public let justPaired: Bool
    public let leftover: [UInt8]
}

/// Everything a Mac does with a network connection before its first frame, the twin of the C# `LinkResponder`, as a
/// state machine fed the bytes the connection delivers: the Mac's `Session` is driven the same way.
///
/// INFO answers who this Mac is and closes. PAIR runs an ephemeral P-256 exchange in which the phone commits to its
/// key before it sees the Mac's, so a relay in the middle cannot steer both legs to one code; the phone then shows six
/// digits from the transcript, and the owner types them here. The Mac proves it matched before the phone proves
/// anything, and only after the phone's own proof is the phone trusted and its pairing key kept. Nothing in a pairing
/// is decided by the phone alone: any device on the network can run the exchange and work out its own code, but only
/// the owner at this Mac can type it. RESUME mixes that key into a fresh exchange, and both ends prove they hold it.
///
/// `onDone` is called exactly once: with a result, or with nil, after which the caller closes the connection. Nothing
/// secret is logged: not the code, not a key, only the phone's link address. Main thread only; the timers are the
/// app's, through `schedule`, so a test can fire them by hand.
public final class LinkResponder {
    /// How long the phone may take over each message.
    public static let messageTimeoutSeconds: TimeInterval = 10
    /// How long the owner has to type the code.
    public static let ownerTimeoutSeconds: TimeInterval = 60

    private enum Stage {
        case request
        case reveal
        /// The code is on the Mac's screen; the phone has nothing to say until the Mac confirms.
        case owner
        case pairConfirm
        case resumeConfirm
        case done
    }

    /// The pairing in progress, which holds the limiter's one slot until `finish`.
    private struct Pairing {
        let phoneId: [UInt8]
        let name: String
        let nameField: [UInt8]
        let commit: [UInt8]
        let own: P256.KeyAgreement.PrivateKey
        let nonce: [UInt8]
        /// Read when the pairing started: a Forget moves them, and the pairing then keeps and trusts nothing.
        let trustGeneration: Int
        let pairedGeneration: Int
    }

    private let identity: LinkIdentity
    private let paired: PairedLinks
    private let limiter: PairingLimiter
    private let prompt: PairingPrompt
    private let trust: TrustStore
    private let send: ([UInt8]) -> Bool
    private let schedule: (TimeInterval, @escaping () -> Void) -> Void
    private let onDone: (LinkResult?) -> Void

    private var stage = Stage.request
    /// Moved on by every wait, so a timer armed for an earlier one does nothing.
    private var step = 0
    private var reader = LinkMessageReader()
    private var address = "a network phone"
    private var phoneName = ""
    private var pairing: Pairing?
    private var transcript: [UInt8] = []
    private var prk: [UInt8] = []
    private var question: PairingQuestion?

    /// Starts waiting for the phone's request at once. `send` takes a whole message or record and is false when the
    /// connection is dead; `schedule` runs its closure once after the delay.
    public init(
        identity: LinkIdentity,
        paired: PairedLinks,
        limiter: PairingLimiter,
        prompt: PairingPrompt,
        trust: TrustStore,
        send: @escaping ([UInt8]) -> Bool,
        schedule: @escaping (TimeInterval, @escaping () -> Void) -> Void,
        onDone: @escaping (LinkResult?) -> Void
    ) {
        self.identity = identity
        self.paired = paired
        self.limiter = limiter
        self.prompt = prompt
        self.trust = trust
        self.send = send
        self.schedule = schedule
        self.onDone = onDone
        wait("request")
    }

    /// The address a network phone goes by in the trust file, the PIN gate and the log.
    public static func address(of phoneId: [UInt8]) -> String {
        "link:" + Hex.string(phoneId)
    }

    /// Bytes from the phone, in arrival order.
    public func receive(_ bytes: [UInt8]) {
        guard stage != .done else { return }
        reader.append(bytes)
        do {
            while stage != .done {
                // While the owner is typing the phone has been told nothing to answer.
                if stage == .owner {
                    if !reader.remainder.isEmpty {
                        refuse("it sent more before the code was typed on this Mac")
                    }
                    return
                }
                guard let body = try reader.next() else { return }
                try handle(body)
            }
        } catch LinkCryptoError.badKey {
            refuse("its key is not a valid P-256 point")
        } catch {
            refuse("\(error)")
        }
    }

    /// The phone closed the connection (`byPeer`), or this Mac stopped it: the listener stopping, a Forget or another
    /// owner dialog. A phone that hangs up mid-pairing counts against the lock, whatever the stage, or it could put
    /// the pairing question on the owner's screen again and again for free; this Mac stopping it does not count.
    public func cancel(byPeer: Bool) {
        guard stage != .done else { return }
        let why =
            byPeer
            ? "Refused to pair \(address): it closed the network link"
            : "Stopped pairing \(address): this Mac stopped it"
        finish(nil, outcome: byPeer ? .failed : .abandoned, pairing == nil ? nil : why)
    }

    /// A responder let go of mid-pairing still frees the one pairing slot, counted as a failure as a phone that hung
    /// up would be. The app never does this; it ends each one through `cancel`.
    deinit {
        if stage != .done, pairing != nil {
            question?.withdraw()
            limiter.end(.failed)
        }
    }

    /// Whether a Forget came after this pairing started, so that it must not trust or keep the phone.
    private func forgottenSince(_ pairing: Pairing) -> Bool {
        trust.forgetGeneration != pairing.trustGeneration || paired.generation != pairing.pairedGeneration
    }

    private func handle(_ body: [UInt8]) throws {
        switch stage {
        case .request:
            try request(LinkWire.decodeRequest(body))
        case .reveal:
            try reveal(body)
        case .pairConfirm:
            try confirm(body, pairing: true)
        case .resumeConfirm:
            try confirm(body, pairing: false)
        case .owner, .done:
            break
        }
    }

    private func request(_ request: LinkRequest) throws {
        switch request {
        case .unsupported(let version):
            _ = send(LinkWire.message(LinkWire.status(LinkWire.statusUnsupported)))
            finish(
                nil, outcome: .failed,
                "Refused a network phone on link version \(version); this Mac speaks \(LinkWire.linkVersion)")
        case .info:
            _ = send(
                LinkWire.message(
                    LinkWire.infoReply(
                        laptopId: identity.id, protocolVersion: ProtocolConstants.version, name: identity.name)))
            finish(nil, outcome: .failed, nil)
        case .pair(let phoneId, let name, let nameField, let commit):
            address = Self.address(of: phoneId)
            phoneName = name
            guard limiter.tryBegin() else {
                _ = send(LinkWire.message(LinkWire.status(LinkWire.statusBusy)))
                refuse("another pairing is under way, or pairing is locked after failed attempts")
                return
            }
            let started = Pairing(
                phoneId: phoneId, name: name, nameField: nameField, commit: commit, own: LinkCrypto.newKeyPair(),
                nonce: Hex.random(LinkWire.nonceLength), trustGeneration: trust.forgetGeneration,
                pairedGeneration: paired.generation)
            pairing = started
            let offer = LinkWire.pairOffer(
                laptopId: identity.id, name: identity.name, publicKey: LinkCrypto.publicKey(started.own),
                nonce: started.nonce)
            guard send(LinkWire.message(offer)) else {
                refuse("the link stopped taking data")
                return
            }
            stage = .reveal
            wait("key")
        case .resume(let phoneId, let publicKey, let nonce):
            address = Self.address(of: phoneId)
            guard let found = paired.find(Hex.string(phoneId)) else {
                _ = send(LinkWire.message(LinkWire.status(LinkWire.statusUnknownPhone)))
                refuse("it is not paired with this Mac, or the pairings cannot be read")
                return
            }
            phoneName = found.name
            let own = LinkCrypto.newKeyPair()
            let ownPublic = LinkCrypto.publicKey(own)
            let ownNonce = Hex.random(LinkWire.nonceLength)
            transcript = LinkCrypto.resumeTranscript(
                phoneId: phoneId, laptopId: identity.id, phonePublic: publicKey, phoneNonce: nonce,
                laptopPublic: ownPublic, laptopNonce: ownNonce)
            let shared = try LinkCrypto.agree(own, publicKey)
            prk = LinkCrypto.resumePrk(transcript, shared, found.key)
            let tag = LinkCrypto.laptopTag(prk, transcript, pairing: false)
            guard send(LinkWire.message(LinkWire.resumeReply(publicKey: ownPublic, nonce: ownNonce, tag: tag))) else {
                refuse("the link stopped taking data")
                return
            }
            stage = .resumeConfirm
            wait("confirmation")
        }
    }

    /// The phone's key and nonce. They must open its commitment; then the code goes to the owner.
    private func reveal(_ body: [UInt8]) throws {
        guard let pairing else { return }
        let (phonePublic, phoneNonce) = try LinkWire.decodeReveal(body)
        guard LinkCrypto.fixedTimeEquals(LinkCrypto.commit(phonePublic, phoneNonce), pairing.commit) else {
            refuse("its key is not the one it committed to")
            return
        }
        transcript = LinkCrypto.pairTranscript(
            phoneId: pairing.phoneId, laptopId: identity.id, phoneName: pairing.nameField,
            laptopName: LinkWire.nameField(identity.name), phonePublic: phonePublic, phoneNonce: phoneNonce,
            laptopPublic: LinkCrypto.publicKey(pairing.own), laptopNonce: pairing.nonce)
        let shared = try LinkCrypto.agree(pairing.own, phonePublic)
        prk = LinkCrypto.pairPrk(transcript, shared)

        stage = .owner
        step += 1
        let asked = step
        schedule(Self.ownerTimeoutSeconds) { [weak self] in
            guard let self, self.stage == .owner, self.step == asked else { return }
            self.refuse("no code was typed within \(Int(Self.ownerTimeoutSeconds)) s")
        }
        question = prompt.show(
            phoneName: pairing.name,
            onTyped: { [weak self] typed in self?.typed(typed, asked) },
            onCancelled: { [weak self] in self?.ownerCancelled(asked) })
    }

    /// The owner's digits. The one check that needs the owner: until it passes, nothing that depends on it is sent.
    private func typed(_ digits: String, _ asked: Int) {
        guard stage == .owner, step == asked, let pairing else { return }
        if forgottenSince(pairing) {
            question?.finished(paired: false)
            forgotten()
            return
        }
        guard LinkCrypto.codeMatches(digits, LinkCrypto.sas(transcript)) else {
            question?.mismatched()
            refuse("the code typed on this Mac is not the one that phone shows")
            return
        }
        guard send(LinkWire.message(LinkWire.tag(LinkCrypto.laptopTag(prk, transcript, pairing: true)))) else {
            question?.finished(paired: false)
            refuse("the link stopped taking data")
            return
        }
        stage = .pairConfirm
        // From here `finish` tells the window how the pairing ended, whatever ends it.
        question?.matched()
        // The phone's owner confirms on the phone that this Mac said the code matched, the one check that tells the
        // phone it reached this Mac; that takes a person, so the wait is the owner's, not a message's.
        wait("confirmation", seconds: Self.ownerTimeoutSeconds)
    }

    /// The owner forgot the trusted phones while this one was pairing: the later word wins, and it is the owner's, so
    /// it does not count against the lock.
    private func forgotten() {
        finish(
            nil, outcome: .abandoned,
            "Did not pair \(address): the trusted phones were forgotten while it was pairing")
    }

    private func ownerCancelled(_ asked: Int) {
        guard stage == .owner, step == asked else { return }
        finish(nil, outcome: .abandoned, "The owner cancelled pairing \(address)")
    }

    /// The phone's own confirmation, the last cleartext message. Only now is a pairing trusted and kept.
    private func confirm(_ body: [UInt8], pairing isPairing: Bool) throws {
        let tag = try LinkWire.decodeTag(body)
        guard LinkCrypto.fixedTimeEquals(tag, LinkCrypto.phoneTag(prk, transcript, pairing: isPairing)) else {
            refuse(isPairing ? "it did not confirm the same keys" : "it does not hold the key this Mac paired it with")
            return
        }
        if isPairing, let pairing {
            if forgottenSince(pairing) {
                forgotten()
                return
            }
            let key = LinkCrypto.pairingKey(prk)
            let idHex = Hex.string(pairing.phoneId)
            if !paired.add(idHex, key: key, name: pairing.name, since: pairing.pairedGeneration) {
                guard paired.generation == pairing.pairedGeneration else {
                    forgotten()
                    return
                }
                Log.write(
                    "Paired \(address) for this connection only: the pairing could not be saved, so it will pair "
                        + "again next time")
            }
            guard trust.grant(address, since: pairing.trustGeneration) else {
                forgotten()
                return
            }
            Log.write("Paired \(address)")
        }
        let result = LinkResult(
            prk: prk, address: address, phoneName: phoneName, justPaired: isPairing, leftover: reader.remainder)
        finish(result, outcome: .paired, nil)
    }

    /// Arms the wait for the phone's next message.
    private func wait(_ what: String, seconds: TimeInterval = LinkResponder.messageTimeoutSeconds) {
        step += 1
        let waiting = step
        schedule(seconds) { [weak self] in
            guard let self, self.stage != .done, self.step == waiting else { return }
            self.finish(
                nil, outcome: .failed,
                "Gave up on \(self.address): no \(what) within \(Int(seconds)) s")
        }
    }

    /// Ends short of a link, logging why. In a pairing this counts against the lock.
    private func refuse(_ reason: String) {
        finish(nil, outcome: .failed, "Refused \(pairing == nil ? "" : "to pair ")\(address): \(reason)")
    }

    private func finish(_ result: LinkResult?, outcome: PairingOutcome, _ message: String?) {
        guard stage != .done else { return }
        if stage == .pairConfirm {
            question?.finished(paired: result != nil)
        }
        stage = .done
        step += 1
        question?.withdraw()
        question = nil
        if pairing != nil {
            limiter.end(outcome)
        }
        if let message {
            Log.write(message)
        }
        onDone(result)
    }
}
