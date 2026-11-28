package me.akshitbansal.edgepad.screens

import android.view.View
import android.widget.LinearLayout
import android.widget.TextView
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.link.ForegroundApp
import me.akshitbansal.edgepad.protocol.AppKindToken
import me.akshitbansal.edgepad.surface.AutoProfiles
import me.akshitbansal.edgepad.surface.DialKind
import me.akshitbansal.edgepad.surface.Gesture
import me.akshitbansal.edgepad.surface.GestureAction
import me.akshitbansal.edgepad.surface.Profile
import me.akshitbansal.edgepad.surface.Profiles

/**
 * App profiles: a switch, the profiles, and a button that adds the app in front. Each profile opens in place into its
 * four corner dials and its gestures, every one "Same as default" or a choice. Stored as one string by `setRaw`.
 */
object ProfilesScreen {
    private val cornerNames =
        listOf(
            R.string.corner_top_left,
            R.string.corner_top_right,
            R.string.corner_bottom_right,
            R.string.corner_bottom_left,
        )

    private const val DISABLED_ALPHA = 0.4f

    /**
     * [defaults] are the four corner dials the surface shows when no profile applies; a profile that changes one corner
     * starts the other three from them. The app name is personal data: it is shown here and never logged.
     */
    fun build(
        ui: Ui,
        on: Boolean,
        setOn: (Boolean) -> Unit,
        raw: String,
        setRaw: (String) -> Unit,
        current: ForegroundApp?,
        offered: Boolean,
        openApps: List<ForegroundApp>?,
        picking: Boolean,
        setPicking: (Boolean) -> Unit,
        supported: Boolean,
        split: Boolean,
        autoOn: Boolean,
        setAutoOn: (Boolean) -> Unit,
        autoRaw: String,
        setAutoRaw: (String) -> Unit,
        autoSupported: Boolean,
        defaults: List<DialKind?>,
        onBack: () -> Unit,
    ): View {
        var profiles = Profiles(raw)
        var masterOn = on
        var auto = Profiles(autoRaw)
        var openKind: AppKindToken? = null
        var openProfile: String? = null
        var openRow: String? = null
        var picker = picking
        val list = LinearLayout(ui.context).apply { orientation = LinearLayout.VERTICAL }
        val kinds = listOf<DialKind?>(null) + DialKind.entries
        val actions = GestureAction.entries
        val same = ui.string(R.string.profile_default)

        fun kindName(kind: DialKind?) = if (kind == null) ui.string(R.string.corner_off) else ui.string(kind.nameRes)

        fun shown(app: ForegroundApp) = app.name.ifEmpty { app.token }

        lateinit var render: () -> Unit

        fun change(
            app: ForegroundApp,
            profile: Profile,
        ) {
            profiles = profiles.with(app.token, app.name, profile)
            setRaw(profiles.encode())
            openRow = null
            render()
        }

        fun Column.pickRow(
            key: String,
            title: String,
            value: String,
            names: List<String>,
            selected: Int,
            onPick: (Int) -> Unit,
        ) {
            hairline()
            val end = ui.secondary(value, Type.VALUE, if (openRow == key) ui.palette.accent else ui.palette.dim)
            add(
                ui.field(title, end).apply {
                    minimumHeight = ui.dp(Space.TOUCH)
                    ui.tappable(this) {
                        openRow = if (openRow == key) null else key
                        render()
                    }
                },
            )
            if (openRow == key) {
                hairline()
                add(ui.choices(names, selected, onPick).apply { setPaddingRelative(ui.dp(Space.L), 0, 0, 0) })
            }
        }

        fun Column.editor(
            scope: String,
            profile: Profile,
            onChange: (Profile) -> Unit,
            footer: Column.() -> Unit,
        ) {
            val corners = profile.corners
            val cornerChoices = listOf(same) + kinds.map { kindName(it) }
            cornerNames.forEachIndexed { corner, name ->
                val held = corners?.get(corner)
                pickRow(
                    "$scope.c$corner",
                    ui.string(name),
                    if (corners == null) same else kindName(held),
                    cornerChoices,
                    if (corners == null) 0 else 1 + kinds.indexOf(held),
                ) { pick ->
                    val next = (corners ?: defaults).toMutableList()
                    if (pick > 0) next[corner] = kinds[pick - 1]
                    onChange(profile.copy(corners = if (pick == 0) null else next))
                }
            }
            val gestureChoices = listOf(same) + actions.map { ui.string(it.nameRes) }
            for (gesture in Gesture.entries) {
                val set = profile.gestures[gesture]
                val count = gesture.fingers
                val fingers = ui.context.resources.getQuantityString(R.plurals.gestures_fingers, count, count)
                pickRow(
                    "$scope.g${gesture.name}",
                    "$fingers · ${ui.string(gesture.nameRes)}",
                    if (set == null) same else ui.string(set.nameRes),
                    gestureChoices,
                    if (set == null) 0 else 1 + actions.indexOf(set),
                ) { pick ->
                    val rest = profile.gestures - gesture
                    val next = if (pick == 0) rest else rest + (gesture to actions[pick - 1])
                    onChange(profile.copy(gestures = next))
                }
            }
            hairline()
            footer()
        }

        fun Column.automatic() {
            val live = masterOn && supported && !split
            val kindRows =
                listOf(
                    AppKindToken.MEDIA to R.string.auto_kind_media,
                    AppKindToken.TIMELINE to R.string.auto_kind_timeline,
                    AppKindToken.BROWSER to R.string.auto_kind_browser,
                )
            val shownKinds =
                LinearLayout(ui.context).apply {
                    orientation = LinearLayout.VERTICAL
                    alpha = if (live) 1f else DISABLED_ALPHA
                }
            section(ui.string(R.string.auto_profiles_title))
            Column(ui, shownKinds).apply {
                card {
                    val toggle =
                        ui.toggle(ui.string(R.string.auto_profiles_toggle), autoOn && live) {
                            setAutoOn(it)
                        }
                    toggle.isEnabled = live
                    add(toggle)
                    kindRows.forEach { (kind, name) ->
                        val raw = auto.forApp(kind.token) ?: AutoProfiles.default(kind)
                        hairline()
                        val summary =
                            (0 until Profiles.CORNERS).joinToString(", ") { kindName(raw.corner(it, defaults[it])) }
                        val opened = openKind == kind
                        add(
                            ui.valueField(ui.string(name), summary).apply {
                                isEnabled = live
                                ui.tappable(this) {
                                    if (live) {
                                        openKind = if (opened) null else kind
                                        openRow = null
                                        render()
                                    }
                                }
                            },
                        )
                        if (opened && live) {
                            editor("auto.${kind.token}", raw, { next ->
                                auto = auto.with(kind.token, "", next)
                                setAutoRaw(auto.encode())
                                openRow = null
                                render()
                            }) {
                                add(
                                    ui.button(ui.string(R.string.profile_default), Ui.Style.QUIET) {
                                        auto = auto.with(kind.token, "", AutoProfiles.default(kind))
                                        setAutoRaw(auto.encode())
                                        openRow = null
                                        render()
                                    },
                                )
                            }
                        }
                    }
                }
            }
            add(shownKinds)
        }

        fun addProfile(app: ForegroundApp) {
            if (profiles.forApp(app.token) == null) {
                profiles = profiles.with(app.token, app.name, Profile())
                setRaw(profiles.encode())
            }
            openProfile = app.token
            openRow = null
        }

        fun Column.pickList() {
            if (openApps == null) {
                footnote(ui.string(R.string.profiles_pick_loading))
                return
            }
            val rows =
                (listOfNotNull(current) + openApps)
                    .distinctBy { it.token }
                    .filter { profiles.forApp(it.token) == null }
            if (rows.isEmpty()) {
                footnote(ui.string(R.string.profiles_pick_none))
                return
            }
            card(Space.M) {
                rows.forEachIndexed { i, app ->
                    if (i > 0) hairline()
                    val front = app.token == current?.token
                    val end = if (front) ui.secondary(ui.string(R.string.profiles_pick_front), Type.VALUE) else null
                    add(
                        ui.field(shown(app), end).apply {
                            minimumHeight = ui.dp(Space.TOUCH)
                            ui.tappable(this) {
                                addProfile(app)
                                picker = false
                                setPicking(false)
                                render()
                            }
                        },
                    )
                }
            }
        }

        render = {
            list.removeAllViews()
            Column(ui, list).apply {
                section(ui.string(R.string.profiles_title))
                if (profiles.all.isEmpty()) {
                    footnote(ui.string(R.string.profiles_empty))
                } else {
                    card {
                        profiles.all.forEachIndexed { i, (app, profile) ->
                            if (i > 0) hairline()
                            val opened = app.token == openProfile
                            val state = if (profile.isDefault) R.string.profile_default else R.string.profile_custom
                            val tint = if (opened) ui.palette.accent else ui.palette.dim
                            val end = ui.secondary(ui.string(state), Type.VALUE, tint)
                            add(
                                ui.field(shown(app), end).apply {
                                    minimumHeight = ui.dp(Space.TOUCH)
                                    ui.tappable(this) {
                                        openProfile = if (opened) null else app.token
                                        openRow = null
                                        render()
                                    }
                                },
                            )
                            if (opened) {
                                editor("app", profile, { change(app, it) }) {
                                    lateinit var delete: TextView
                                    delete =
                                        ui.button(ui.string(R.string.profiles_delete), Ui.Style.QUIET) {
                                            ui.confirm(
                                                delete,
                                                ui.string(R.string.profiles_delete),
                                                ui.string(R.string.profiles_delete_confirm, shown(app)),
                                                ui.string(R.string.profiles_delete),
                                            ) {
                                                profiles = profiles.without(app.token)
                                                setRaw(profiles.encode())
                                                openProfile = null
                                                openRow = null
                                                render()
                                            }
                                        }
                                    add(delete)
                                }
                            }
                        }
                    }
                }
                val none = current == null
                val label =
                    when {
                        offered -> R.string.profiles_add_open
                        none -> R.string.profiles_none_in_front
                        else -> R.string.profiles_add_current
                    }
                val blocked = split || (none && !offered)
                val addButton =
                    ui.button(ui.string(label), Ui.Style.OUTLINED) {
                        if (offered) {
                            picker = !picker
                            setPicking(picker)
                            render()
                        } else if (current != null) {
                            addProfile(current)
                            render()
                        }
                    }
                addButton.isEnabled = !blocked
                addButton.alpha = if (blocked) DISABLED_ALPHA else 1f
                add(addButton, Space.XL)
                if (offered && picker && !split) pickList()
                if (autoSupported) automatic()
            }
        }
        render()
        return ui.page(
            ui.bar(ui.string(R.string.profiles_title), onBack, backLabel = ui.string(R.string.settings_surface_title)),
        ) {
            card {
                val toggle =
                    ui.toggle(ui.string(R.string.profiles_toggle), on && supported && !split) {
                        masterOn = it
                        setOn(it)
                        render()
                    }
                toggle.isEnabled = supported && !split
                toggle.alpha = if (split) DISABLED_ALPHA else 1f
                add(toggle)
            }
            val caption =
                when {
                    split -> R.string.profiles_needs_four
                    supported -> R.string.profiles_caption
                    else -> R.string.profiles_unsupported
                }
            footnote(ui.string(caption))
            add(list)
        }
    }
}
