package me.akshitbansal.edgepad.link

/**
 * The app in front on the laptop, from TEXT FOREGROUND_APP. The name is personal data: show it, never log it.
 * Mirrors protocol/actions.txt.
 */
data class ForegroundApp(val token: String, val name: String) {
    companion object {
        private val TOKEN = Regex("[A-Za-z0-9._-]{1,64}")
        private const val NAME_MAX = 40

        /** `token/name`, split on the first slash. Null on empty text, a bad token or no slash. */
        fun parse(text: String): ForegroundApp? {
            val slash = text.indexOf('/')
            if (slash < 0) return null
            val token = text.substring(0, slash)
            if (!TOKEN.matches(token)) return null
            val clean = text.substring(slash + 1)
                .map { if (it.isISOControl() || it == '|') ' ' else it }
                .joinToString("")
                .trim()
            val points = clean.codePointCount(0, clean.length)
            val end = if (points > NAME_MAX) clean.offsetByCodePoints(0, NAME_MAX) else clean.length
            return ForegroundApp(token, clean.substring(0, end))
        }
    }
}
