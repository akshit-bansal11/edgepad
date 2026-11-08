package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.link.blob.ClipboardSync

/** The most of a shared text the confirm dialog shows; its message scrolls, so every line up to here is seen. */
internal const val PREVIEW_MAX = 1000

/**
 * The text as the confirm dialog shows it: all of it, every line, trimmed, and cut at [PREVIEW_MAX] characters with
 * an ellipsis. All lines, not the first: a first line alone could ask about "git status" and send what follows it.
 */
internal fun preview(text: CharSequence): String {
    val all = text.trim()
    return if (all.length <= PREVIEW_MAX) all.toString() else all.take(PREVIEW_MAX).trimEnd().toString() + "…"
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
