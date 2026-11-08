package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.link.blob.ClipboardSync

/** The most of a shared text the confirm dialog shows; its message scrolls, so every line up to here is seen. */
internal const val PREVIEW_MAX = 1000

/** What the confirm dialog shows of a shared text, and how many characters past the cut it does not show. */
internal data class Preview(
    val shown: String,
    val notShown: Int,
)

// Characters that draw nothing or reorder what follows them (zero-width space, direction marks, embeddings,
// overrides and isolates, word joiner, BOM). In the preview they show as U+FFFD, so nothing hides or disguises text.
private const val INVISIBLE =
    "\u061C\u200B\u200E\u200F\u202A\u202B\u202C\u202D\u202E\u2060\u2066\u2067\u2068\u2069\uFEFF"

// Any whitespace run holding two or more line breaks, shown as one blank line, so padding cannot push text out of view.
private val BLANK_RUN = Regex("\\n\\s*\\n")

/**
 * The text as the confirm dialog shows it: every line, not the first only (a first line alone could ask about
 * "git status" and send what follows it), with invisible characters made visible and blank runs collapsed, cut at
 * [PREVIEW_MAX] characters. [Preview.notShown] says how much more is sent than is shown. Only the preview changes:
 * what is sent is the text as shared.
 */
internal fun preview(text: CharSequence): Preview {
    val visible =
        text
            .trim()
            .map { if (it in INVISIBLE) '\uFFFD' else it }
            .joinToString("")
            .replace(BLANK_RUN, "\n\n")
    if (visible.length <= PREVIEW_MAX) return Preview(visible, 0)
    return Preview(visible.take(PREVIEW_MAX).trimEnd() + "…", visible.length - PREVIEW_MAX)
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
