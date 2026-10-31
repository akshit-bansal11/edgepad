package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.CaptureStatus
import me.akshitbansal.edgepad.protocol.FileStatus
import me.akshitbansal.edgepad.protocol.FileType
import me.akshitbansal.edgepad.protocol.Frame
import java.io.IOException
import java.security.MessageDigest

/**
 * Takes in the laptop's file frames and decides, file by file, whether this phone keeps one. This is a trust
 * boundary: the phone writes a file because the laptop said so, so every field is checked before anything is
 * written. The type must be in [FileType], the size inside its limit, the name a plain file name; the pieces must
 * arrive in order and add up to the size, and the SHA-256 must match. Anything else throws the file away and says
 * why, and the laptop keeps its copy.
 *
 * Nor is a file taken unasked. A FILE_BEGIN is accepted only while [toPhone] is on and against a permit: one capture
 * action this phone itself sent ([permit]) allows one file, within [SCREENSHOT_WINDOW_MS] for a screenshot or
 * [RECORDING_WINDOW_MS] for a recording, which is sent only once it stops. A connection takes at most [MAX_FILES]
 * files and [MAX_BYTES] bytes. Turning [toPhone] off, or [abort], throws away a file half here.
 *
 * Plain Kotlin: the bytes go to a [Store], which on a phone is the gallery, so a test can drive every path. Called on
 * the link's reading thread, and [abort] from whichever thread closes the link, hence the lock.
 */
class FileInbox(
    private val store: Store,
    private val report: (Progress) -> Unit,
    private val clock: () -> Long = System::currentTimeMillis,
) {
    /** Where a file goes while it arrives. */
    fun interface Store {
        /** A new, unpublished file called [name], or null when none could be made. */
        fun open(
            name: String,
            type: FileType,
        ): Pending?
    }

    /** One file on its way in, seen by nothing else until [publish]. */
    interface Pending {
        @Throws(IOException::class)
        fun write(bytes: ByteArray)

        /** Makes the file visible, and returns the folder it is in, as the user would name it. */
        @Throws(IOException::class)
        fun publish(): String

        /** Throws the file away. Never throws. */
        fun discard()
    }

    /** What the capture screen shows. */
    sealed interface Progress {
        data class Receiving(
            val name: String,
            val percent: Int,
        ) : Progress

        data class Saved(
            val folder: String,
        ) : Progress

        /** This phone refused or lost the file; the laptop keeps it. */
        data class Failed(
            val status: FileStatus,
        ) : Progress

        /** The laptop kept the capture and said why. */
        data class Kept(
            val status: CaptureStatus,
        ) : Progress
    }

    private class Transfer(
        val name: String,
        val size: Long,
        val pending: Pending,
    ) {
        val digest: MessageDigest = MessageDigest.getInstance("SHA-256")
        var received = 0L
        var seq = 0
        var percent = -1
    }

    private var current: Transfer? = null

    /** When each unused permit runs out, oldest first. */
    private val permits = ArrayDeque<Long>()
    private var files = 0
    private var bytes = 0L

    /** Whether this phone wants captures. Off revokes every permit and throws away a file on its way in. */
    @get:Synchronized
    @set:Synchronized
    var toPhone = false
        set(value) {
            field = value
            if (!value) {
                permits.clear()
                abort()
            }
        }

    /** This phone sent [action]. A capture action, while [toPhone] is on, allows the laptop one file for it. */
    @Synchronized
    fun permit(action: ActionId) {
        val window =
            when (action) {
                ActionId.CAPTURE_REGION, ActionId.CAPTURE_SCREEN -> SCREENSHOT_WINDOW_MS
                ActionId.CAPTURE_RECORD -> RECORDING_WINDOW_MS
                else -> return
            }
        if (toPhone) permits.addLast(clock() + window)
    }

    /** The laptop said a capture is staying with it, which settles that capture's permit. */
    @Synchronized
    fun settle() {
        permits.removeFirstOrNull()
    }

    /** A link closed: its permits and its counts go with it, and so does a file half here. */
    @Synchronized
    fun reset() {
        permits.clear()
        files = 0
        bytes = 0
        abort()
    }

    /** One file frame from the laptop. Returns the FILE_RESULT to send back, or null when there is nothing to say. */
    @Synchronized
    fun take(frame: Frame): Frame? =
        when (frame) {
            is Frame.FileBegin -> begin(frame)
            is Frame.FileChunk -> chunk(frame)
            is Frame.FileEnd -> end(frame)
            else -> null
        }

    /** The link closed: a file half here is thrown away. The laptop, which never heard SAVED, keeps it. */
    @Synchronized
    fun abort() {
        current?.pending?.discard()
        current = null
    }

    private fun begin(frame: Frame.FileBegin): Frame? {
        // A new file while one is still arriving means the laptop gave up on the old one.
        abort()
        // Unasked, or past what one connection may send: refused before a byte is looked at.
        val now = clock()
        while (permits.isNotEmpty() && permits.first() < now) permits.removeFirst()
        if (permits.removeFirstOrNull() == null) return refuse(FileStatus.REJECTED)
        if (files >= MAX_FILES || bytes + frame.size > MAX_BYTES) return refuse(FileStatus.REJECTED)
        val type = FileType.of(frame.type) ?: return refuse(FileStatus.REJECTED)
        if (frame.size !in 1..type.limit) return refuse(FileStatus.REJECTED)
        val name = safeName(frame.name, type) ?: return refuse(FileStatus.REJECTED)
        val pending = store.open(name, type) ?: return refuse(FileStatus.WRITE_FAILED)
        files++
        bytes += frame.size
        current = Transfer(name, frame.size, pending).also { progress(it) }
        return null
    }

    /** A piece of a file that was refused, or never begun, is ignored: the answer has already gone. */
    private fun chunk(frame: Frame.FileChunk): Frame? {
        val transfer = current ?: return null
        if (frame.seq != transfer.seq || transfer.received + frame.data.size > transfer.size) {
            return refuse(FileStatus.REJECTED)
        }
        try {
            transfer.pending.write(frame.data)
        } catch (e: IOException) {
            return refuse(FileStatus.WRITE_FAILED)
        }
        transfer.digest.update(frame.data)
        transfer.received += frame.data.size
        transfer.seq = (transfer.seq + 1) and SEQ_MASK
        progress(transfer)
        return null
    }

    private fun end(frame: Frame.FileEnd): Frame? {
        val transfer = current ?: return null
        if (transfer.received != transfer.size) return refuse(FileStatus.REJECTED)
        if (!MessageDigest.isEqual(transfer.digest.digest(), frame.sha256)) return refuse(FileStatus.BAD_HASH)
        val folder =
            try {
                transfer.pending.publish()
            } catch (e: IOException) {
                return refuse(FileStatus.WRITE_FAILED)
            }
        current = null
        report(Progress.Saved(folder))
        return FileStatus.SAVED.frame()
    }

    private fun refuse(status: FileStatus): Frame {
        abort()
        report(Progress.Failed(status))
        return status.frame()
    }

    /** Once per whole percent, not once per piece: a 200 MB file is 800,000 of them. */
    private fun progress(transfer: Transfer) {
        val percent = (transfer.received * PERCENT / transfer.size).toInt()
        if (percent == transfer.percent) return
        transfer.percent = percent
        report(Progress.Receiving(transfer.name, percent))
    }

    companion object {
        /** A region is a drag on the laptop, so this is the laptop's two-minute watch and the sending after it. */
        const val SCREENSHOT_WINDOW_MS = 5L * 60 * 1000

        /** A recording is sent when it stops, and the laptop watches for one for an hour. */
        const val RECORDING_WINDOW_MS = 65L * 60 * 1000

        const val MAX_FILES = 20
        const val MAX_BYTES = 1024L * 1024 * 1024

        private const val SEQ_MASK = 0xFF
        private const val PERCENT = 100L
    }
}

/** Characters no file name on a phone or a laptop should carry, replaced rather than refused. */
private const val RESERVED = ":*?\"<>|"

/** The longest name this phone writes, extension included. */
internal const val MAX_NAME_LENGTH = 100

/**
 * The name this phone saves [name] under, or null when it is refused. A name that could reach outside the folder (a
 * slash either way, a NUL, a leading dot, which also covers "." and "..") is refused outright, never repaired: a
 * laptop sending one is not sending a capture. Anything else unprintable or reserved becomes `_`, the name is cut to
 * [MAX_NAME_LENGTH], and the extension is always [type]'s own, so a PNG cannot arrive as `run.apk`.
 */
internal fun safeName(
    name: String,
    type: FileType,
): String? {
    if (name.isEmpty() || name.startsWith('.') || name.any { it == '/' || it == '\\' || it == '\u0000' }) return null
    val cleaned = buildString { name.forEach { append(if (it.isISOControl() || it in RESERVED) '_' else it) } }
    var stem = cleaned.substringBeforeLast('.').take(MAX_NAME_LENGTH - 1 - type.extension.length)
    // A cut between the halves of a surrogate pair would leave half a character.
    if (stem.lastOrNull()?.isHighSurrogate() == true) stem = stem.dropLast(1)
    stem = stem.trim().trimEnd('.')
    return if (stem.isEmpty()) null else "$stem.${type.extension}"
}
