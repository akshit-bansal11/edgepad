package me.akshitbansal.edgepad.screens

import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.view.View
import android.widget.LinearLayout
import me.akshitbansal.edgepad.Palette
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.surface.Backdrop
import kotlin.math.roundToInt

/**
 * What the control surface is drawn in: one colour for every control, what sits behind them (the theme,
 * a colour, a gradient or an image), and a pattern over that, with a preview that follows every change.
 */
object AppearanceScreen {
    private const val PERCENT = 100
    private const val OPACITY_STEPS = 20
    private const val PREVIEW_DP = 110f
    private val angleRange = StepRange(0f, Settings.MAX_ANGLE, 15f)
    private val sizeRange = StepRange(Settings.MIN_PATTERN_SIZE, Settings.MAX_PATTERN_SIZE, 4f)
    private val keyTextRange = StepRange(Settings.MIN_KEY_TEXT_SCALE, Settings.MAX_KEY_TEXT_SCALE, 0.1f)

    private val backgroundNames =
        mapOf(
            Settings.Background.THEME to R.string.background_theme,
            Settings.Background.COLOR to R.string.background_color,
            Settings.Background.GRADIENT to R.string.background_gradient,
            Settings.Background.IMAGE to R.string.background_image,
        )
    private val patternNames =
        mapOf(
            Settings.Pattern.NONE to R.string.pattern_none,
            Settings.Pattern.SQUARES to R.string.pattern_squares,
            Settings.Pattern.DOTS to R.string.pattern_dots,
            Settings.Pattern.CHECKER to R.string.pattern_checker,
        )

    /** The Settings hub's one-line summary: background, then pattern. */
    fun summary(
        ui: Ui,
        settings: Settings,
    ): String =
        ui.string(backgroundNames.getValue(settings.background)) + " · " +
            ui.string(patternNames.getValue(settings.pattern))

    fun build(
        ui: Ui,
        settings: Settings,
        onPickImage: () -> Unit,
        onBack: () -> Unit,
    ): View {
        val preview = Preview(ui.context, settings)
        return ui.page(ui.bar(ui.string(R.string.appearance_title), onBack)) {
            columns(
                {
                    add(controlColor(ui, settings))
                    hairline()
                    add(macroButtons(ui, settings))
                    hairline()
                    add(keyboardText(ui, settings))
                    hairline()
                    add(background(ui, settings, preview, onPickImage))
                    hairline()
                },
                {
                    add(pattern(ui, settings, preview))
                    hairline()
                    add(preview, Space.L, ui.dp(PREVIEW_DP))
                },
            )
        }
    }

    /** Auto follows the background; custom shows a picker whose colour every control shares. */
    private fun controlColor(
        ui: Ui,
        settings: Settings,
    ): View =
        LinearLayout(ui.context).apply {
            orientation = LinearLayout.VERTICAL
            val modes = listOf(ui.string(R.string.control_color_auto), ui.string(R.string.control_color_custom))
            val mode =
                ui.segmented(modes, if (settings.controlColor == null) 0 else 1) { i ->
                    settings.controlColor = if (i == 0) null else ui.palette.ink
                    refresh(this) { controlColor(ui, settings) }
                }
            addView(ui.field(ui.string(R.string.control_color), mode))
            val chosen = settings.controlColor
            if (chosen != null) {
                addView(ColorPicker.build(ui, ui.string(R.string.control_color), chosen) { settings.controlColor = it })
            }
        }

    /**
     * Whether a macro button carries its name under its picture. Only ever half a choice: a slot the laptop
     * sent no picture for shows its name whichever way this is set, because the alternative is a button with
     * nothing on it at all.
     */
    private fun macroButtons(
        ui: Ui,
        settings: Settings,
    ): View {
        val styles = listOf(ui.string(R.string.macro_buttons_label), ui.string(R.string.macro_buttons_icon))
        val pick =
            ui.segmented(styles, if (settings.macroLabels) 0 else 1) { i ->
                settings.macroLabels = i == 0
            }
        return ui.field(ui.string(R.string.macro_buttons), pick)
    }

    /** How large the keyboard's key labels are drawn; the keyboard shrinks the lot if any would leave its key. */
    private fun keyboardText(
        ui: Ui,
        settings: Settings,
    ): View =
        ui.slider(
            ui.string(R.string.keyboard_text_size),
            keyTextRange.steps,
            keyTextRange.stepOf(settings.keyTextScale),
            { step -> ui.string(R.string.multiplier_value, keyTextRange.valueAt(step)) },
        ) { step ->
            settings.keyTextScale = keyTextRange.valueAt(step)
        }

    /** The kind of background, then the rows that kind needs. */
    private fun background(
        ui: Ui,
        settings: Settings,
        preview: Preview,
        onPickImage: () -> Unit,
    ): View =
        LinearLayout(ui.context).apply {
            orientation = LinearLayout.VERTICAL
            val kinds = Settings.Background.entries
            val kind =
                ui.segmented(
                    kinds.map { ui.string(backgroundNames.getValue(it)) },
                    kinds.indexOf(settings.background),
                ) { i ->
                    settings.background = kinds[i]
                    preview.update()
                    refresh(this) { background(ui, settings, preview, onPickImage) }
                }
            addView(ui.field(ui.string(R.string.background), kind))
            val colour = {
                ColorPicker.build(ui, ui.string(R.string.background_color), settings.backgroundColor) {
                    settings.backgroundColor = it
                    preview.update()
                }
            }
            when (settings.background) {
                Settings.Background.THEME -> {
                    Unit
                }

                Settings.Background.COLOR -> {
                    addView(colour())
                }

                Settings.Background.GRADIENT -> {
                    addView(colour())
                    addView(
                        ColorPicker.build(ui, ui.string(R.string.gradient_end), settings.gradientEnd) {
                            settings.gradientEnd = it
                            preview.update()
                        },
                    )
                    addView(
                        ui.slider(
                            ui.string(R.string.gradient_angle),
                            angleRange.steps,
                            angleRange.stepOf(settings.gradientAngle),
                            { step -> ui.string(R.string.degrees_value, angleRange.valueAt(step).roundToInt()) },
                        ) { step ->
                            settings.gradientAngle = angleRange.valueAt(step)
                            preview.update()
                        },
                    )
                }

                Settings.Background.IMAGE -> {
                    val present = settings.backgroundImage.exists()
                    val state = if (present) R.string.background_image_set else R.string.background_image_none
                    addView(ui.field(ui.string(state), ui.chip(ui.string(R.string.background_pick_image), onPickImage)))
                }
            }
        }

    /** The pattern over the background, and while there is one, its size, strength and colour. */
    private fun pattern(
        ui: Ui,
        settings: Settings,
        preview: Preview,
    ): View =
        LinearLayout(ui.context).apply {
            orientation = LinearLayout.VERTICAL
            val patterns = Settings.Pattern.entries
            val kind =
                ui.segmented(
                    patterns.map { ui.string(patternNames.getValue(it)) },
                    patterns.indexOf(settings.pattern),
                ) { i ->
                    settings.pattern = patterns[i]
                    preview.update()
                    refresh(this) { pattern(ui, settings, preview) }
                }
            addView(ui.field(ui.string(R.string.pattern), kind))
            if (settings.pattern == Settings.Pattern.NONE) return@apply
            addView(
                ui.slider(
                    ui.string(R.string.pattern_size),
                    sizeRange.steps,
                    sizeRange.stepOf(settings.patternSize),
                    { step -> ui.string(R.string.dial_length_value, sizeRange.valueAt(step).roundToInt()) },
                ) { step ->
                    settings.patternSize = sizeRange.valueAt(step)
                    preview.update()
                },
            )
            addView(
                ui.slider(
                    ui.string(R.string.pattern_opacity),
                    OPACITY_STEPS,
                    (settings.patternOpacity * OPACITY_STEPS).roundToInt(),
                    { step -> ui.string(R.string.percent_value, step * PERCENT / OPACITY_STEPS) },
                ) { step ->
                    settings.patternOpacity = step / OPACITY_STEPS.toFloat()
                    preview.update()
                },
            )
            addView(
                ColorPicker.build(ui, ui.string(R.string.pattern_color), settings.patternColor) {
                    settings.patternColor = it
                    preview.update()
                },
            )
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

    /** A strip of the surface's background as the settings now describe it. */
    class Preview(
        context: Context,
        private val settings: Settings,
    ) : View(context) {
        /** Android lint requires a (Context) constructor on every custom View; nothing inflates this one. */
        constructor(context: Context) : this(context, Settings(context))

        private val density = resources.displayMetrics.density
        private val palette = Palette.of(context)
        private var backdrop = Backdrop(settings, density, palette.background)
        private val border =
            Paint(Paint.ANTI_ALIAS_FLAG).apply {
                style = Paint.Style.STROKE
                strokeWidth = Space.HAIR * density
                color = palette.line
            }

        init {
            contentDescription = context.getString(R.string.background_preview_description)
        }

        /** Re-reads the settings; the backdrop reads them once, when it is made. */
        fun update() {
            backdrop = Backdrop(settings, density, palette.background)
            // Before the first layout there is no size, and an image cannot be cropped to nothing.
            if (width > 0 && height > 0) backdrop.resize(width, height)
            invalidate()
        }

        override fun onSizeChanged(
            w: Int,
            h: Int,
            oldw: Int,
            oldh: Int,
        ) {
            super.onSizeChanged(w, h, oldw, oldh)
            backdrop.resize(w, h)
        }

        override fun onDraw(canvas: Canvas) {
            super.onDraw(canvas)
            backdrop.draw(canvas)
            val inset = border.strokeWidth / 2
            canvas.drawRect(inset, inset, width - inset, height - inset, border)
        }
    }
}
