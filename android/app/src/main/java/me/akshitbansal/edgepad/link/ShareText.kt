package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.link.blob.ClipboardSync

/** The most of a shared text the confirm dialog shows; its message scrolls, so every line up to here is seen. */
internal const val PREVIEW_MAX = 1000

/**
 * What the confirm dialog shows of a shared text: [shown], how many [lines] are sent (counted on the text as shared,
 * a trailing line break included, which a terminal would run), and how many characters past the cut are [notShown].
 */
internal data class Preview(
    val shown: String,
    val lines: Int,
    val notShown: Int,
)

/** U+FFFD, which every shared character that would draw nothing, or change how others draw, is shown as. */
private const val MARK = 0xFFFD

// Letters and symbols that are drawn as blank space: Hangul fillers and the empty Braille cell.
private val BLANK_GLYPHS = setOf(0x115F, 0x1160, 0x2800, 0x3164, 0xFFA0)

/**
 * Whether a code point could hide or disguise text: every format character (zero-width, direction marks, overrides,
 * isolates, tags, BOM), control characters other than tab and line breaks, private-use, lone surrogates, unassigned
 * code points, and [BLANK_GLYPHS]. By category, not a list of known tricks, so a new one is caught too.
 */
private fun disguises(cp: Int): Boolean =
    when (Character.getType(cp)) {
        Character.FORMAT.toInt(),
        Character.PRIVATE_USE.toInt(),
        Character.SURROGATE.toInt(),
        Character.UNASSIGNED.toInt(),
        -> true

        Character.CONTROL.toInt() -> cp != '\n'.code && cp != '\r'.code && cp != '\t'.code

        else -> cp in BLANK_GLYPHS
    }

/** A line with nothing drawn on it: every code point is whitespace or a space character (NBSP, ideographic, ...). */
private fun blank(line: String): Boolean =
    line.codePoints().allMatch { Character.isWhitespace(it) || Character.isSpaceChar(it) }

/**
 * The text as the confirm dialog shows it: every line, not the first only (a first line alone could ask about
 * "git status" and send what follows it), each code point that could disguise text shown as U+FFFD, blank lines at
 * the ends dropped and every run of them shown as one, cut at [PREVIEW_MAX] characters. Only the preview changes:
 * what is sent is the text as shared.
 */
internal fun preview(text: CharSequence): Preview {
    val all = text.toString()
    val shownLines = mutableListOf<String>()
    for (line in all.split('\n')) {
        val visible = buildString { line.codePoints().forEach { appendCodePoint(if (disguises(it)) MARK else it) } }
        when {
            !blank(visible) -> shownLines += visible.trimEnd()
            shownLines.isNotEmpty() && shownLines.last().isNotEmpty() -> shownLines += ""
        }
    }
    val shown = shownLines.joinToString("\n").trimEnd()
    val lines = all.count { it == '\n' } + 1
    if (shown.length <= PREVIEW_MAX) return Preview(shown, lines, 0)
    return Preview(shown.take(PREVIEW_MAX).trimEnd() + "\u2026", lines, shown.length - PREVIEW_MAX)
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
