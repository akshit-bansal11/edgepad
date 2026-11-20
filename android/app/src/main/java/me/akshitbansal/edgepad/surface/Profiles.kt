package me.akshitbansal.edgepad.surface

import me.akshitbansal.edgepad.link.ForegroundApp

/**
 * What one app changes. [corners] are the four corner dials (null inside is no dial) or null to follow the default
 * arrangement; [gestures] hold only the gestures this app overrides.
 */
data class Profile(
    val corners: List<DialKind?>? = null,
    val gestures: Map<Gesture, GestureAction> = emptyMap(),
) {
    val isDefault: Boolean get() = corners == null && gestures.isEmpty()

    /** The dial in [corner] for this app, or [default] when the profile does not set the corners. */
    fun corner(
        corner: Int,
        default: DialKind?,
    ): DialKind? = if (corners != null && corners.size == Profiles.CORNERS) corners[corner] else default

    /** The action for [gesture] in this app, or [default] when the profile does not override it. */
    fun gesture(
        gesture: Gesture,
        default: GestureAction,
    ): GestureAction = gestures[gesture] ?: default
}

/**
 * Per-app profiles, kept as one small string in preferences and parsed once, here. Records are joined by a newline,
 * a record's fields by U+001F: token, name, the four corners joined by a comma (a lone `-` for the default, else
 * `NONE` or a [DialKind] name each), then `GESTURE=ACTION` pairs joined by a comma. Anything malformed decodes to an
 * empty set, and an unknown name is ignored, as [Shapes] does. The name is personal data: never log it.
 */
class Profiles(
    raw: String = "",
) {
    private val byToken: Map<String, Pair<ForegroundApp, Profile>> = decode(raw)

    val all: List<Pair<ForegroundApp, Profile>> get() = byToken.values.toList()

    fun forApp(token: String): Profile? = byToken[token]?.second

    /** This set with [token] set to [profile]; unchanged for a bad token or a 25th app. */
    fun with(
        token: String,
        name: String,
        profile: Profile,
    ): Profiles {
        if (!validToken(token) || (token !in byToken && byToken.size >= MAX)) return this
        val next = LinkedHashMap(byToken)
        next[token] = ForegroundApp(token, clean(name)) to profile
        return Profiles(encode(next.values))
    }

    fun without(token: String): Profiles =
        if (token in byToken) Profiles(encode(byToken.values.filter { it.first.token != token })) else this

    fun encode(): String = encode(byToken.values)

    companion object {
        const val MAX = 24
        const val CORNERS = 4
        private const val RECORD = '\n'
        private const val FIELD = '\u001F'
        private const val NONE = "NONE"
        private const val DEFAULT = "-"

        private fun validToken(token: String) = ForegroundApp.parse("$token/")?.token == token

        private fun clean(name: String) = name.filterNot { it == RECORD || it == FIELD }

        private fun encode(entries: Collection<Pair<ForegroundApp, Profile>>): String =
            entries.joinToString(RECORD.toString()) { (app, profile) ->
                val corners = profile.corners?.joinToString(",") { it?.name ?: NONE } ?: DEFAULT
                val gestures = profile.gestures.entries.joinToString(",") { "${it.key.name}=${it.value.name}" }
                listOf(app.token, clean(app.name), corners, gestures).joinToString(FIELD.toString())
            }

        private fun decode(raw: String): Map<String, Pair<ForegroundApp, Profile>> {
            val out = LinkedHashMap<String, Pair<ForegroundApp, Profile>>()
            if (raw.isEmpty()) return out
            for (record in raw.split(RECORD)) {
                val fields = record.split(FIELD)
                if (fields.size != 4 || !validToken(fields[0])) return LinkedHashMap()
                val token = fields[0]
                if (token in out) continue
                if (out.size >= MAX) break
                out[token] = ForegroundApp(token, fields[1]) to Profile(corners(fields[2]), gestures(fields[3]))
            }
            return out
        }

        /** Four known entries or the default: any other length, or any unknown name, follows the default. */
        private fun corners(text: String): List<DialKind?>? {
            val parts = text.split(",")
            if (parts.size != CORNERS) return null
            val kinds = ArrayList<DialKind?>(CORNERS)
            for (part in parts) {
                if (part == NONE) {
                    kinds.add(null)
                } else {
                    kinds.add(DialKind.entries.firstOrNull { it.name == part } ?: return null)
                }
            }
            return kinds
        }

        private fun gestures(text: String): Map<Gesture, GestureAction> {
            val out = LinkedHashMap<Gesture, GestureAction>()
            for (pair in text.split(",")) {
                val parts = pair.split("=")
                if (parts.size != 2) continue
                val gesture = Gesture.entries.firstOrNull { it.name == parts[0] } ?: continue
                val action = GestureAction.entries.firstOrNull { it.name == parts[1] } ?: continue
                out[gesture] = action
            }
            return out
        }
    }
}
