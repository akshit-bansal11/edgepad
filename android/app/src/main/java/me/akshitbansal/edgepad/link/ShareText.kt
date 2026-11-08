package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.link.blob.ClipboardSync

/** The longest preview line the confirm dialog shows of a shared text. */
internal const val PREVIEW_MAX = 80

/** The first line with something on it, trimmed and cut at [PREVIEW_MAX] characters, or "" for a text with none. */
internal fun previewLine(text: CharSequence): String {
    val line = text.lineSequence().map { it.trim() }.firstOrNull { it.isNotEmpty() } ?: return ""
    return if (line.length <= PREVIEW_MAX) line else line.take(PREVIEW_MAX).trimEnd() + "…"
}

/** What a shared text is as a clipboard blob: null when it is blank, [TooLong] when over the clipboard's cap. */
internal sealed interface SharedText {
    class Ok(
        val bytes: ByteArray,
    ) : SharedText

    data object Blank : SharedText

    data object TooLong : SharedText
}

internal fun sharedText(text: CharSequence?): SharedText {
    if (text == null || text.isBlank()) return SharedText.Blank
    val bytes = text.toString().toByteArray()
    return if (bytes.size > ClipboardSync.MAX_TEXT_BYTES) SharedText.TooLong else SharedText.Ok(bytes)
}
