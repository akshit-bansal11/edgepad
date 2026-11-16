package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.link.blob.ClipboardContent
import me.akshitbansal.edgepad.link.blob.ClipboardSurface
import me.akshitbansal.edgepad.protocol.BlobKind

/**
 * The last text copies that came from a laptop, newest first. Memory only: never written to disk, never logged, and
 * emptied by [clear] when no link remains. Main thread only.
 */
class ClipboardHistory {
    private val list = ArrayList<String>()

    val items: List<String> get() = list.toList()

    /** Blank text and text over [MAX_BYTES] of UTF-8 are ignored; a repeat moves to the front. */
    fun record(text: String) {
        if (text.isBlank() || text.toByteArray(Charsets.UTF_8).size > MAX_BYTES) return
        list.remove(text)
        list.add(0, text)
        while (list.size > MAX_ITEMS) list.removeAt(list.lastIndex)
    }

    fun clear() = list.clear()

    companion object {
        const val MAX_ITEMS = 10
        const val MAX_BYTES = 8 * 1024
    }
}

/**
 * A clipboard that notes the text written to it. Only a laptop's copy is ever written (the phone's own are read), so
 * what lands in [history] is exactly the laptop's text copies.
 */
class RecordingSurface(
    private val inner: ClipboardSurface,
    private val history: ClipboardHistory,
) : ClipboardSurface {
    override fun read(): ClipboardContent? = inner.read()

    override fun write(content: ClipboardContent) {
        inner.write(content)
        if (content.kind == BlobKind.CLIP_TEXT) history.record(String(content.data, Charsets.UTF_8))
    }
}
