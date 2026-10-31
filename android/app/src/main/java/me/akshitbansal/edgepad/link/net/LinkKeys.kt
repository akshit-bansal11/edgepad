package me.akshitbansal.edgepad.link.net

import android.content.Context
import android.content.SharedPreferences
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import java.io.IOException
import java.security.GeneralSecurityException
import java.security.KeyStore
import java.util.Base64
import javax.crypto.AEADBadTagException
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

/**
 * Seals a pairing key so the file it is saved in is useless anywhere else. [aad] binds a sealed value to the entry
 * it was saved under, so a value copied to another laptop's entry does not open. The real one is
 * [KeystoreWrapper]; tests use a fake, since the JVM they run on has no Android Keystore.
 */
interface Wrapper {
    fun wrap(
        plain: ByteArray,
        aad: ByteArray,
    ): ByteArray

    /** Throws [GeneralSecurityException] for a value this wrapper did not seal, or sealed under other [aad]. */
    fun unwrap(
        wrapped: ByteArray,
        aad: ByteArray,
    ): ByteArray
}

/**
 * AES-256-GCM under an Android Keystore key, `edgepad-link-wrap`, made on first use. Keystore keys never leave
 * the keystore, so a copy of the app's files carries no usable pairing key. Not testable on CI: no Keystore there.
 * The output is the 12-byte IV the Keystore chose, then the ciphertext and its tag.
 */
class KeystoreWrapper : Wrapper {
    override fun wrap(
        plain: ByteArray,
        aad: ByteArray,
    ): ByteArray {
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.ENCRYPT_MODE, key())
        cipher.updateAAD(aad)
        return cipher.iv + cipher.doFinal(plain)
    }

    override fun unwrap(
        wrapped: ByteArray,
        aad: ByteArray,
    ): ByteArray {
        if (wrapped.size < IV_LENGTH + TAG_BITS / 8) throw AEADBadTagException("A wrapped key too short to open")
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(TAG_BITS, wrapped, 0, IV_LENGTH))
        cipher.updateAAD(aad)
        return cipher.doFinal(wrapped, IV_LENGTH, wrapped.size - IV_LENGTH)
    }

    @Synchronized
    private fun key(): SecretKey {
        val store = KeyStore.getInstance(KEYSTORE)
        store.load(null)
        (store.getKey(ALIAS, null) as? SecretKey)?.let { return it }
        val purposes = KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT
        val spec =
            KeyGenParameterSpec
                .Builder(ALIAS, purposes)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(KEY_BITS)
                .build()
        val generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, KEYSTORE)
        generator.init(spec)
        return generator.generateKey()
    }

    private companion object {
        const val KEYSTORE = "AndroidKeyStore"
        const val ALIAS = "edgepad-link-wrap"
        const val TRANSFORMATION = "AES/GCM/NoPadding"
        const val IV_LENGTH = 12
        const val TAG_BITS = 128
        const val KEY_BITS = 256
    }
}

/** What a network transport needs of [LinkKeys]; a JVM test hands it a map instead of SharedPreferences. */
interface PairingKeys {
    val phoneId: ByteArray

    fun pairingKey(laptopId: ByteArray): ByteArray?

    fun save(
        laptopId: ByteArray,
        pairingKey: ByteArray,
    )

    fun forget(laptopId: ByteArray)
}

/**
 * The phone's side of the pairings it has made: its own id, 16 random bytes made once per install, and one
 * pairing key per laptop, each sealed by [wrapper] before it is written. The SharedPreferences plumbing is thin
 * and runs only on a phone; the rules are in the companion, where a JVM test reaches them.
 */
class LinkKeys(
    private val prefs: SharedPreferences,
    private val wrapper: Wrapper,
) : PairingKeys {
    /** This phone's link id, made and saved the first time it is asked for. */
    override val phoneId: ByteArray by lazy { idOf(prefs.getString(KEY_PHONE_ID, null)) ?: newPhoneId() }

    /** The key a pairing with [laptopId] left, or null: never paired, forgotten, or no longer openable. */
    override fun pairingKey(laptopId: ByteArray): ByteArray? =
        unseal(wrapper, laptopId, prefs.getString(entry(laptopId), null))

    override fun save(
        laptopId: ByteArray,
        pairingKey: ByteArray,
    ) = commit(prefs.edit().putString(entry(laptopId), seal(wrapper, laptopId, pairingKey)))

    override fun forget(laptopId: ByteArray) = commit(prefs.edit().remove(entry(laptopId)))

    private fun newPhoneId(): ByteArray {
        val id = LinkCrypto.randomBytes(LinkWire.ID_LENGTH)
        commit(prefs.edit().putString(KEY_PHONE_ID, encode(id)))
        return id
    }

    // Written at once rather than with apply(): these writes are rare, off the main thread, and a key lost to a
    // process death means pairing again.
    private fun commit(edit: SharedPreferences.Editor) {
        if (!edit.commit()) throw IOException("The pairing keys could not be saved")
    }

    companion object {
        private const val NAME = "link-keys"
        private const val KEY_PHONE_ID = "phoneId"

        /** The phone's store, its keys sealed by the Android Keystore. */
        fun open(context: Context): LinkKeys =
            LinkKeys(context.getSharedPreferences(NAME, Context.MODE_PRIVATE), KeystoreWrapper())

        /** The preference that holds [laptopId]'s key. */
        internal fun entry(laptopId: ByteArray): String = "pk." + Base64.getUrlEncoder().encodeToString(laptopId)

        internal fun seal(
            wrapper: Wrapper,
            laptopId: ByteArray,
            pairingKey: ByteArray,
        ): String = encode(wrapper.wrap(pairingKey, laptopId))

        /**
         * The key in [stored], or null when there is none or it does not open. That fails closed: a key that
         * cannot be read makes the phone pair again, and is never guessed at.
         */
        internal fun unseal(
            wrapper: Wrapper,
            laptopId: ByteArray,
            stored: String?,
        ): ByteArray? {
            if (stored == null) return null
            return try {
                wrapper.unwrap(Base64.getDecoder().decode(stored), laptopId)
            } catch (e: GeneralSecurityException) {
                null
            } catch (e: IllegalArgumentException) {
                null
            }
        }

        /** A saved phone id, or null when there is none or it is not 16 bytes of Base64. */
        internal fun idOf(stored: String?): ByteArray? {
            if (stored == null) return null
            return try {
                Base64.getDecoder().decode(stored).takeIf { it.size == LinkWire.ID_LENGTH }
            } catch (e: IllegalArgumentException) {
                null
            }
        }

        private fun encode(bytes: ByteArray): String = Base64.getEncoder().encodeToString(bytes)
    }
}
