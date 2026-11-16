package me.akshitbansal.edgepad.screens

import android.view.View
import android.widget.LinearLayout
import android.widget.TextView
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.link.ForegroundApp
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
        supported: Boolean,
        defaults: List<DialKind?>,
        onBack: () -> Unit,
    ): View {
        var profiles = Profiles(raw)
        var openProfile: String? = null
        var openRow: String? = null
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
            app: ForegroundApp,
            profile: Profile,
        ) {
            val corners = profile.corners
            val cornerChoices = listOf(same) + kinds.map { kindName(it) }
            cornerNames.forEachIndexed { corner, name ->
                val held = corners?.get(corner)
                pickRow(
                    "c$corner",
                    ui.string(name),
                    if (corners == null) same else kindName(held),
                    cornerChoices,
                    if (corners == null) 0 else 1 + kinds.indexOf(held),
                ) { pick ->
                    val next = (corners ?: defaults).toMutableList()
                    if (pick > 0) next[corner] = kinds[pick - 1]
                    change(app, profile.copy(corners = if (pick == 0) null else next))
                }
            }
            val gestureChoices = listOf(same) + actions.map { ui.string(it.nameRes) }
            for (gesture in Gesture.entries) {
                val set = profile.gestures[gesture]
                pickRow(
                    "g${gesture.name}",
                    ui.string(gesture.nameRes),
                    if (set == null) same else ui.string(set.nameRes),
                    gestureChoices,
                    if (set == null) 0 else 1 + actions.indexOf(set),
                ) { pick ->
                    val rest = profile.gestures - gesture
                    val next = if (pick == 0) rest else rest + (gesture to actions[pick - 1])
                    change(app, profile.copy(gestures = next))
                }
            }
            hairline()
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

        render = {
            list.removeAllViews()
            Column(ui, list).apply {
                if (profiles.all.isEmpty()) {
                    footnote(ui.string(R.string.profiles_empty))
                } else {
                    card {
                        profiles.all.forEachIndexed { i, (app, profile) ->
                            if (i > 0) hairline()
                            val opened = app.token == openProfile
                            val state = if (profile.isDefault) R.string.profile_default else R.string.profile_custom
                            val end = ui.secondary(ui.string(state), Type.VALUE)
                            val name = ui.text(shown(app), Type.BODY, ui.palette.ink)
                            add(
                                ui.field(name, end).apply {
                                    minimumHeight = ui.dp(Space.TOUCH)
                                    ui.tappable(this) {
                                        openProfile = if (opened) null else app.token
                                        openRow = null
                                        render()
                                    }
                                },
                            )
                            if (opened) editor(app, profile)
                        }
                    }
                }
                val none = current == null
                val label = if (none) R.string.profiles_none_in_front else R.string.profiles_add_current
                val addButton =
                    ui.button(ui.string(label), Ui.Style.OUTLINED) {
                        if (current != null) {
                            if (profiles.forApp(current.token) == null) {
                                profiles = profiles.with(current.token, current.name, Profile())
                                setRaw(profiles.encode())
                            }
                            openProfile = current.token
                            openRow = null
                            render()
                        }
                    }
                addButton.isEnabled = !none
                addButton.alpha = if (none) DISABLED_ALPHA else 1f
                add(addButton, Space.XL)
            }
        }
        render()
        return ui.page(
            ui.bar(ui.string(R.string.profiles_title), onBack, backLabel = ui.string(R.string.settings_surface_title)),
        ) {
            card {
                val toggle = ui.toggle(ui.string(R.string.profiles_toggle), on && supported) { setOn(it) }
                toggle.isEnabled = supported
                add(toggle)
            }
            footnote(ui.string(if (supported) R.string.profiles_caption else R.string.profiles_unsupported))
            section(ui.string(R.string.profiles_title))
            add(list)
        }
    }
}
