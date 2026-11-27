package me.akshitbansal.edgepad.surface

import me.akshitbansal.edgepad.protocol.AppKindToken

/**
 * The profiles the phone applies by kind of app, when the app in front has none of its own. Pure, so a JVM test holds
 * it. The stored ones are a [Profiles] encoding keyed by the kind's token; a kind with none stored uses its default.
 */
object AutoProfiles {
    // Top-left, clockwise: the four corners.
    private val media = listOf<DialKind?>(DialKind.MEDIA, DialKind.VOLUME, DialKind.ZOOM, DialKind.BRIGHTNESS)
    private val timeline = listOf<DialKind?>(DialKind.SCROLL_H, DialKind.SCROLL, DialKind.ZOOM, DialKind.VOLUME)
    private val browser = listOf<DialKind?>(DialKind.SCROLL, DialKind.TABS, DialKind.ZOOM, DialKind.VOLUME)

    /** The profile a kind starts from. */
    fun default(kind: AppKindToken): Profile =
        Profile(
            corners =
                when (kind) {
                    AppKindToken.MEDIA -> media
                    AppKindToken.TIMELINE -> timeline
                    AppKindToken.BROWSER -> browser
                },
        )

    /** The stored profile for [kind] in [raw], else its default. */
    fun profile(
        raw: String,
        kind: AppKindToken,
    ): Profile = Profiles(raw).forApp(kind.token) ?: default(kind)

    /**
     * What applies now: the app's own [custom] profile, else the automatic one for its [kind] while [autoOn] and
     * [profilesOn] (the master switch), else null, which is the default dials.
     */
    fun resolve(
        custom: Profile?,
        profilesOn: Boolean,
        autoOn: Boolean,
        kind: AppKindToken?,
        raw: String,
    ): Profile? =
        when {
            !profilesOn -> null
            custom != null -> custom
            autoOn && kind != null -> profile(raw, kind)
            else -> null
        }
}
