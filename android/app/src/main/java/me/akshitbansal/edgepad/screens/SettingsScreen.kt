package me.akshitbansal.edgepad.screens

import android.app.UiModeManager
import android.text.SpannableStringBuilder
import android.text.Spanned
import android.text.style.ForegroundColorSpan
import android.text.style.RelativeSizeSpan
import android.text.style.TypefaceSpan
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.LinearLayout
import android.widget.Switch
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.surface.DialKind
import me.akshitbansal.edgepad.surface.Placement
import kotlin.math.roundToInt

/** Connection, where each dial sits and how it feels, the trackpad, and the theme. Every change is saved as it is made. */
object SettingsScreen {
    /** What the connection section shows: the remembered laptop, if any, and its state. */
    class Connection(
        val name: String?,
        val detail: String,
    )

    private const val EDITOR_WIDTH_DP = 120f
    private const val EDITOR_HEIGHT_DP = 240f
    private const val LEADING = 1.5f
    private const val DISABLED_ALPHA = 0.5f
    private const val SUB_SIZE = 0.75f
    private const val SENSITIVITY_STEP = 0.1f
    private val sensitivitySteps =
        ((Settings.MAX_SENSITIVITY - Settings.MIN_SENSITIVITY) / SENSITIVITY_STEP)
            .roundToInt()

    fun build(
        ui: Ui,
        settings: Settings,
        connection: Connection,
        onForget: () -> Unit,
        onBack: () -> Unit,
    ): View {
        val rows = HashMap<DialKind, Switch>()
        val editor =
            PlacementEditor(ui.context, settings) { kind ->
                rows[kind]?.text =
                    dialLabel(ui, kind, settings.placement(kind))
            }
        return ui.page {
            add(header(ui, onBack))

            section(ui.string(R.string.settings_connection))
            hairline()
            val forget = if (connection.name != null) ui.chip(ui.string(R.string.forget), onForget) else null
            add(ui.row(ui.stack(connection.name ?: ui.string(R.string.no_laptop), connection.detail), forget))
            hairline()
            add(ui.toggle(ui.string(R.string.reconnect_automatically), settings.reconnect) { settings.reconnect = it })

            section(ui.string(R.string.settings_dials))
            hairline()
            val placement =
                LinearLayout(ui.context).apply {
                    setPadding(0, ui.dp(Space.L), 0, ui.dp(Space.L))
                    addView(editor, LinearLayout.LayoutParams(ui.dp(EDITOR_WIDTH_DP), ui.dp(EDITOR_HEIGHT_DP)))
                    val hint =
                        ui
                            .text(
                                ui.string(R.string.settings_dials_hint),
                                Type.CAPTION,
                                ui.palette.dim,
                                Type.plain,
                            ).apply {
                                setLineSpacing(0f, LEADING)
                            }
                    addView(
                        hint,
                        LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply {
                            marginStart =
                                ui.dp(Space.XL)
                        },
                    )
                }
            add(placement)
            for (kind in DialKind.entries) {
                hairline()
                rows[kind] =
                    add(
                        ui.toggle(
                            dialLabel(ui, kind, settings.placement(kind)),
                            settings.placement(kind) != null,
                        ) { on ->
                            settings.place(kind, if (on) Placement.of(kind.onAt) else null)
                            rows[kind]?.text = dialLabel(ui, kind, settings.placement(kind))
                            editor.refresh()
                        },
                    )
            }
            hairline()
            val backlight =
                twoLines(ui, ui.string(R.string.keyboard_backlight), ui.string(R.string.keyboard_backlight_sub))
            add(ui.toggle(backlight, false) {}).apply {
                isEnabled = false
                alpha = DISABLED_ALPHA
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
            add(ui.toggle(ui.string(R.string.haptic_ticks), settings.haptics) { settings.haptics = it })
            hairline()
            add(ui.toggle(ui.string(R.string.snap_round), settings.snap) { settings.snap = it })

            section(ui.string(R.string.settings_trackpad))
            hairline()
            add(
                ui.toggle(
                    ui.string(R.string.natural_scrolling),
                    settings.naturalScroll,
                ) { settings.naturalScroll = it },
            )
            hairline()
            add(ui.toggle(ui.string(R.string.gesture_hints), settings.hints) { settings.hints = it })

            section(ui.string(R.string.settings_appearance))
            hairline()
            val themes = listOf(ui.string(R.string.theme_dark), ui.string(R.string.theme_light))
            val theme = ui.segmented(themes, if (ui.palette.dark) 0 else 1) { i -> setTheme(ui, dark = i == 0) }
            add(ui.row(ui.text(ui.string(R.string.theme), Type.BODY, ui.palette.ink, Type.plain), theme))
            hairline()
        }
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

    private fun dialLabel(
        ui: Ui,
        kind: DialKind,
        placement: Placement?,
    ): CharSequence = twoLines(ui, ui.string(kind.nameRes), placementName(ui.context, placement))

    /** A name over a smaller monospace line, in one text so the row's Switch reads both. */
    private fun twoLines(
        ui: Ui,
        title: String,
        sub: String,
    ): CharSequence =
        SpannableStringBuilder(title).apply {
            append('\n')
            val start = length
            append(sub)
            setSpan(TypefaceSpan(Type.mono), start, length, Spanned.SPAN_EXCLUSIVE_EXCLUSIVE)
            setSpan(RelativeSizeSpan(SUB_SIZE), start, length, Spanned.SPAN_EXCLUSIVE_EXCLUSIVE)
            setSpan(ForegroundColorSpan(ui.palette.dim), start, length, Spanned.SPAN_EXCLUSIVE_EXCLUSIVE)
        }

    private fun sensitivityText(
        ui: Ui,
        sensitivity: Float,
    ): String = ui.string(R.string.sensitivity_value, sensitivity)

    private fun toStep(sensitivity: Float): Int =
        ((sensitivity - Settings.MIN_SENSITIVITY) / SENSITIVITY_STEP).roundToInt()

    private fun fromStep(step: Int): Float = Settings.MIN_SENSITIVITY + step * SENSITIVITY_STEP
}
