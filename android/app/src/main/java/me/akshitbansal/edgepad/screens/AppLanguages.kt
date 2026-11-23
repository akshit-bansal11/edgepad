package me.akshitbansal.edgepad.screens

/**
 * The languages Edgepad ships, each named in itself so a person who cannot read the current one can still find theirs.
 * The tags are the res/values-* folders: the unqualified values folder is English, and a test holds the two lists
 * together, since the build derives the system's own per-app language list (generateLocaleConfig) from those folders.
 */
object AppLanguages {
    class Entry(
        val tag: String,
        val name: String,
    )

    val all =
        listOf(
            Entry("en", "English"),
            Entry("ar", "العربية"),
            Entry("de", "Deutsch"),
            Entry("es", "Español"),
            Entry("fr", "Français"),
            Entry("hi", "हिन्दी"),
            Entry("it", "Italiano"),
            Entry("ja", "日本語"),
            Entry("ko", "한국어"),
            Entry("nl", "Nederlands"),
            Entry("pl", "Polski"),
            Entry("pt-BR", "Português (Brasil)"),
            Entry("ru", "Русский"),
            Entry("tr", "Türkçe"),
            Entry("zh-CN", "中文（简体）"),
        )

    /** The entry for a tag the system reports, such as "de-DE" or "pt-PT": the exact tag, else the same language. */
    fun match(tag: String): Entry? =
        all.firstOrNull { it.tag.equals(tag, ignoreCase = true) }
            ?: all.firstOrNull { it.tag.substringBefore('-').equals(tag.substringBefore('-'), ignoreCase = true) }
}
