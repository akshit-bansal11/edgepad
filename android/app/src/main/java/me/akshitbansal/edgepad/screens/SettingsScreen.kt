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
    private const val PREVIEW_DP = 220f
    private const val PERCENT = 100
    private val backgroundNames =
        mapOf(
            Settings.Background.THEME to R.string.background_theme,
            Settings.Background.COLOR to R.string.background_color,
            Settings.Background.GRADIENT to R.string.background_gradient,
            Settings.Background.IMAGE to R.string.background_image,
        )
    private val patternLabels =
        mapOf(
            Settings.Pattern.NONE to R.string.pattern_none,
            Settings.Pattern.SQUARES to R.string.pattern_squares,
            Settings.Pattern.DOTS to R.string.pattern_dots,
            Settings.Pattern.CHECKER to R.string.pattern_checker,
        )
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
        onPickImage: () -> Unit,
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
            val preview = DialPreview(ui.context, settings)
            val length =
                ui.mono(
                    dpText(ui, settings.dialLength),
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
                    length.text = dpText(ui, settings.dialLength)
                    preview.show(settings.dialLength, settings.dialHeight)
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
                    preview.show(settings.dialLength, settings.dialHeight)
                },
            ).contentDescription = ui.string(R.string.dial_height)
            add(preview, Space.M, ui.dp(PREVIEW_DP))
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
            add(controlColor(ui, settings))
            hairline()
            add(background(ui, settings, onPickImage))
            hairline()
        }

    /** Auto follows the background; custom opens a picker whose colour every control shares. */
    private fun controlColor(
        ui: Ui,
        settings: Settings,
    ): View =
        LinearLayout(ui.context).apply {
            orientation = LinearLayout.VERTICAL
            val picker =
                ColorPicker.build(ui, ui.string(R.string.control_color), settings.controlColor ?: ui.palette.ink) {
                    settings.controlColor = it
                }
            picker.visibility = if (settings.controlColor == null) View.GONE else View.VISIBLE
            val modes = listOf(ui.string(R.string.control_color_auto), ui.string(R.string.control_color_custom))
            val mode =
                ui.segmented(modes, if (settings.controlColor == null) 0 else 1) { i ->
                    if (i == 0) settings.controlColor = null else settings.controlColor = ui.palette.ink
                    picker.visibility = if (i == 0) View.GONE else View.VISIBLE
                    refresh(this) { controlColor(ui, settings) }
                }
            addView(ui.row(ui.text(ui.string(R.string.control_color), Type.BODY, ui.palette.ink, Type.plain), mode))
            addView(picker)
        }

    /** The surface's background: its kind, then the rows that kind needs, then the pattern over it. */
    private fun background(
        ui: Ui,
        settings: Settings,
        onPickImage: () -> Unit,
    ): View =
        LinearLayout(ui.context).apply {
            orientation = LinearLayout.VERTICAL
            val kinds = Settings.Background.entries
            val names = kinds.map { ui.string(backgroundNames.getValue(it)) }
            val kind =
                ui.segmented(names, kinds.indexOf(settings.background)) { i ->
                    settings.background = kinds[i]
                    refresh(this) { background(ui, settings, onPickImage) }
                }
            addView(ui.row(ui.text(ui.string(R.string.background), Type.BODY, ui.palette.ink, Type.plain), kind))
            when (settings.background) {
                Settings.Background.THEME -> {
                    Unit
                }

                Settings.Background.COLOR -> {
                    addView(
                        ColorPicker.build(ui, ui.string(R.string.background_color), settings.backgroundColor) {
                            settings.backgroundColor =
                                it
                        },
                    )
                }

                Settings.Background.GRADIENT -> {
                    addView(
                        ColorPicker.build(ui, ui.string(R.string.background_color), settings.backgroundColor) {
                            settings.backgroundColor =
                                it
                        },
                    )
                    addView(
                        ColorPicker.build(ui, ui.string(R.string.gradient_end), settings.gradientEnd) {
                            settings.gradientEnd =
                                it
                        },
                    )
                    val angle =
                        ui.mono(
                            degreesText(ui, settings.gradientAngle),
                            Type.CAPTION,
                            ui.palette.ink,
                            0f,
                        )
                    addView(
                        ui.row(
                            ui.text(ui.string(R.string.gradient_angle), Type.BODY, ui.palette.ink, Type.plain),
                            angle,
                        ),
                    )
                    addView(
                        ui
                            .ruler(Settings.MAX_ANGLE.toInt(), settings.gradientAngle.roundToInt()) { step ->
                                settings.gradientAngle = step.toFloat()
                                angle.text = ui.string(R.string.degrees_value, step)
                            }.apply { contentDescription = ui.string(R.string.gradient_angle) },
                    )
                }

                Settings.Background.IMAGE -> {
                    val present = settings.backgroundImage.exists()
                    val state = if (present) R.string.background_image_set else R.string.background_image_none
                    addView(
                        ui.row(
                            ui.text(ui.string(state), Type.BODY, ui.palette.ink, Type.plain),
                            ui.chip(ui.string(R.string.background_pick_image), onPickImage),
                        ),
                    )
                }
            }
            val patterns = Settings.Pattern.entries
            val patternNames = patterns.map { ui.string(patternLabels.getValue(it)) }
            val pattern =
                ui.segmented(patternNames, patterns.indexOf(settings.pattern)) { i ->
                    settings.pattern = patterns[i]
                    refresh(this) { background(ui, settings, onPickImage) }
                }
            addView(ui.row(ui.text(ui.string(R.string.pattern), Type.BODY, ui.palette.ink, Type.plain), pattern))
            if (settings.pattern != Settings.Pattern.NONE) {
                val size =
                    ui.mono(
                        dpText(ui, settings.patternSize),
                        Type.CAPTION,
                        ui.palette.ink,
                        0f,
                    )
                addView(ui.row(ui.text(ui.string(R.string.pattern_size), Type.BODY, ui.palette.ink, Type.plain), size))
                addView(
                    ui
                        .ruler(
                            (Settings.MAX_PATTERN_SIZE - Settings.MIN_PATTERN_SIZE).toInt(),
                            (settings.patternSize - Settings.MIN_PATTERN_SIZE).roundToInt(),
                        ) { step ->
                            settings.patternSize = Settings.MIN_PATTERN_SIZE + step
                            size.text = dpText(ui, settings.patternSize)
                        }.apply { contentDescription = ui.string(R.string.pattern_size) },
                )
                val opacity =
                    ui.mono(
                        percentText(ui, settings.patternOpacity),
                        Type.CAPTION,
                        ui.palette.ink,
                        0f,
                    )
                addView(
                    ui.row(
                        ui.text(ui.string(R.string.pattern_opacity), Type.BODY, ui.palette.ink, Type.plain),
                        opacity,
                    ),
                )
                addView(
                    ui
                        .ruler(PERCENT, (settings.patternOpacity * PERCENT).roundToInt()) { step ->
                            settings.patternOpacity = step / PERCENT.toFloat()
                            opacity.text = ui.string(R.string.percent_value, step)
                        }.apply { contentDescription = ui.string(R.string.pattern_opacity) },
                )
                addView(
                    ColorPicker.build(ui, ui.string(R.string.pattern_color), settings.patternColor) {
                        settings.patternColor =
                            it
                    },
                )
            }
        }

    /** Rebuilds a block in place after a choice that changes which rows it shows. */
    private fun refresh(
        block: LinearLayout,
        build: () -> View,
    ) {
        val parent = block.parent as? LinearLayout ?: return
        val index = parent.indexOfChild(block)
        parent.removeViewAt(index)
        parent.addView(build(), index, block.layoutParams)
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

    private fun dpText(
        ui: Ui,
        dp: Float,
    ): String = ui.string(R.string.dial_length_value, dp.roundToInt())

    private fun percentText(
        ui: Ui,
        fraction: Float,
    ): String = ui.string(R.string.percent_value, (fraction * PERCENT).roundToInt())

    private fun degreesText(
        ui: Ui,
        degrees: Float,
    ): String = ui.string(R.string.degrees_value, degrees.roundToInt())

    private fun toStep(sensitivity: Float): Int =
        ((sensitivity - Settings.MIN_SENSITIVITY) / SENSITIVITY_STEP).roundToInt()

    private fun fromStep(step: Int): Float = Settings.MIN_SENSITIVITY + step * SENSITIVITY_STEP
}
