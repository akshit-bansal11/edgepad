package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.protocol.AgentToken
import me.akshitbansal.edgepad.protocol.CiToken
import me.akshitbansal.edgepad.protocol.SessionStateToken
import me.akshitbansal.edgepad.protocol.LimitWindow as WireWindow

/** TEXT STATS: -1 is unknown. Percents are 0..100, rates KiB/s, ping ms, temperatures C, power W. */
data class StatsSample(
    val cpu: Int,
    val ram: Int,
    val gpu: Int,
    val vram: Int,
    val down: Int,
    val up: Int,
    val ping: Int,
    val loss: Int,
    val cpuTemp: Int,
    val gpuTemp: Int,
    val gpuPower: Int,
)

/** TEXT STATS_INFO. A MiB of -1 is unknown; an empty name is unknown. */
data class StatsInfo(
    val cpuName: String,
    val ramMiB: Int,
    val gpuName: String,
    val vramMiB: Int,
    val netName: String,
)

/** One TEXT DISKS entry. -1 is unknown. */
data class DiskEntry(
    val label: String,
    val usedPct: Int,
    val totalGiB: Int,
    val freeGiB: Int,
)

/** One TEXT SESSIONS entry. [contextPct] is how full the session's context window is, 0 to 100; -1 when unknown. */
data class SessionEntry(
    val agent: AgentToken,
    val state: SessionStateToken,
    val title: String,
    val contextPct: Int = -1,
)

/** The window of a TEXT LIMITS entry: a length in minutes, the session's context, or a named limit with no length. */
sealed interface LimitWindow {
    /** The wire form, which [DashWire.limitWindow] parses back. */
    val text: String

    data class Minutes(
        val minutes: Int,
    ) : LimitWindow {
        override val text: String get() = minutes.toString()
    }

    data object Context : LimitWindow {
        override val text: String get() = WireWindow.CONTEXT
    }

    data class Named(
        val name: String,
    ) : LimitWindow {
        override val text: String get() = WireWindow.NAMED_PREFIX + name
    }
}

/** One TEXT LIMITS entry. Times are UTC minutes since the Unix epoch, -1 unknown. */
data class LimitEntry(
    val agent: AgentToken,
    val window: LimitWindow,
    val pct: Int,
    val resetAtMin: Long,
    val measuredAtMin: Long,
) {
    /** [pct] while it can still be true; -1 (drawn as a dash) once the window reset before [nowMin], so the old use is stale. */
    fun pctAt(nowMin: Long): Int = if (resetAtMin in 0 until nowMin) -1 else pct
}

/**
 * One TEXT SPEND entry: tokens (uncached input plus output) and estimated cents, by period. The week is the last 7 days
 * and the month the last 30, both counting today. [unpriced]: some tokens came from a model with no price, so the cents
 * are a lower bound.
 */
data class SpendEntry(
    val agent: AgentToken,
    val measuredAtMin: Long,
    val tokToday: Long,
    val centsToday: Long,
    val tokWeek: Long,
    val centsWeek: Long,
    val tokMonth: Long,
    val centsMonth: Long,
    val unpriced: Boolean = false,
)

/** TEXT WEATHER. [wmoCode] -1 is unknown. */
data class WeatherInfo(
    val tempTenthsC: Int,
    val wmoCode: Int,
    val city: String,
)

/** The kind token of TEXT NET_INFO. A token this build does not know reads as [OTHER]. */
enum class NetKind(
    val token: String,
) {
    WIFI("wifi"),
    ETHERNET("ethernet"),
    CELLULAR("cellular"),
    OTHER("other"),
    NONE("none"),
    ;

    companion object {
        fun of(token: String): NetKind = entries.firstOrNull { it.token == token } ?: OTHER
    }
}

/** The band token of TEXT NET_INFO. */
enum class NetBand(
    val token: String,
) {
    GHZ_2_4("2.4"),
    GHZ_5("5"),
    GHZ_6("6"),
    UNKNOWN("?"),
    ;

    companion object {
        fun of(token: String): NetBand = entries.firstOrNull { it.token == token } ?: UNKNOWN
    }
}

/** TEXT NET_INFO. -1 is unknown; an address is empty when there is none. There is no network name. */
data class NetInfo(
    val kind: NetKind,
    val band: NetBand,
    val linkMbps: Int,
    val signalPct: Int,
    val localIp: String,
    val gateway: String,
)

/** The state token of TEXT SPEED_RESULT. */
enum class SpeedState(
    val token: String,
) {
    RUNNING("running"),
    DONE("done"),
    FAILED("failed"),
    CANCELLED("cancelled"),
    CAPPED("capped"),
    ;

    companion object {
        fun of(token: String): SpeedState? = entries.firstOrNull { it.token == token }
    }
}

/** TEXT SPEED_RESULT. Speeds in Kbit/s and times in ms, -1 unknown; [bytes] is what the test moved. */
data class SpeedResult(
    val state: SpeedState,
    val downKbps: Long,
    val upKbps: Long,
    val latencyMs: Int,
    val jitterMs: Int,
    val atUnixMin: Long,
    val bytes: Long,
)

/** BLOB PERM_ASK, decoded. [complete] false means the text was over the cap and was not sent: answer on the laptop. */
data class PermAsk(
    val id: String,
    val complete: Boolean,
    val agent: AgentToken,
    val tool: String,
    val text: String,
)

/**
 * Which list of the GitHub page a BLOB GITHUB line belongs to, by its one-letter [char] on the wire: R is pull
 * requests waiting on the user's review, M the user's own, N the notification count.
 */
enum class GithubSection(
    val char: Char,
) {
    REVIEW('R'),
    MINE('M'),
    NOTIFICATIONS('N'),
    ;

    companion object {
        fun of(token: String): GithubSection? =
            if (token.length == 1) entries.firstOrNull { it.char == token[0] } else null
    }
}

/** One BLOB GITHUB line. For [GithubSection.NOTIFICATIONS], [number] is the count and the rest is empty. */
data class GithubRow(
    val section: GithubSection,
    val repo: String,
    val number: Int,
    val title: String,
    val draft: Boolean,
    val ci: CiToken,
)

/**
 * The decoders of the dashboard wire (TEXT 27..30 and 33..35, BLOB PERM_ASK and GITHUB), held to
 * protocol/dashboard-vectors.txt. Pure and never throwing: a hostile or short payload yields an empty list or null,
 * an entry that does not parse is dropped and the rest still count.
 */
object DashWire {
    private const val STATS_FIELDS = 11
    private const val INFO_FIELDS = 5
    private const val DISK_FIELDS = 4
    private const val LIMIT_FIELDS = 5
    private const val SPEND_FIELDS = 9
    private const val SESSION_FIELDS = 4
    private val UNPRICED_FLAGS = setOf("0", "1")
    private const val WEATHER_FIELDS = 3
    private const val NET_FIELDS = 6
    private const val SPEED_FIELDS = 7
    private const val MAX_LINK_MBPS = 100_000
    private const val MAX_KBPS = 100_000_000L
    private const val MAX_MS = 3_600_000
    private const val MAX_UNIX_MIN = 100_000_000L
    private const val MAX_TEST_BYTES = 35_000_000L
    private const val GITHUB_FIELDS = 6
    private const val PERM_PARTS = 3
    private const val PERCENT_MAX = 100
    private const val MAX_DISKS = 6
    private const val MAX_SESSIONS = 8
    private const val MAX_PER_SECTION = 10
    private const val MAX_ASK_BYTES = 4096
    private const val MAX_TOOL = 24
    private const val MAX_MINUTES = 525_600
    private const val TEMP_MAX = 999
    private const val WMO_MAX = 99
    private const val ID_LENGTH = 12

    /** Indexes of the percent fields of STATS: cpu, ram, gpu, vram and packet loss. */
    private val percentFields = setOf(0, 1, 2, 3, 7)

    private val ipv4Pattern = Regex("""(\d{1,3}\.){3}\d{1,3}""")
    private val toolPattern = Regex("[A-Za-z0-9_.-]{1,$MAX_TOOL}")
    private val idPattern = Regex("[0-9a-f]{$ID_LENGTH}")

    fun stats(text: String): StatsSample? {
        val parts = text.split('/')
        if (parts.size != STATS_FIELDS) return null
        val v =
            parts.mapIndexed { index, part ->
                val n = part.toIntOrNull() ?: return null
                when {
                    n < 0 -> -1
                    index in percentFields -> minOf(n, PERCENT_MAX)
                    else -> n
                }
            }
        return StatsSample(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8], v[9], v[10])
    }

    fun statsInfo(text: String): StatsInfo? {
        val parts = text.split('|')
        if (parts.size != INFO_FIELDS) return null
        val ram = parts[1].toIntOrNull() ?: return null
        val vram = parts[3].toIntOrNull() ?: return null
        return StatsInfo(parts[0], maxOf(ram, -1), parts[2], maxOf(vram, -1), parts[4])
    }

    fun disks(text: String): List<DiskEntry> =
        entries(text)
            .mapNotNull { entry ->
                val p = entry.split(':')
                val used = p.getOrNull(1)?.toIntOrNull()
                val total = p.getOrNull(2)?.toIntOrNull()
                val free = p.getOrNull(3)?.toIntOrNull()
                if (p.size != DISK_FIELDS || used == null || total == null || free == null) {
                    null
                } else {
                    DiskEntry(p[0], if (used < 0) -1 else minOf(used, PERCENT_MAX), maxOf(total, -1), maxOf(free, -1))
                }
            }.take(MAX_DISKS)

    fun sessions(text: String): List<SessionEntry> =
        entries(text)
            .mapNotNull { entry ->
                val p = entry.split(':', limit = SESSION_FIELDS)
                val agent = AgentToken.of(p[0])
                val state = p.getOrNull(1)?.let(SessionStateToken::of)
                val context = p.getOrNull(2)?.toLongOrNull()
                if (p.size != SESSION_FIELDS || agent == null || state == null || context == null) {
                    null
                } else {
                    SessionEntry(agent, state, p[3], if (context in 0..PERCENT_MAX.toLong()) context.toInt() else -1)
                }
            }.take(MAX_SESSIONS)

    /** Null for an unknown form: zero, past a year, a leading zero or sign, a fraction, `5h`, an empty `~` name. */
    fun limitWindow(text: String): LimitWindow? =
        when {
            text == WireWindow.CONTEXT -> {
                LimitWindow.Context
            }

            text.startsWith(WireWindow.NAMED_PREFIX) -> {
                text.substring(WireWindow.NAMED_PREFIX.length).takeIf { it.isNotBlank() }?.let(LimitWindow::Named)
            }

            else -> {
                text
                    .toIntOrNull()
                    ?.takeIf { it in 1..MAX_MINUTES && it.toString() == text }
                    ?.let(LimitWindow::Minutes)
            }
        }

    fun limits(text: String): List<LimitEntry> =
        entries(text).mapNotNull { entry ->
            val p = entry.split(':')
            val agent = AgentToken.of(p[0])
            val window = p.getOrNull(1)?.let(::limitWindow)
            val pct = p.getOrNull(2)?.toIntOrNull()
            val reset = p.getOrNull(3)?.toLongOrNull()
            val measured = p.getOrNull(4)?.toLongOrNull()
            if (p.size != LIMIT_FIELDS) return@mapNotNull null
            if (agent == null || window == null || pct == null) return@mapNotNull null
            if (reset == null || measured == null) return@mapNotNull null
            LimitEntry(agent, window, pct.coerceIn(0, PERCENT_MAX), maxOf(reset, -1L), maxOf(measured, -1L))
        }

    fun spend(text: String): List<SpendEntry> =
        entries(text).mapNotNull { entry ->
            val p = entry.split('/')
            val agent = AgentToken.of(p[0])
            val n = p.drop(1).map { it.toLongOrNull() }
            if (p.size != SPEND_FIELDS || agent == null || n.any { it == null } || p.last() !in UNPRICED_FLAGS) {
                null
            } else {
                val v = n.filterNotNull()
                SpendEntry(
                    agent,
                    maxOf(v[0], -1L),
                    maxOf(v[1], 0L),
                    maxOf(v[2], 0L),
                    maxOf(v[3], 0L),
                    maxOf(v[4], 0L),
                    maxOf(v[5], 0L),
                    maxOf(v[6], 0L),
                    p.last() == "1",
                )
            }
        }

    /** Null for the empty payload (no weather) and for anything that is not `temp/wmo/city`. */
    fun weather(text: String): WeatherInfo? {
        val p = text.split('/', limit = WEATHER_FIELDS)
        val temp = p.getOrNull(0)?.toIntOrNull()
        val wmo = p.getOrNull(1)?.toIntOrNull()
        if (p.size != WEATHER_FIELDS || temp == null || wmo == null) return null
        return WeatherInfo(
            temp.coerceIn(-TEMP_MAX, TEMP_MAX),
            if (wmo in 0..WMO_MAX) wmo else -1,
            p[2].replace('/', ' ').trim(),
        )
    }

    /** Null unless six `|` fields with numbers where numbers go. An unknown kind or band degrades, it does not drop. */
    fun netInfo(text: String): NetInfo? {
        val p = text.split('|')
        if (p.size != NET_FIELDS) return null
        val link = p[2].toIntOrNull() ?: return null
        val signal = p[3].toIntOrNull() ?: return null
        return NetInfo(
            NetKind.of(p[0]),
            NetBand.of(p[1]),
            if (link < 0) -1 else minOf(link, MAX_LINK_MBPS),
            if (signal < 0) -1 else minOf(signal, PERCENT_MAX),
            ip(p[4]),
            ip(p[5]),
        )
    }

    /** Null for an unknown state or anything that is not seven `|` fields with numbers where numbers go. */
    fun speedResult(text: String): SpeedResult? {
        val p = text.split('|')
        if (p.size != SPEED_FIELDS) return null
        val state = SpeedState.of(p[0]) ?: return null
        val down = p[1].toLongOrNull() ?: return null
        val up = p[2].toLongOrNull() ?: return null
        val latency = p[3].toIntOrNull() ?: return null
        val jitter = p[4].toIntOrNull() ?: return null
        val at = p[5].toLongOrNull() ?: return null
        val bytes = p[6].toLongOrNull() ?: return null
        return SpeedResult(
            state,
            if (down < 0) -1 else minOf(down, MAX_KBPS),
            if (up < 0) -1 else minOf(up, MAX_KBPS),
            if (latency < 0) -1 else minOf(latency, MAX_MS),
            if (jitter < 0) -1 else minOf(jitter, MAX_MS),
            if (at in 0..MAX_UNIX_MIN) at else -1,
            bytes.coerceIn(0, MAX_TEST_BYTES),
        )
    }

    private fun ip(text: String): String = if (ipv4Pattern.matches(text)) text else ""

    /** The PERM_ASK label `id/f`: the id and whether the text is complete. Null for anything else. */
    fun permLabel(label: String): Pair<String, Boolean>? {
        val slash = label.indexOf('/')
        if (slash != ID_LENGTH) return null
        val id = label.substring(0, slash)
        val flag = label.substring(slash + 1)
        if (!idPattern.matches(id) || (flag != "0" && flag != "1")) return null
        return id to (flag == "1")
    }

    /**
     * The PERM_ASK body `agent\ntool\ntext`. Null when the agent is unknown, the tool is not 1..24 name characters
     * or the body is over 4096 bytes.
     */
    fun permAsk(
        id: String,
        complete: Boolean,
        body: ByteArray,
    ): PermAsk? {
        if (body.size > MAX_ASK_BYTES) return null
        val p = String(body, Charsets.UTF_8).split('\n', limit = PERM_PARTS)
        val agent = AgentToken.of(p[0])
        if (p.size != PERM_PARTS || agent == null || !toolPattern.matches(p[1])) return null
        // Cleaned again here (idempotent): a laptop that skipped it cannot put a live control or bidi character on the sheet.
        return PermAsk(id, complete, agent, p[1], cleanText(p[2]))
    }

    fun github(body: ByteArray): List<GithubRow> {
        val counts = HashMap<GithubSection, Int>()
        return String(body, Charsets.UTF_8)
            .split('\n')
            .mapNotNull { line ->
                val p = line.split('\t')
                val section = GithubSection.of(p[0])
                val number = p.getOrNull(2)?.toIntOrNull()
                if (p.size != GITHUB_FIELDS || section == null || number == null || number < 0) {
                    null
                } else {
                    val seen = counts.getOrDefault(section, 0)
                    counts[section] = seen + 1
                    if (seen >= MAX_PER_SECTION) {
                        null
                    } else {
                        GithubRow(section, p[1], number, p[3], p[4] == "d", CiToken.of(p[5]))
                    }
                }
            }
    }

    /** Every code point of Unicode category Cf, Zl or Zp: invisible, so never rendered raw (Trojan Source). */
    fun isInvisible(codePoint: Int): Boolean =
        when (Character.getType(codePoint).toByte()) {
            Character.FORMAT, Character.LINE_SEPARATOR, Character.PARAGRAPH_SEPARATOR -> true
            else -> false
        }

    private fun isControl(codePoint: Int): Boolean = Character.getType(codePoint).toByte() == Character.CONTROL

    /** The laptop's Clean(text, n), so the vectors can hold both ends to the same text. */
    fun clean(
        text: String,
        max: Int,
    ): String {
        val out = StringBuilder()
        var gap = false
        text.codePoints().forEach { cp ->
            if ((cp.toChar() in " |/:" && cp < SURROGATE_START) || isControl(cp) || isInvisible(cp)) {
                gap = out.isNotEmpty()
            } else {
                if (gap) out.append(' ')
                gap = false
                out.appendCodePoint(cp)
            }
        }
        val s = out.toString()
        val end = if (s.codePointCount(0, s.length) <= max) s.length else s.offsetByCodePoints(0, max)
        return s.substring(0, end).trimEnd(' ')
    }

    /**
     * The laptop's CleanText: keeps line feed and tab, shows every other control (Cc) and invisible character as
     * `<U+XXXX>`. Deletes nothing; idempotent.
     */
    fun cleanText(text: String): String {
        val out = StringBuilder()
        text.codePoints().forEach { cp ->
            when {
                cp == '\n'.code || cp == '\t'.code -> out.append(cp.toChar())
                isInvisible(cp) || isControl(cp) -> out.append("<U+").append(hex(cp)).append('>')
                else -> out.appendCodePoint(cp)
            }
        }
        return out.toString()
    }

    private fun hex(cp: Int): String = cp.toString(HEX).uppercase().padStart(HEX_DIGITS, '0')

    private const val HEX = 16
    private const val HEX_DIGITS = 4
    private const val SURROGATE_START = 0xD800

    private fun entries(text: String): List<String> = if (text.isEmpty()) emptyList() else text.split('|')
}

/** The phone's one answer to an approval (TEXT PERM_ANSWER): exactly `id/a` or `id/d`. */
object PermAnswer {
    fun text(
        id: String,
        allow: Boolean,
    ): String = id + '/' + if (allow) 'a' else 'd'
}
