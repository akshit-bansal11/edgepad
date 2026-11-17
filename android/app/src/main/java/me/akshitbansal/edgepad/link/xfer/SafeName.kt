package me.akshitbansal.edgepad.link.xfer

/** Turns a name the laptop sent into a file name this phone will create, or refuses it (protocol/filenames.txt). */
object SafeName {
    private const val INVALID = "<>:\"/\\|?*"

    private val reserved =
        setOf("CON", "PRN", "AUX", "NUL") +
            (1..9).map { "COM$it" } +
            (1..9).map { "LPT$it" }

    /**
     * The last path segment, control and bidi/format characters dropped, the characters Windows forbids in a name replaced by an
     * underscore, trailing dots and spaces trimmed, cut to [XferLimits.MAX_NAME_BYTES] UTF-8 bytes keeping the
     * extension. Null when nothing usable is left, or when the name is a Windows device name with or without an
     * extension.
     */
    fun clean(raw: String?): String? {
        if (raw.isNullOrEmpty()) return null
        val segment = raw.substring(raw.lastIndexOfAny(charArrayOf('/', '\\')) + 1)
        val text = StringBuilder()
        segment.codePoints().forEach {
            if (Character.isISOControl(it) || isBidiOrFormat(it)) return@forEach
            text.appendCodePoint(if (it < 0x80 && it.toChar() in INVALID) '_'.code else it)
        }
        val name = cutToBytes(text.toString().trimEnd('.', ' ')).trimEnd('.', ' ')
        if (name.isEmpty()) return null
        val stem = name.substringBefore('.').trimEnd(' ')
        return if (stem.uppercase() in reserved) null else name
    }

    /** [name] if nothing by that name exists, else `name (1).ext`, `name (2).ext` and so on: never an overwrite. */
    fun unique(
        name: String,
        exists: (String) -> Boolean,
    ): String {
        if (!exists(name)) return name
        val dot = name.lastIndexOf('.')
        val stem = if (dot > 0) name.substring(0, dot) else name
        val extension = if (dot > 0) name.substring(dot) else ""
        var n = 1
        while (exists("$stem ($n)$extension")) n++
        return "$stem ($n)$extension"
    }

    // Invisible format characters (Unicode Cf) that reorder or hide text: invoice<U+202E>fdp.exe would show as
    // invoiceexe.pdf. The whole category, U+E0001 too, as C# and Swift drop it.
    private fun isBidiOrFormat(point: Int): Boolean = Character.getType(point) == Character.FORMAT.toInt()

    private fun utf8(text: String): Int = text.toByteArray(Charsets.UTF_8).size

    private fun cutToBytes(name: String): String {
        if (utf8(name) <= XferLimits.MAX_NAME_BYTES) return name
        val dot = name.lastIndexOf('.')
        var extension = if (dot > 0) name.substring(dot) else ""
        if (utf8(extension) > XferLimits.MAX_EXTENSION_BYTES) extension = ""
        var budget = XferLimits.MAX_NAME_BYTES - utf8(extension)
        val stem = name.substring(0, name.length - extension.length)
        val kept = StringBuilder()
        var at = 0
        while (at < stem.length) {
            val point = stem.codePointAt(at)
            val text = String(Character.toChars(point))
            budget -= utf8(text)
            if (budget < 0) break
            kept.append(text)
            at += text.length
        }
        return kept.toString().trimEnd('.', ' ') + extension
    }
}
