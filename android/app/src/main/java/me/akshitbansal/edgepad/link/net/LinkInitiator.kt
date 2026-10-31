package me.akshitbansal.edgepad.link.net

import java.io.IOException
import java.io.InputStream
import java.io.OutputStream

/**
 * What a phone came away with: the laptop's status, and on success the encrypted channel and, after a pairing,
 * the laptop's id and the key to resume with, to keep in [LinkKeys]. A status of [LinkWire.STATUS_OK] with no
 * [channel] means the laptop did not prove it knew the keys: nothing more was sent, and the caller closes.
 */
class Initiated(
    val status: Int,
    val channel: SecureChannel?,
    val laptopId: ByteArray,
    val pairingKey: ByteArray,
)

/**
 * The phone's half of EDGL1, a port of the laptop suite's `shared/Edgepad.Core.Tests/LinkInitiator.cs`, step for
 * step as docs/PROTOCOL.md has it. Every call blocks and runs on the link thread; a malformed reply or a dropped
 * connection is an [IOException] and a bad laptop key a [java.security.GeneralSecurityException], and the caller
 * closes the transport after either.
 *
 * The constructor's two arguments exist so a test can replay the fixed keys and nonces of `protocol/link-vectors.txt`.
 */
class LinkInitiator(
    private val newKeyPair: () -> Ephemeral = LinkCrypto::newKeyPair,
    private val newNonce: () -> ByteArray = { LinkCrypto.randomBytes(LinkWire.NONCE_LENGTH) },
) {
    fun info(
        input: InputStream,
        output: OutputStream,
    ): LinkReply {
        LinkWire.write(output, LinkWire.infoRequest())
        return LinkWire.decodeReply(LinkWire.MODE_INFO, LinkWire.read(input))
    }

    /**
     * A pairing: commit to a fresh key, read the laptop's, reveal, then hand [showCode] the six digits for the
     * owner to type on the laptop. The phone sends nothing that depends on the digits. It then waits for the
     * laptop's confirmation and checks it against this transcript. That confirmation proves only that the peer ran
     * this exchange, not that anyone typed the code (a rogue laptop or a relay can send it too), so the phone then
     * asks its own owner through [confirmed] whether the laptop said the code matched, and answers with its own
     * confirmation only on a yes. A laptop that closes, confirms the wrong keys, or an owner who says no, leaves the
     * phone with no channel and no key.
     *
     * [expectedLaptopId], when the caller already knows which laptop it meant, ends the pairing before the reveal if
     * another one answers. [showCode] runs on this thread just before the long wait, so it is also where the caller
     * lengthens the read timeout for the owner's typing; [confirmed] blocks this thread until the owner answers.
     * To cancel, close the transport from another thread: the wait ends and this returns with no channel.
     */
    fun pair(
        input: InputStream,
        output: OutputStream,
        phoneId: ByteArray,
        name: String,
        expectedLaptopId: ByteArray?,
        confirmed: () -> Boolean,
        showCode: (Sas) -> Unit,
    ): Initiated {
        val own = newKeyPair()
        val nonce = newNonce()
        LinkWire.write(output, LinkWire.pairRequest(phoneId, name, LinkCrypto.commit(own.publicKey, nonce)))
        val offer = LinkWire.decodeReply(LinkWire.MODE_PAIR, LinkWire.read(input))
        if (offer.status != LinkWire.STATUS_OK) return Initiated(offer.status, null, offer.laptopId, NONE)
        if (expectedLaptopId != null && !offer.laptopId.contentEquals(expectedLaptopId)) {
            return Initiated(LinkWire.STATUS_OK, null, offer.laptopId, NONE)
        }

        LinkWire.write(output, LinkWire.reveal(own.publicKey, nonce))
        val transcript =
            LinkCrypto.pairTranscript(
                phoneId,
                offer.laptopId,
                LinkWire.nameField(name),
                offer.nameField,
                own.publicKey,
                nonce,
                offer.publicKey,
                offer.nonce,
            )
        val shared = LinkCrypto.agree(own, offer.publicKey)
        val prk = LinkCrypto.pairPrk(transcript, shared)
        shared.fill(0)
        try {
            showCode(LinkCrypto.sas(transcript))
            val tag =
                try {
                    LinkWire.decodeTag(LinkWire.read(input))
                } catch (e: IOException) {
                    // Closed, timed out, cancelled or malformed: either way the owner did not type this code there.
                    return Initiated(LinkWire.STATUS_OK, null, offer.laptopId, NONE)
                }
            if (!LinkCrypto.tagsEqual(tag, LinkCrypto.laptopTag(prk, transcript, pairing = true))) {
                return Initiated(LinkWire.STATUS_OK, null, offer.laptopId, NONE)
            }
            // The phone's owner saw the laptop accept the code, or the pairing ends here with nothing sent.
            if (!confirmed()) return Initiated(LinkWire.STATUS_OK, null, offer.laptopId, NONE)
            LinkWire.write(output, LinkWire.tag(LinkCrypto.phoneTag(prk, transcript, pairing = true)))
            val channel = SecureChannel(input, output, prk, isPhone = true)
            return Initiated(LinkWire.STATUS_OK, channel, offer.laptopId, LinkCrypto.pairingKey(prk))
        } finally {
            prk.fill(0)
        }
    }

    /** Every later connection: a fresh exchange mixed with the [pairingKey] a pairing with [laptopId] left. */
    fun resume(
        input: InputStream,
        output: OutputStream,
        phoneId: ByteArray,
        laptopId: ByteArray,
        pairingKey: ByteArray,
    ): Initiated {
        val own = newKeyPair()
        val nonce = newNonce()
        LinkWire.write(output, LinkWire.resumeRequest(phoneId, own.publicKey, nonce))
        val reply = LinkWire.decodeReply(LinkWire.MODE_RESUME, LinkWire.read(input))
        if (reply.status != LinkWire.STATUS_OK) return Initiated(reply.status, null, laptopId, NONE)

        val transcript =
            LinkCrypto.resumeTranscript(phoneId, laptopId, own.publicKey, nonce, reply.publicKey, reply.nonce)
        val shared = LinkCrypto.agree(own, reply.publicKey)
        val prk = LinkCrypto.resumePrk(transcript, shared, pairingKey)
        shared.fill(0)
        try {
            if (!LinkCrypto.tagsEqual(reply.tag, LinkCrypto.laptopTag(prk, transcript, pairing = false))) {
                return Initiated(LinkWire.STATUS_OK, null, laptopId, NONE)
            }
            LinkWire.write(output, LinkWire.tag(LinkCrypto.phoneTag(prk, transcript, pairing = false)))
            return Initiated(LinkWire.STATUS_OK, SecureChannel(input, output, prk, isPhone = true), laptopId, NONE)
        } finally {
            prk.fill(0)
        }
    }

    private companion object {
        val NONE = ByteArray(0)
    }
}
