package me.akshitbansal.edgepad.link

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
 * Plain Kotlin: the bytes go to a [Store], which on a phone is the gallery, so a test can drive every path. Called on
 * the link's reading thread, and [abort] from whichever thread closes the link, hence the lock.
 */
class FileInbox(
    private val store: Store,
    private val report: (Progress) -> Unit,
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
        val type = FileType.of(frame.type) ?: return refuse(FileStatus.REJECTED)
        if (frame.size !in 1..type.limit) return refuse(FileStatus.REJECTED)
        val name = safeName(frame.name, type) ?: return refuse(FileStatus.REJECTED)
        val pending = store.open(name, type) ?: return refuse(FileStatus.WRITE_FAILED)
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

    private companion object {
        const val SEQ_MASK = 0xFF
        const val PERCENT = 100L
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
