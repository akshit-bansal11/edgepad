package me.akshitbansal.edgepad.link.net

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertNull
import org.junit.Test
import java.util.Base64
import javax.crypto.AEADBadTagException

/**
 * The rules [LinkKeys] keeps around its SharedPreferences: each key sealed under its own laptop's id, and a value
 * that does not open read as no key at all. The real [KeystoreWrapper] needs the Android Keystore, which the JVM
 * these tests run on does not have, so a fake that checks the binding the same way stands in for it.
 */
class LinkKeysTest {
    private val wrapper = FakeWrapper()
    private val laptopA = ByteArray(LinkWire.ID_LENGTH) { 0x10 }
    private val laptopB = ByteArray(LinkWire.ID_LENGTH) { 0x20 }
    private val key = ByteArray(LinkCrypto.KEY_LENGTH) { it.toByte() }

    @Test
    fun aSealedKeyOpensUnderItsOwnLaptop() {
        val stored = LinkKeys.seal(wrapper, laptopA, key)
        assertArrayEquals(key, LinkKeys.unseal(wrapper, laptopA, stored))
    }

    @Test
    fun aKeyCopiedToAnotherLaptopsEntryDoesNotOpen() {
        val stored = LinkKeys.seal(wrapper, laptopA, key)
        assertNull(LinkKeys.unseal(wrapper, laptopB, stored))
    }

    @Test
    fun nothingStoredOrSomethingUnreadableIsNoKey() {
        assertNull(LinkKeys.unseal(wrapper, laptopA, null))
        assertNull(LinkKeys.unseal(wrapper, laptopA, "not base64 !"))
        assertNull(LinkKeys.unseal(wrapper, laptopA, Base64.getEncoder().encodeToString(byteArrayOf(1, 2))))
    }

    @Test
    fun eachLaptopHasItsOwnEntry() {
        assertNotEquals(LinkKeys.entry(laptopA), LinkKeys.entry(laptopB))
        assertEquals(LinkKeys.entry(laptopA), LinkKeys.entry(laptopA.copyOf()))
    }

    @Test
    fun aSavedPhoneIdIsReadBackOnlyWhenItIsSixteenBytes() {
        val id = ByteArray(LinkWire.ID_LENGTH) { (it + 1).toByte() }
        assertArrayEquals(id, LinkKeys.idOf(Base64.getEncoder().encodeToString(id)))
        assertNull(LinkKeys.idOf(Base64.getEncoder().encodeToString(id.copyOf(15))))
        assertNull(LinkKeys.idOf("not base64 !"))
        assertNull(LinkKeys.idOf(null))
    }

    /** Prefixes the binding and flips every bit: not secret, but it refuses a value bound to another id. */
    private class FakeWrapper : Wrapper {
        override fun wrap(
            plain: ByteArray,
            aad: ByteArray,
        ): ByteArray = aad + flip(plain)

        override fun unwrap(
            wrapped: ByteArray,
            aad: ByteArray,
        ): ByteArray {
            val bound = wrapped.size >= aad.size && wrapped.copyOf(aad.size).contentEquals(aad)
            if (!bound) throw AEADBadTagException("Not sealed under this id")
            return flip(wrapped.copyOfRange(aad.size, wrapped.size))
        }

        private fun flip(bytes: ByteArray): ByteArray = ByteArray(bytes.size) { bytes[it].toInt().inv().toByte() }
    }
}
