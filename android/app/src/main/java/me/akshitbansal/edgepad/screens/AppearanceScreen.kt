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
    private const val ANGLE_STEP = 15f
    private const val SIZE_STEP = 4f
    private const val OPACITY_STEPS = 20
    private const val PREVIEW_DP = 110f
    private val angleSteps = (Settings.MAX_ANGLE / ANGLE_STEP).roundToInt()
    private val sizeSteps = ((Settings.MAX_PATTERN_SIZE - Settings.MIN_PATTERN_SIZE) / SIZE_STEP).roundToInt()

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
                            angleSteps,
                            (settings.gradientAngle / ANGLE_STEP).roundToInt(),
                            { step -> ui.string(R.string.degrees_value, (step * ANGLE_STEP).roundToInt()) },
                        ) { step ->
                            settings.gradientAngle = step * ANGLE_STEP
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
                    sizeSteps,
                    ((settings.patternSize - Settings.MIN_PATTERN_SIZE) / SIZE_STEP).roundToInt(),
                    { step ->
                        ui.string(
                            R.string.dial_length_value,
                            (Settings.MIN_PATTERN_SIZE + step * SIZE_STEP).roundToInt(),
                        )
                    },
                ) { step ->
                    settings.patternSize = Settings.MIN_PATTERN_SIZE + step * SIZE_STEP
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
