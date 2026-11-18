package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.link.blob.ClipboardContent
import me.akshitbansal.edgepad.link.blob.ClipboardSurface
import me.akshitbansal.edgepad.protocol.BlobKind

/** Where a copy happened. */
enum class ClipSide { LAPTOP, PHONE }

/** One copy: its [text], the [side] it came from, and when ([at], epoch milliseconds). */
data class ClipItem(
    val text: String,
    val side: ClipSide,
    val at: Long,
)

/**
 * The last text copies from the laptop and from this phone, newest first. Memory only: never written to disk, never
 * logged, and emptied by [clear] when no link remains. Every call is synchronized: the clipboard sync records from
 * its worker thread while the sheet reads on the main thread.
 */
class ClipboardHistory(
    private val clock: () -> Long = System::currentTimeMillis,
) {
    private val list = ArrayList<ClipItem>()

    val items: List<ClipItem> @Synchronized get() = list.toList()

    /**
     * Blank text and text over [MAX_BYTES] of UTF-8 are ignored; a repeat moves to the front and takes the new [side]
     * and time.
     */
    @Synchronized
    fun record(
        text: String,
        side: ClipSide,
    ) {
        if (text.isBlank() || text.toByteArray(Charsets.UTF_8).size > MAX_BYTES) return
        list.removeAll { it.text == text }
        list.add(0, ClipItem(text, side, clock()))
        while (list.size > MAX_ITEMS) list.removeAt(list.lastIndex)
    }

    @Synchronized
    fun clear() = list.clear()

    companion object {
        const val MAX_ITEMS = 10
        const val MAX_BYTES = 8 * 1024
    }
}

/**
 * A clipboard that notes the text passing through it: the laptop's copies written here, and this phone's own copies
 * as they are read for sending (so only while sharing is active, which is when the sync reads). Images are never
 * recorded, nor is anything the surface marks sensitive.
 */
class RecordingSurface(
    private val inner: ClipboardSurface,
    private val history: ClipboardHistory,
) : ClipboardSurface {
    override fun read(): ClipboardContent? {
        val content = inner.read()
        if (content != null && content.kind == BlobKind.CLIP_TEXT && !content.sensitive) {
            history.record(String(content.data, Charsets.UTF_8), ClipSide.PHONE)
        }
        return content
    }

    override fun write(content: ClipboardContent) {
        inner.write(content)
        if (content.kind == BlobKind.CLIP_TEXT) history.record(String(content.data, Charsets.UTF_8), ClipSide.LAPTOP)
    }
}
