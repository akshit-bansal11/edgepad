package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.protocol.AppAddStatus

/**
 * The apps one laptop offered to add as macros: the list it last sent, the icons that followed it, and how the last add
 * ended. The laptop owns all of it. A row is a token the laptop made and a name to show, and the phone sends the token
 * back and nothing else, so there is no path from anything stored here to a program run on the laptop.
 *
 * The list is an epoch: each APPS_LIST the laptop answers starts a new one, and only the newest list's tokens mean
 * anything to it. An icon is kept only for a token of the list held now, so a straggler from an older list is ignored.
 *
 * Everything the laptop sends is untrusted, so each cap here is the shared core's own (AppPicker) and a row or icon
 * that breaks one is dropped rather than closing anything. Touched on the UI thread only, like [LaptopState]; the blob
 * itself is put together on the link's thread and handed over already complete.
 */
class AppCatalogState {
    /** One row of the list. [token] is the laptop's `epoch.index`, which this side never builds. */
    class App(
        val token: String,
        val name: String,
    )

    /** The rows in the order the laptop sent them, which is sorted by name. */
    var apps: List<App> = emptyList()
        private set

    /** The epoch the held list belongs to, or null while there is no list or it has no valid row. */
    var epoch: Int? = null
        private set

    /**
     * True once a list has arrived, even an empty one. An empty list is an answer (nothing to offer, or a scan that
     * failed), and the screen must tell that from a list still on its way.
     */
    var loaded = false
        private set

    /** How the last APP_ADD ended, or null before the laptop has answered one. */
    var lastStatus: AppAddStatus? = null
        private set

    private val icons = HashMap<String, ByteArray>()

    /** The PNG for the row [token] names, or null until it has arrived. Bytes, because decoding is Android's. */
    fun icon(token: String): ByteArray? = icons[token]

    /** Forgets the list and its icons: a new APPS_LIST, a lost link, or a screen about to ask afresh. */
    fun clear() {
        apps = emptyList()
        epoch = null
        loaded = false
        icons.clear()
    }

    /**
     * Takes in an APP_LIST blob, replacing the list and every icon that went with the old one. False for one over
     * [MAX_LIST_BYTES], which is kept out whole: a list cut mid-row would be a different list.
     */
    fun setList(blob: ByteArray): Boolean {
        if (blob.size > MAX_LIST_BYTES) return false
        val rows = parse(blob.decodeToString())
        apps = rows
        epoch = rows.firstOrNull()?.let { parseToken(it.token)?.first }
        loaded = true
        icons.clear()
        return true
    }

    /** Takes in an APP_ICON blob for [token]. False, and nothing kept, for a token not in the list or a picture over the cap. */
    fun setIcon(
        token: String,
        png: ByteArray,
    ): Boolean {
        if (png.isEmpty() || png.size > MAX_ICON_BYTES || apps.none { it.token == token }) return false
        icons[token] = png
        return true
    }

    /** TEXT APP_ADD_STATUS. A token this build does not know reads as [AppAddStatus.FAILED]. */
    fun addStatus(token: String) {
        lastStatus = AppAddStatus.of(token)
    }

    companion object {
        /** The laptop sends at most this many apps. */
        const val MAX_APPS = 400

        /** The laptop cuts a name to this many characters. */
        const val MAX_NAME_CHARS = 40

        /** The laptop sends no icon bigger than this. */
        const val MAX_ICON_BYTES = 8 * 1024

        /** More than 400 names of 40 characters and their tokens can come to, with room over. */
        const val MAX_LIST_BYTES = 128 * 1024

        private const val TOKEN_PARTS = 2

        /**
         * A token as the laptop writes it: digits, one dot, digits. Null for anything else, so a row whose token
         * could never be answered with is not shown.
         */
        fun parseToken(token: String): Pair<Int, Int>? {
            val parts = token.split('.')
            if (parts.size != TOKEN_PARTS || parts.any { it.isEmpty() || !it.all { c -> c in '0'..'9' } }) return null
            val epoch = parts[0].toIntOrNull() ?: return null
            val index = parts[1].toIntOrNull() ?: return null
            return epoch to index
        }

        /**
         * The rows of a list blob: one `token TAB name` line each. A line with no tab, an unusable token or a blank
         * name is skipped. Rows of another epoch than the first are skipped, since one list is one epoch. A token that
         * comes twice keeps its place and takes the last name; rows past [MAX_APPS] are dropped.
         */
        fun parse(text: String): List<App> {
            val rows = LinkedHashMap<String, App>()
            var epoch: Int? = null
            for (line in text.split('\n')) {
                val tab = line.indexOf('\t')
                if (tab < 0) continue
                val token = line.substring(0, tab)
                val named = parseToken(token)?.first ?: continue
                if (epoch != null && named != epoch) continue
                val name = clean(line.substring(tab + 1))
                if (name.isEmpty() || (token !in rows && rows.size >= MAX_APPS)) continue
                epoch = named
                rows[token] = App(token, name)
            }
            return rows.values.toList()
        }

        /**
         * The rows whose name contains [query]: empty after trimming means all of them. Case is ignored and accents are
         * not folded, so "cafe" does not find "Café": folding needs locale rules this screen has no use for, and a
         * name the owner can see is one they can type.
         */
        fun filter(
            apps: List<App>,
            query: String,
        ): List<App> {
            val wanted = query.trim()
            return if (wanted.isEmpty()) apps else apps.filter { it.name.contains(wanted, ignoreCase = true) }
        }

        // Control characters out, then cut on a whole character: half a surrogate pair is no name.
        private fun clean(raw: String): String {
            val name = raw.filterNot { it.isISOControl() }.trim()
            if (name.length <= MAX_NAME_CHARS) return name
            val cut = if (name[MAX_NAME_CHARS - 1].isHighSurrogate()) MAX_NAME_CHARS - 1 else MAX_NAME_CHARS
            return name.substring(0, cut).trimEnd()
        }
    }
}
