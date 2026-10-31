package me.akshitbansal.edgepad.link.blob

import android.util.Log
import me.akshitbansal.edgepad.protocol.BlobKind
import me.akshitbansal.edgepad.protocol.Frame
import java.nio.ByteBuffer
import java.nio.charset.CharacterCodingException
import java.security.MessageDigest
import java.util.concurrent.atomic.AtomicInteger
import kotlin.concurrent.thread

/** What a clipboard holds. [sensitive] is the platform's verdict; a flag it could not read counts as set. */
class ClipboardContent(
    val kind: BlobKind,
    val data: ByteArray,
    val sensitive: Boolean = false,
)

/** This phone's clipboard. Either call may block or throw: [ClipboardSync] never makes them on the reading thread. */
interface ClipboardSurface {
    /** The clipboard now, or null for nothing that can be shared. */
    fun read(): ClipboardContent?

    fun write(content: ClipboardContent)
}

/**
 * The shared clipboard's rules, the same as on every laptop: nothing moves unless sharing was turned on for this link
 * ([setShared]) and the owner's own toggle is on, sensitive content never leaves, and a copy that came from the laptop
 * does not go back. One per link, and each link is one laptop: only the selected laptop's is shared, so a blob from any
 * other is refused here and never reaches the clipboard.
 *
 * A change here waits [DEBOUNCE_MS] for the app that made it to finish, then goes on a thread of its own, since a
 * transfer is paced. A blob from the laptop is checked on the reading thread and written from another, so a clipboard
 * that stalls does not stop the link.
 *
 * @param surface This phone's clipboard.
 * @param sender The link's sender.
 * @param allowed The owner's own toggle, read every time, since it can change while the link is up.
 * @param ended True once the link is gone.
 * @param clock Milliseconds, for tests.
 * @param later Runs work off the reading thread after a delay, for tests.
 * @param log Where a failure goes, for tests: [Log] does not run in a unit test.
 */
class ClipboardSync(
    private val surface: ClipboardSurface,
    private val sender: BlobSender,
    private val allowed: () -> Boolean,
    private val ended: () -> Boolean,
    private val clock: () -> Long = { System.nanoTime() / NANOS_PER_MS },
    private val later: (Int, () -> Unit) -> Unit = { ms, work -> onThread(ms, work) },
    private val log: (String) -> Unit = { Log.w(TAG, it) },
) {
    private val gate = Any()
    private val receiver = BlobReceiver({ cap(it) }, ::received)
    private val changes = AtomicInteger()

    @Volatile private var shared = false

    /** The SHA-256 of what both ends last agreed on, so neither sends it to the other again. */
    private var known: ByteArray? = null

    /** The latest blob waiting to be written. A newer one replaces it. */
    private var incoming: ClipboardContent? = null
    private var quietUntil = 0L

    private val active: Boolean get() = shared && !ended() && allowed()

    /**
     * SET CLIPBOARD, as this phone is about to send it. False for a value that is neither 0 nor 1, and for a 1 while
     * the owner's toggle is off. Turning it on sends nothing: only the next copy goes, so a laptop just selected learns
     * nothing old.
     */
    fun setShared(value: Int): Boolean {
        if (value !in 0..1 || (value == 1 && !allowed())) return false
        shared = value == 1
        if (!shared) {
            receiver.reset()
            synchronized(gate) {
                incoming = null
                known = null
            }
        }
        return true
    }

    /** The platform saw this clipboard change. Any thread; returns at once. */
    fun localChanged() {
        if (!active) return
        val change = changes.incrementAndGet()
        later(DEBOUNCE_MS) { guarded { flush(change) } }
    }

    /** A blob frame from this link's laptop. False when it was refused. */
    fun take(frame: Frame): Boolean {
        if (active) return receiver.take(frame)
        receiver.reset()
        return false
    }

    private fun received(
        kind: Int,
        data: ByteArray,
        hash: ByteArray,
    ): Boolean {
        val blob = BlobKind.of(kind)
        if (blob == null || !valid(blob, data)) return false
        synchronized(gate) {
            known = hash
            incoming = ClipboardContent(blob, data)
        }
        later(0) { guarded { writeIncoming() } }
        return true
    }

    private fun writeIncoming() {
        val content = synchronized(gate) { incoming.also { incoming = null } }
        if (content == null || !active) return
        surface.write(content)
        synchronized(gate) { quietUntil = clock() + WRITE_QUIET_MS }
    }

    private fun flush(change: Int) {
        if (change != changes.get() || !active) return
        if (synchronized(gate) { clock() < quietUntil }) return
        val content = surface.read()
        if (content == null || content.sensitive || content.data.isEmpty()) return
        val limit = cap(content.kind.id)
        if (limit == null || content.data.size > limit) {
            log("Clipboard: ${content.data.size} bytes of ${content.kind} is over the cap, so it stays here")
            return
        }
        val hash = sha256(content.data)
        synchronized(gate) {
            if (known?.let { MessageDigest.isEqual(hash, it) } == true) return
            known = hash
        }
        var sent = false
        try {
            sent = sender.send(content.kind, NO_LABEL, content.data) { !active }
        } finally {
            if (!sent) forget(hash)
        }
    }

    // The laptop never got this, so the same content copied again must go. A blob since then stays known.
    private fun forget(hash: ByteArray) {
        synchronized(gate) {
            if (known === hash) {
                known = null
            }
        }
    }

    // The last stop before a thread with nothing above it, so a clipboard that fails costs one copy and no more.
    private fun guarded(work: () -> Unit) {
        try {
            work()
        } catch (e: Exception) {
            log("Clipboard: ${e.javaClass.simpleName}: ${e.message}")
        }
    }

    companion object {
        const val MAX_TEXT_BYTES = 1024L * 1024
        const val MAX_IMAGE_BYTES = 4L * 1024 * 1024

        /** A small PNG can inflate to any size, and the far end decodes it. 8K is 33 million. */
        const val MAX_IMAGE_PIXELS = 50_000_000L

        const val DEBOUNCE_MS = 150

        /**
         * How long after a write the clipboard is not read for sending. The hash cannot catch every echo of our own
         * write: a platform that hands an image back re-encoded has different bytes.
         */
        const val WRITE_QUIET_MS = 500L

        private const val TAG = "Edgepad"
        private const val NANOS_PER_MS = 1_000_000L

        /** The label is for kinds that need a name; a copy has none. */
        private const val NO_LABEL = ""

        /** The PNG signature, then IHDR's length and name, then its width and height. */
        private const val PNG_HEADER_BYTES = 24
        private const val PNG_WIDTH_AT = 16
        private const val PNG_HEIGHT_AT = 20
        private const val U32_MASK = 0xFFFFFFFFL
        private val PNG_START =
            byteArrayOf(-119, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52)

        private fun cap(kind: Int): Long? =
            when (BlobKind.of(kind)) {
                BlobKind.CLIP_TEXT -> MAX_TEXT_BYTES
                BlobKind.CLIP_IMAGE -> MAX_IMAGE_BYTES
                else -> null
            }

        private fun valid(
            kind: BlobKind,
            data: ByteArray,
        ): Boolean =
            data.isNotEmpty() &&
                when (kind) {
                    BlobKind.CLIP_TEXT -> isUtf8(data)
                    BlobKind.CLIP_IMAGE -> isPng(data)
                    else -> false
                }

        private fun isUtf8(data: ByteArray): Boolean =
            try {
                data.decodeToString(throwOnInvalidSequence = true)
                true
            } catch (e: CharacterCodingException) {
                false
            }

        // A PNG whose header can be decoded. The rest of the file is the platform decoder's to refuse.
        private fun isPng(data: ByteArray): Boolean {
            if (data.size < PNG_HEADER_BYTES || !data.copyOf(PNG_START.size).contentEquals(PNG_START)) return false
            val header = ByteBuffer.wrap(data)
            val width = header.getInt(PNG_WIDTH_AT).toLong() and U32_MASK
            val height = header.getInt(PNG_HEIGHT_AT).toLong() and U32_MASK
            return height > 0 && width in 1..MAX_IMAGE_PIXELS / height
        }

        private fun onThread(
            ms: Int,
            work: () -> Unit,
        ) {
            thread(isDaemon = true, name = "edgepad-clipboard") {
                Thread.sleep(ms.toLong())
                work()
            }
        }
    }
}
