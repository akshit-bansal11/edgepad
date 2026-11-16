package me.akshitbansal.edgepad.link.xfer

import me.akshitbansal.edgepad.protocol.Frame
import java.io.File
import java.io.IOException
import java.io.RandomAccessFile
import java.security.MessageDigest
import kotlin.concurrent.thread

/**
 * Takes one file at a time from the laptop: XFER_BEGIN, XFER_CHUNKs and XFER_END in, XFER_STATUS out. Chunks must
 * arrive contiguously; one that does not is answered with the bytes held, and the sender restarts there. The file
 * grows as `name.size.part` in the store, hashed as it arrives, and is published (never over a file, never opened)
 * only when the laptop's SHA-256 matches. A session that ends keeps the part, and a BEGIN for the same name and size
 * resumes it, re-hashing what is held. Frames come in on the link's reading thread; the publish, the one long copy,
 * runs on [runner]'s own thread so the link keeps being read. [cancel] may come from any thread.
 *
 * @param reply Takes one status for the laptop.
 * @param arrived Takes the name a file was published under.
 * @param progress Takes the bytes held after each chunk, for the notification to gate.
 * @param runner Runs the publish; tests run it in place.
 */
class XferReceiver(
    private val store: XferStore,
    private val reply: (Frame.XferStatus) -> Unit,
    private val arrived: (String) -> Unit = {},
    private val progress: (name: String, held: Long, size: Long) -> Unit = { _, _, _ -> },
    private val runner: (() -> Unit) -> Unit = { work -> thread(name = "edgepad-xfer-save", block = work) },
    now: () -> Long = System::currentTimeMillis,
) {
    private val lock = Any()
    private var active: Active? = null

    init {
        store.purge(now() - XferLimits.PARTIAL_KEPT_MS)
    }

    /** Whether a file is arriving. */
    val busy: Boolean get() = synchronized(lock) { active != null }

    /** Takes any XFER frame. False when it is not for the file arriving, or is not this side's to take. */
    fun take(frame: Frame): Boolean =
        when (frame) {
            is Frame.XferBegin -> begin(frame)
            is Frame.XferChunk -> chunk(frame)
            is Frame.XferEnd -> end(frame)
            is Frame.XferStatus -> status(frame)
            else -> false
        }

    /** Stops the file arriving, deletes its part and tells the laptop. False when none is, or it is being saved. */
    fun cancel(): Boolean =
        synchronized(lock) {
            val a = active?.takeUnless { it.saving } ?: return false
            finish(deletePart = true)
            say(a.id, XferCode.CANCELLED, 0)
            true
        }

    /** The session is over: the part stays, for the next BEGIN of the same name and size. */
    fun close() {
        synchronized(lock) {
            if (active?.saving == false) finish(deletePart = false)
        }
    }

    private fun begin(frame: Frame.XferBegin): Boolean =
        synchronized(lock) {
            if (active != null) {
                say(frame.id, XferCode.BUSY, 0)
                return true
            }
            val name = SafeName.clean(frame.name)
            if (name == null || frame.size > XferLimits.MAX_BYTES.toULong()) {
                say(frame.id, XferCode.REJECTED, 0)
                return true
            }
            val size = frame.size.toLong()
            try {
                if (store.freeBytes() < size + XferLimits.FREE_SPACE_MARGIN) {
                    say(frame.id, XferCode.REJECTED, 0)
                } else {
                    open(frame.id, size, name)
                }
            } catch (e: IOException) {
                say(frame.id, XferCode.WRITE_FAILED, 0)
            } catch (e: SecurityException) {
                say(frame.id, XferCode.WRITE_FAILED, 0)
            }
            true
        }

    private fun open(
        id: Int,
        size: Long,
        name: String,
    ) {
        val part = store.part(name, size)
        val digest = MessageDigest.getInstance("SHA-256")
        val file = RandomAccessFile(part, "rw")
        try {
            if (file.length() > size) file.setLength(0)
            // What is held is hashed again from disk: the digest at the far end is of the whole file, not of this run.
            val buffer = ByteArray(XferLimits.BUFFER_BYTES)
            var read = file.read(buffer)
            while (read > 0) {
                digest.update(buffer, 0, read)
                read = file.read(buffer)
            }
            val held = file.length()
            active = Active(id, size, name, part, file, digest, held)
            say(id, XferCode.READY, held)
        } catch (e: IOException) {
            file.close()
            throw e
        }
    }

    private fun chunk(frame: Frame.XferChunk): Boolean =
        synchronized(lock) {
            val a = active?.takeIf { it.id == frame.id && !it.saving } ?: return false
            if (frame.offset != a.held.toULong()) {
                say(a.id, XferCode.PROGRESS, a.held)
                return true
            }
            if (a.held.toULong() + frame.data.size.toULong() > a.size.toULong()) {
                finish(deletePart = true)
                say(frame.id, XferCode.REJECTED, 0)
                return true
            }
            try {
                a.file.write(frame.data)
            } catch (e: IOException) {
                finish(deletePart = true)
                say(frame.id, XferCode.WRITE_FAILED, 0)
                return true
            }
            a.digest.update(frame.data)
            a.held += frame.data.size
            if (a.held - a.reported >= XferLimits.PROGRESS_EVERY_BYTES) {
                a.reported = a.held
                say(a.id, XferCode.PROGRESS, a.held)
            }
            progress(a.name, a.held, a.size)
            true
        }

    private fun end(frame: Frame.XferEnd): Boolean =
        synchronized(lock) {
            val a = active?.takeIf { it.id == frame.id && !it.saving } ?: return false
            // Short of the size is not a bad file, it is not all of it yet: say where it stands.
            if (a.held != a.size) {
                say(a.id, XferCode.PROGRESS, a.held)
                return true
            }
            if (!MessageDigest.isEqual(a.digest.digest(), frame.sha256)) {
                finish(deletePart = true)
                say(frame.id, XferCode.BAD_HASH, 0)
                return true
            }
            a.saving = true
            runner { save(a) }
            true
        }

    /** The laptop's CANCELLED. Any other status is the sender's to receive, not this side's. */
    private fun status(frame: Frame.XferStatus): Boolean =
        synchronized(lock) {
            val a = active?.takeIf { it.id == frame.id && !it.saving } ?: return false
            if (frame.status != XferCode.CANCELLED.value) return false
            finish(deletePart = true)
            true
        }

    private fun save(a: Active) {
        val published =
            try {
                a.file.close()
                store.publish(a.part, a.name)
            } catch (e: IOException) {
                null
            }
        synchronized(lock) {
            if (active === a) active = null
            if (published == null) say(a.id, XferCode.WRITE_FAILED, 0) else say(a.id, XferCode.SAVED, a.size)
        }
        if (published != null) arrived(published)
    }

    private fun finish(deletePart: Boolean) {
        val a = active ?: return
        active = null
        try {
            a.file.close()
        } catch (e: IOException) {
            // Closing a part that is being dropped has nothing left to lose.
        }
        if (deletePart) a.part.delete()
    }

    private fun say(
        id: Int,
        code: XferCode,
        offset: Long,
    ) = reply(Frame.XferStatus(id, code.value, offset.toULong()))

    private class Active(
        val id: Int,
        val size: Long,
        val name: String,
        val part: File,
        val file: RandomAccessFile,
        val digest: MessageDigest,
        var held: Long,
    ) {
        var reported = held
        var saving = false
    }
}
