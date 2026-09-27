package me.akshitbansal.edgepad.screens

import android.content.Context
import android.graphics.Canvas
import android.view.View
import android.widget.HorizontalScrollView
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
 * The background and the pattern are each a row of pills, the chosen one filled in the accent.
 */
object AppearanceScreen {
    private const val PERCENT = 100
    private const val OPACITY_STEPS = 20
    private const val PREVIEW_DP = 110f
    private const val PREVIEW_RADIUS_DP = 10f
    private const val PREVIEW_CARD_RADIUS_DP = 16f
    private val angleRange = StepRange(0f, Settings.MAX_ANGLE, 15f)
    private val sizeRange = StepRange(Settings.MIN_PATTERN_SIZE, Settings.MAX_PATTERN_SIZE, 4f)

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

    /** The Settings hub's one-line summary, read as a phrase: the background, then the pattern. */
    fun summary(
        ui: Ui,
        settings: Settings,
    ): String =
        ui.string(backgroundNames.getValue(settings.background)) + ", " +
            ui.string(patternNames.getValue(settings.pattern)).lowercase()

    fun build(
        ui: Ui,
        settings: Settings,
        onPickImage: () -> Unit,
        onBack: () -> Unit,
    ): View {
        val preview = Preview(ui.context, settings)
        val bar = ui.bar(ui.string(R.string.appearance_title), onBack, backLabel = ui.string(R.string.settings_title))
        return ui.page(bar) {
            columns(
                {
                    section(ui.string(R.string.settings_controls))
                    card { add(controlColor(ui, settings)) }
                    section(ui.string(R.string.background))
                    card { add(background(ui, settings, preview, onPickImage)) }
                },
                {
                    section(ui.string(R.string.pattern))
                    card { add(pattern(ui, settings, preview)) }
                    section(ui.string(R.string.appearance_preview))
                    add(previewCard(ui, preview))
                },
            )
        }
    }

    /** The preview strip inset on a card of its own, rounded a step tighter than the card round it. */
    private fun previewCard(
        ui: Ui,
        preview: Preview,
    ): View =
        LinearLayout(ui.context).apply {
            background = ui.rounded(ui.palette.card, PREVIEW_CARD_RADIUS_DP)
            val pad = ui.dp(Space.S)
            setPadding(pad, pad, pad, pad)
            preview.background = ui.rounded(ui.palette.background, PREVIEW_RADIUS_DP)
            preview.clipToOutline = true
            addView(preview, LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, ui.dp(PREVIEW_DP)))
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
                addView(line(ui))
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
            val names = kinds.map { ui.string(backgroundNames.getValue(it)) }
            addView(
                pills(ui, names, kinds.indexOf(settings.background)) { i ->
                    settings.background = kinds[i]
                    preview.update()
                    refresh(this) { background(ui, settings, preview, onPickImage) }
                },
            )
            val colour = {
                addView(line(ui))
                addView(
                    ColorPicker.build(ui, ui.string(R.string.background_color), settings.backgroundColor) {
                        settings.backgroundColor = it
                        preview.update()
                    },
                )
            }
            when (settings.background) {
                Settings.Background.THEME -> {
                    Unit
                }

                Settings.Background.COLOR -> {
                    colour()
                }

                Settings.Background.GRADIENT -> {
                    colour()
                    addView(line(ui))
                    addView(
                        ColorPicker.build(ui, ui.string(R.string.gradient_end), settings.gradientEnd) {
                            settings.gradientEnd = it
                            preview.update()
                        },
                    )
                    addView(line(ui))
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
                    val pick =
                        ui.chip(ui.string(R.string.background_pick_image), onPickImage).apply {
                            // The one action on this card, so it takes the tinted accent rather than a neutral fill.
                            background.setTint(ui.palette.accentSoft)
                            setTextColor(ui.palette.accent)
                        }
                    addView(line(ui))
                    addView(ui.field(ui.string(state), pick))
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
            val names = patterns.map { ui.string(patternNames.getValue(it)) }
            addView(
                pills(ui, names, patterns.indexOf(settings.pattern)) { i ->
                    settings.pattern = patterns[i]
                    preview.update()
                    refresh(this) { pattern(ui, settings, preview) }
                },
            )
            if (settings.pattern == Settings.Pattern.NONE) return@apply
            addView(line(ui))
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
            addView(line(ui))
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
            addView(line(ui))
            addView(
                ColorPicker.build(ui, ui.string(R.string.pattern_color), settings.patternColor) {
                    settings.patternColor = it
                    preview.update()
                },
            )
        }

    /**
     * One pill per option, side by side, scrolling sideways if they outgrow the card. The chosen one is filled
     * in the accent; picking another hands its index to [onPick], whose caller rebuilds the block to move it.
     */
    private fun pills(
        ui: Ui,
        names: List<CharSequence>,
        chosen: Int,
        onPick: (Int) -> Unit,
    ): View {
        val row = LinearLayout(ui.context)
        names.forEachIndexed { i, name ->
            val pill = ui.chip(name) { if (i != chosen) onPick(i) }
            if (i == chosen) {
                // Tinting the chip's own fill keeps its shape, inset and touch target; only the colour changes.
                pill.background.setTint(ui.palette.accent)
                pill.setTextColor(ui.palette.onAccent)
                pill.isSelected = true
                pill.stateDescription = ui.string(R.string.chosen)
            }
            val wrap = LinearLayout.LayoutParams.WRAP_CONTENT
            row.addView(pill, LinearLayout.LayoutParams(wrap, wrap).apply { if (i > 0) marginStart = ui.dp(Space.S) })
        }
        return HorizontalScrollView(ui.context).apply {
            isHorizontalScrollBarEnabled = false
            setPadding(0, ui.dp(Space.XS), 0, ui.dp(Space.XS))
            addView(row)
        }
    }

    /** A separator between two rows of a block, which sits in a card. */
    private fun line(ui: Ui): View =
        ui.hairline().apply {
            layoutParams = LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, ui.dp(Space.HAIR))
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

    /** A strip of the surface's background as the settings now describe it, clipped to its rounded outline. */
    class Preview(
        context: Context,
        private val settings: Settings,
    ) : View(context) {
        /** Android lint requires a (Context) constructor on every custom View; nothing inflates this one. */
        constructor(context: Context) : this(context, Settings(context))

        private val density = resources.displayMetrics.density
        private val palette = Palette.of(context)
        private var backdrop = Backdrop(settings, density, palette.background)

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
        }
    }
}
