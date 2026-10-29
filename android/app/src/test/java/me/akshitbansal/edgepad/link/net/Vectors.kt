package me.akshitbansal.edgepad.link.net

import java.io.File
import java.math.BigInteger
import java.security.KeyFactory
import java.security.interfaces.ECPrivateKey
import java.security.spec.ECPrivateKeySpec

/**
 * protocol/link-vectors.txt, the EDGL1 values the laptop's own code worked out, read the way the frame fixtures
 * are read: found by walking up from the working directory.
 */
internal object Vectors {
    private val lines: List<Pair<String, Map<String, String>>> =
        fixture()
            .readLines()
            .filter { it.isNotBlank() && !it.startsWith("#") }
            .map { line ->
                val parts = line.split(" ")
                parts[0] to parts.drop(1).associate { it.substringBefore('=') to it.substringAfter('=') }
            }

    val names: Set<String> get() = lines.map { it.first }.toSet()

    /** The only line called [name]. */
    fun line(name: String): Map<String, String> = lines.single { it.first == name }.second

    /** Field [key] of the line [name], as bytes. */
    fun bytes(
        name: String,
        key: String,
    ): ByteArray = hex(line(name).getValue(key))

    /** The `record` bytes of every line called [name], in counter order. */
    fun records(name: String): List<ByteArray> =
        lines
            .filter { it.first == name }
            .sortedBy { it.second.getValue("counter").toInt() }
            .map { hex(it.second.getValue("record")) }

    /** The phone's key pair, RFC 5903's i. */
    fun phone(): Ephemeral = fixed(bytes("PAIR_INPUT", "dP"), bytes("PAIR_INPUT", "pkP"))

    /** The laptop's key pair, RFC 5903's r. */
    fun laptop(): Ephemeral = fixed(bytes("PAIR_INPUT", "dL"), bytes("PAIR_INPUT", "pkL"))

    /** A cleartext message as the wire carries it: u16 length, little-endian, then the body. */
    fun frame(body: ByteArray): ByteArray = byteArrayOf(body.size.toByte(), (body.size shr 8).toByte()) + body

    fun hex(text: String): ByteArray = text.chunked(2).map { it.toInt(16).toByte() }.toByteArray()

    // A key pair from known values, for vectors only. The platform cannot derive the public half from the private
    // one, so the file's public key is taken as given; the ECDH test checks both agree on the same secret.
    private fun fixed(
        d: ByteArray,
        publicKey: ByteArray,
    ): Ephemeral {
        val params = LinkCrypto.newKeyPair().secret.params
        val key = KeyFactory.getInstance("EC").generatePrivate(ECPrivateKeySpec(BigInteger(1, d), params))
        return Ephemeral(key as ECPrivateKey, publicKey)
    }

    private fun fixture(): File {
        var dir: File? = File("").absoluteFile
        while (dir != null) {
            val candidate = File(dir, "protocol/link-vectors.txt")
            if (candidate.isFile) return candidate
            dir = dir.parentFile
        }
        error("protocol/link-vectors.txt was not found above ${File("").absolutePath}")
    }
}
