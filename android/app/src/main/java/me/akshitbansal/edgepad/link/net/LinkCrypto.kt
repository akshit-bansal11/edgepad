package me.akshitbansal.edgepad.link.net

import java.math.BigInteger
import java.nio.ByteBuffer
import java.security.GeneralSecurityException
import java.security.KeyFactory
import java.security.KeyPairGenerator
import java.security.MessageDigest
import java.security.SecureRandom
import java.security.interfaces.ECPrivateKey
import java.security.interfaces.ECPublicKey
import java.security.spec.ECGenParameterSpec
import java.security.spec.ECPoint
import java.security.spec.ECPublicKeySpec
import javax.crypto.KeyAgreement
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

/**
 * The arithmetic of EDGL1, ported from the laptop's `shared/Edgepad.Core/Net/Link/LinkCrypto.cs` and checked
 * against the same `protocol/link-vectors.txt`: P-256 ECDH, HKDF and HMAC over SHA-256, all platform crypto.
 * P-256 rather than X25519 because `KeyAgreement "X25519"` needs API 33 and this app runs from 31.
 *
 * Every comparison of a tag goes through [MessageDigest.isEqual], which takes the same time whatever the bytes:
 * a compare that stops at the first wrong byte tells a patient attacker how many leading bytes were right.
 */
object LinkCrypto {
    const val KEY_LENGTH = 32
    const val AEAD_TAG_LENGTH = 16
    const val NONCE_LENGTH = 12

    private val TRANSCRIPT_LABEL = "EDGL1 th".toByteArray()
    private val SAS_LABEL = "EDGL1 sas".toByteArray()
    private val COMMIT_LABEL = "EDGL1 commit".toByteArray()
    private val PAIR_LABEL = "pair".toByteArray()
    private val CONFIRM_LAPTOP = "EDGL1 confirm laptop".toByteArray()
    private val CONFIRM_PHONE = "EDGL1 confirm phone".toByteArray()
    private val PAIRING_KEY = "EDGL1 pairing key".toByteArray()
    private val KEY_P2L = "EDGL1 key p2l".toByteArray()
    private val KEY_L2P = "EDGL1 key l2p".toByteArray()
    private val IV = "EDGL1 iv".toByteArray()

    // P-256's field prime and b (SEC 2, section 2.4.2); a is -3.
    private val P = BigInteger("FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF", 16)
    private val B = BigInteger("5AC635D8AA3A93E7B3EBBD55769886BC651D06B0CC53B0F63BCE3C3E27D2604B", 16)
    private val THREE = BigInteger.valueOf(3)

    private const val CODE_MODULUS = 1_000_000L
    private const val COORDINATE_LENGTH = 32

    private val random = SecureRandom()

    fun randomBytes(count: Int): ByteArray = ByteArray(count).also { random.nextBytes(it) }

    /** A fresh key pair, for one connection only. */
    fun newKeyPair(): Ephemeral {
        val generator = KeyPairGenerator.getInstance("EC")
        generator.initialize(ECGenParameterSpec("secp256r1"), random)
        val pair = generator.generateKeyPair()
        val point = (pair.public as ECPublicKey).w
        val encoded = byteArrayOf(0x04) + coordinate(point.affineX) + coordinate(point.affineY)
        return Ephemeral(pair.private as ECPrivateKey, encoded)
    }

    /**
     * The raw shared secret, the X coordinate of the shared point, as the laptop's `DeriveRawSecretAgreement` gives
     * it. The peer's key must be a 65-byte uncompressed point on the curve, checked here by the curve equation
     * rather than left to the provider; the encoding cannot express the point at infinity. A bad key is a
     * [GeneralSecurityException] and the caller closes. The caller zeroes what this returns.
     */
    fun agree(
        own: Ephemeral,
        peerPublic: ByteArray,
    ): ByteArray {
        if (peerPublic.size != LinkWire.PUBLIC_KEY_LENGTH || peerPublic[0] != 0x04.toByte()) {
            throw GeneralSecurityException("The peer's key is not an uncompressed P-256 point")
        }
        val x = BigInteger(1, peerPublic.copyOfRange(1, 1 + COORDINATE_LENGTH))
        val y = BigInteger(1, peerPublic.copyOfRange(1 + COORDINATE_LENGTH, LinkWire.PUBLIC_KEY_LENGTH))
        if (!onCurve(x, y)) throw GeneralSecurityException("The peer's key is not on P-256")
        val peer = KeyFactory.getInstance("EC").generatePublic(ECPublicKeySpec(ECPoint(x, y), own.secret.params))
        val agreement = KeyAgreement.getInstance("ECDH")
        agreement.init(own.secret)
        agreement.doPhase(peer, true)
        return agreement.generateSecret()
    }

    /** True when (x, y) are both below p and satisfy y² = x³ − 3x + b (mod p). */
    private fun onCurve(
        x: BigInteger,
        y: BigInteger,
    ): Boolean {
        if (x >= P || y >= P) return false
        val right = (x.pow(3) - x * THREE + B).mod(P)
        return y.pow(2).mod(P) == right
    }

    /** HKDF-Extract (RFC 5869) with SHA-256. */
    fun extract(
        salt: ByteArray,
        ikm: ByteArray,
    ): ByteArray = hmac(if (salt.isEmpty()) ByteArray(KEY_LENGTH) else salt, ikm)

    /** HKDF-Expand (RFC 5869) with SHA-256. */
    fun expand(
        prk: ByteArray,
        info: ByteArray,
        length: Int,
    ): ByteArray {
        require(length in 1..255 * KEY_LENGTH) { "HKDF-Expand gives 1 to ${255 * KEY_LENGTH} bytes" }
        val out = ByteArray(length)
        var block = ByteArray(0)
        var at = 0
        var counter = 1
        while (at < length) {
            block = hmac(prk, block, info, byteArrayOf(counter.toByte()))
            val n = minOf(block.size, length - at)
            block.copyInto(out, at, 0, n)
            at += n
            counter++
        }
        return out
    }

    fun hash(vararg parts: ByteArray): ByteArray {
        val digest = MessageDigest.getInstance("SHA-256")
        for (part in parts) digest.update(part)
        return digest.digest()
    }

    fun hmac(
        key: ByteArray,
        vararg parts: ByteArray,
    ): ByteArray {
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(key, "HmacSHA256"))
        for (part in parts) mac.update(part)
        return mac.doFinal()
    }

    /** What the phone binds itself to before it sees the laptop's key and nonce. */
    fun commit(
        phonePublic: ByteArray,
        phoneNonce: ByteArray,
    ): ByteArray = hash(COMMIT_LABEL, phonePublic, phoneNonce)

    /** Everything both sides said in a pairing, in one hash; the names are the fields as sent, length byte included. */
    fun pairTranscript(
        phoneId: ByteArray,
        laptopId: ByteArray,
        phoneNameField: ByteArray,
        laptopNameField: ByteArray,
        phonePublic: ByteArray,
        phoneNonce: ByteArray,
        laptopPublic: ByteArray,
        laptopNonce: ByteArray,
    ): ByteArray =
        hash(
            TRANSCRIPT_LABEL,
            byteArrayOf(LinkWire.MODE_PAIR),
            phoneId,
            laptopId,
            phoneNameField,
            laptopNameField,
            phonePublic,
            phoneNonce,
            laptopPublic,
            laptopNonce,
        )

    /** A resume's transcript: no names travel in a resume, so none are in it. */
    fun resumeTranscript(
        phoneId: ByteArray,
        laptopId: ByteArray,
        phonePublic: ByteArray,
        phoneNonce: ByteArray,
        laptopPublic: ByteArray,
        laptopNonce: ByteArray,
    ): ByteArray =
        hash(
            TRANSCRIPT_LABEL,
            byteArrayOf(LinkWire.MODE_RESUME),
            phoneId,
            laptopId,
            phonePublic,
            phoneNonce,
            laptopPublic,
            laptopNonce,
        )

    /**
     * The six digits the phone shows and the laptop's owner types: the first four bytes of a hash of the
     * transcript, big-endian, mod a million. It proves only that both ends saw the same transcript.
     */
    fun sas(transcript: ByteArray): Sas {
        val value = (ByteBuffer.wrap(hash(SAS_LABEL, transcript)).int.toLong() and 0xFFFFFFFFL) % CODE_MODULUS
        return Sas(value.toString().padStart(Sas.LENGTH, '0'))
    }

    fun tagsEqual(
        a: ByteArray,
        b: ByteArray,
    ): Boolean = MessageDigest.isEqual(a, b)

    fun pairPrk(
        transcript: ByteArray,
        shared: ByteArray,
    ): ByteArray = extract(transcript, shared)

    /** A resume mixes the pairing key into a fresh exchange: forward secrecy from one, the pairing from the other. */
    fun resumePrk(
        transcript: ByteArray,
        shared: ByteArray,
        pairingKey: ByteArray,
    ): ByteArray {
        val ikm = shared + pairingKey
        try {
            return extract(transcript, ikm)
        } finally {
            ikm.fill(0)
        }
    }

    /** The laptop's key confirmation. In a pairing the laptop sends it only after its owner typed the phone's code. */
    fun laptopTag(
        prk: ByteArray,
        transcript: ByteArray,
        pairing: Boolean,
    ): ByteArray = confirmTag(prk, CONFIRM_LAPTOP, transcript, pairing)

    fun phoneTag(
        prk: ByteArray,
        transcript: ByteArray,
        pairing: Boolean,
    ): ByteArray = confirmTag(prk, CONFIRM_PHONE, transcript, pairing)

    /** The key both ends keep after a pairing, under the other's id, to resume with. */
    fun pairingKey(prk: ByteArray): ByteArray = expand(prk, PAIRING_KEY, KEY_LENGTH)

    /** The record keys for each direction, and the 8 bytes whose halves prefix each direction's nonces. */
    fun sessionKeys(prk: ByteArray): Triple<ByteArray, ByteArray, ByteArray> =
        Triple(expand(prk, KEY_P2L, KEY_LENGTH), expand(prk, KEY_L2P, KEY_LENGTH), expand(prk, IV, 8))

    /** A record nonce: the direction's 4-byte prefix, then the record counter, big-endian. */
    fun nonce(
        prefix: ByteArray,
        counter: Long,
    ): ByteArray =
        ByteBuffer
            .allocate(NONCE_LENGTH)
            .put(prefix)
            .putLong(counter)
            .array()

    private fun confirmTag(
        prk: ByteArray,
        label: ByteArray,
        transcript: ByteArray,
        pairing: Boolean,
    ): ByteArray {
        val key = expand(prk, label, KEY_LENGTH)
        try {
            return if (pairing) hmac(key, PAIR_LABEL, transcript) else hmac(key, transcript)
        } finally {
            key.fill(0)
        }
    }

    /** [value] as exactly 32 big-endian bytes; BigInteger adds a sign byte or drops leading zeros. */
    private fun coordinate(value: BigInteger): ByteArray {
        val bytes = value.toByteArray()
        val out = ByteArray(COORDINATE_LENGTH)
        val n = minOf(bytes.size, COORDINATE_LENGTH)
        bytes.copyInto(out, COORDINATE_LENGTH - n, bytes.size - n, bytes.size)
        return out
    }
}

/** One side's key pair for one connection, and its public half as the 65-byte point the wire carries. */
class Ephemeral internal constructor(
    internal val secret: ECPrivateKey,
    val publicKey: ByteArray,
)
