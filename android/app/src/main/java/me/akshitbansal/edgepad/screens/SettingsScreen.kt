package me.akshitbansal.edgepad.screens

import android.app.AlertDialog
import android.app.UiModeManager
import android.view.Gravity
import android.view.View
import android.widget.LinearLayout
import android.widget.TextView
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.surface.ControlSurface
import me.akshitbansal.edgepad.surface.DialKind
import kotlin.math.roundToInt

/** Connection, what each corner does, how the dials feel, the trackpad, and the theme. Every change is saved as it is made. */
object SettingsScreen {
    /** What the connection section shows: the remembered laptop, if any, and its state. */
    class Connection(
        val name: String?,
        val detail: String,
    )

    private const val LENGTH_STEP = 10f
    private const val HEIGHT_STEP = 0.1f
    private const val SENSITIVITY_STEP = 0.1f
    private val sensitivitySteps =
        ((Settings.MAX_SENSITIVITY - Settings.MIN_SENSITIVITY) / SENSITIVITY_STEP)
            .roundToInt()
    private val lengthSteps = ((Settings.MAX_DIAL_LENGTH - Settings.MIN_DIAL_LENGTH) / LENGTH_STEP).roundToInt()
    private val heightSteps = ((Settings.MAX_DIAL_HEIGHT - Settings.MIN_DIAL_HEIGHT) / HEIGHT_STEP).roundToInt()
    private val cornerNames =
        listOf(
            R.string.corner_top_left,
            R.string.corner_top_right,
            R.string.corner_bottom_right,
            R.string.corner_bottom_left,
        )

    fun build(
        ui: Ui,
        settings: Settings,
        connection: Connection,
        onForget: () -> Unit,
        onGestures: () -> Unit,
        onMediaLayout: () -> Unit,
        onBack: () -> Unit,
    ): View =
        ui.page {
            add(header(ui, onBack))

            section(ui.string(R.string.settings_connection))
            hairline()
            val forget = if (connection.name != null) ui.chip(ui.string(R.string.forget), onForget) else null
            add(ui.row(ui.stack(connection.name ?: ui.string(R.string.no_laptop), connection.detail), forget))
            hairline()
            add(ui.toggle(ui.string(R.string.reconnect_automatically), settings.reconnect) { settings.reconnect = it })

            section(ui.string(R.string.settings_corners))
            for (corner in 0 until ControlSurface.CORNERS) {
                hairline()
                add(cornerRow(ui, settings, corner))
            }

            section(ui.string(R.string.settings_feel))
            hairline()
            val value = ui.mono(sensitivityText(ui, settings.sensitivity), Type.CAPTION, ui.palette.ink, 0f)
            add(ui.row(ui.text(ui.string(R.string.slide_sensitivity), Type.BODY, ui.palette.ink, Type.plain), value))
            add(
                ui.ruler(sensitivitySteps, toStep(settings.sensitivity)) { step ->
                    settings.sensitivity = fromStep(step)
                    value.text = sensitivityText(ui, settings.sensitivity)
                },
            ).contentDescription = ui.string(R.string.slide_sensitivity)
            hairline()
            val length =
                ui.mono(
                    ui.string(R.string.dial_length_value, settings.dialLength.roundToInt()),
                    Type.CAPTION,
                    ui.palette.ink,
                    0f,
                )
            add(ui.row(ui.text(ui.string(R.string.dial_length), Type.BODY, ui.palette.ink, Type.plain), length))
            add(
                ui.ruler(
                    lengthSteps,
                    ((settings.dialLength - Settings.MIN_DIAL_LENGTH) / LENGTH_STEP).roundToInt(),
                ) { step ->
                    settings.dialLength = Settings.MIN_DIAL_LENGTH + step * LENGTH_STEP
                    length.text = ui.string(R.string.dial_length_value, settings.dialLength.roundToInt())
                },
            ).contentDescription = ui.string(R.string.dial_length)
            hairline()
            val height =
                ui.mono(
                    ui.string(R.string.multiplier_value, settings.dialHeight),
                    Type.CAPTION,
                    ui.palette.ink,
                    0f,
                )
            add(ui.row(ui.text(ui.string(R.string.dial_height), Type.BODY, ui.palette.ink, Type.plain), height))
            add(
                ui.ruler(
                    heightSteps,
                    ((settings.dialHeight - Settings.MIN_DIAL_HEIGHT) / HEIGHT_STEP).roundToInt(),
                ) { step ->
                    settings.dialHeight = Settings.MIN_DIAL_HEIGHT + step * HEIGHT_STEP
                    height.text = ui.string(R.string.multiplier_value, settings.dialHeight)
                },
            ).contentDescription = ui.string(R.string.dial_height)
            hairline()
            add(ui.toggle(ui.string(R.string.haptic_ticks), settings.haptics) { settings.haptics = it })
            hairline()
            add(ui.toggle(ui.string(R.string.snap_round), settings.snap) { settings.snap = it })

            section(ui.string(R.string.settings_trackpad))
            hairline()
            add(linkRow(ui, ui.string(R.string.gestures_title), onGestures))
            hairline()
            add(
                ui.toggle(
                    ui.string(R.string.natural_scrolling),
                    settings.naturalScroll,
                ) { settings.naturalScroll = it },
            )
            hairline()
            add(ui.toggle(ui.string(R.string.gesture_hints), settings.hints) { settings.hints = it })

            section(ui.string(R.string.settings_media))
            hairline()
            add(linkRow(ui, ui.string(R.string.media_layout_title), onMediaLayout))

            section(ui.string(R.string.settings_appearance))
            hairline()
            val themes = listOf(ui.string(R.string.theme_dark), ui.string(R.string.theme_light))
            val theme = ui.segmented(themes, if (ui.palette.dark) 0 else 1) { i -> setTheme(ui, dark = i == 0) }
            add(ui.row(ui.text(ui.string(R.string.theme), Type.BODY, ui.palette.ink, Type.plain), theme))
            hairline()
        }

    private fun header(
        ui: Ui,
        onBack: () -> Unit,
    ): View =
        LinearLayout(ui.context).apply {
            gravity = Gravity.CENTER_VERTICAL
            val back =
                Glyph(ui.context, Glyph.Shape.CHEVRON_LEFT, ui.palette.ink).apply {
                    contentDescription = ui.string(R.string.back)
                    ui.tappable(this, onBack)
                }
            addView(back, LinearLayout.LayoutParams(ui.dp(Space.TOUCH), ui.dp(Space.TOUCH)))
            addView(
                ui.text(
                    ui.string(R.string.settings_title),
                    Type.HEADING,
                    ui.palette.ink,
                    Type.sans,
                    Type.TRACKING_TIGHT,
                ),
            )
        }

    /** A row that opens another screen. */
    private fun linkRow(
        ui: Ui,
        label: String,
        onOpen: () -> Unit,
    ): View {
        val chevron = Glyph(ui.context, Glyph.Shape.CHEVRON_RIGHT, ui.palette.dim)
        val row =
            ui.row(
                ui.text(label, Type.BODY, ui.palette.ink, Type.plain),
                LinearLayout(
                    ui.context,
                ).apply { addView(chevron, LinearLayout.LayoutParams(ui.dp(Space.XL), ui.dp(Space.XL))) },
            )
        ui.tappable(row, onOpen)
        return row
    }

    private fun cornerRow(
        ui: Ui,
        settings: Settings,
        corner: Int,
    ): View {
        val current = ui.mono(kindName(ui, settings.corner(corner)), Type.SMALL, ui.palette.dim, Type.TRACKING_ROW)
        val end =
            LinearLayout(ui.context).apply {
                gravity = Gravity.CENTER_VERTICAL
                addView(current)
                addView(
                    Glyph(ui.context, Glyph.Shape.CHEVRON_RIGHT, ui.palette.dim),
                    LinearLayout.LayoutParams(ui.dp(Space.XL), ui.dp(Space.XL)),
                )
            }
        val row = ui.row(ui.text(ui.string(cornerNames[corner]), Type.BODY, ui.palette.ink, Type.plain), end)
        ui.tappable(row) { pickCorner(ui, settings, corner, current) }
        return row
    }

    private fun pickCorner(
        ui: Ui,
        settings: Settings,
        corner: Int,
        current: TextView,
    ) {
        val kinds = listOf<DialKind?>(null) + DialKind.entries
        val names = kinds.map { kindName(ui, it) }.toTypedArray()
        AlertDialog
            .Builder(ui.context)
            .setTitle(ui.string(cornerNames[corner]))
            .setSingleChoiceItems(names, kinds.indexOf(settings.corner(corner))) { dialog, which ->
                settings.setCorner(corner, kinds[which])
                current.text = names[which]
                dialog.dismiss()
            }.show()
    }

    private fun kindName(
        ui: Ui,
        kind: DialKind?,
    ): String = if (kind == null) ui.string(R.string.corner_off) else ui.string(kind.nameRes)

    private fun setTheme(
        ui: Ui,
        dark: Boolean,
    ) {
        if (dark == ui.palette.dark) return
        // The system keeps the choice for this app and rebuilds the activity; the link survives the rebuild.
        ui.context.getSystemService(UiModeManager::class.java)?.setApplicationNightMode(
            if (dark) UiModeManager.MODE_NIGHT_YES else UiModeManager.MODE_NIGHT_NO,
        )
    }

    private fun sensitivityText(
        ui: Ui,
        sensitivity: Float,
    ): String = ui.string(R.string.multiplier_value, sensitivity)

    private fun toStep(sensitivity: Float): Int =
        ((sensitivity - Settings.MIN_SENSITIVITY) / SENSITIVITY_STEP).roundToInt()

    private fun fromStep(step: Int): Float = Settings.MIN_SENSITIVITY + step * SENSITIVITY_STEP
}
