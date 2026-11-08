package me.akshitbansal.edgepad.screens

/** Cuts text into pieces a TEXT frame can carry: at most [MAX_BYTES] of UTF-8 each, never inside a character. */
object TextChunks {
    /** The protocol's limit on one TEXT frame. */
    const val MAX_BYTES = 255

    fun split(text: String): List<String> {
        val out = ArrayList<String>()
        val piece = StringBuilder()
        var bytes = 0
        var i = 0
        while (i < text.length) {
            val point = text.codePointAt(i)
            val size = String(Character.toChars(point)).toByteArray(Charsets.UTF_8).size
            if (bytes + size > MAX_BYTES) {
                out.add(piece.toString())
                piece.setLength(0)
                bytes = 0
            }
            piece.appendCodePoint(point)
            bytes += size
            i += Character.charCount(point)
        }
        if (piece.isNotEmpty()) out.add(piece.toString())
        return out
    }
}
