package me.akshitbansal.edgepad.screens

import android.content.Context
import android.graphics.Canvas
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.HorizontalScrollView
import android.widget.LinearLayout
import android.widget.TextView
import me.akshitbansal.edgepad.Palette
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.surface.Backdrop
import kotlin.math.roundToInt

/**
 * What the control surface is drawn in: one colour for every control, what sits behind them (the theme,
 * a colour, a gradient or an image), and a pattern over that, with a preview that follows every change.
 * The background and the pattern are each a segmented control, the same one Settings uses.
 *
 * A choice that changes which rows a block shows refills the rows under its control and leaves the control
 * itself alone, so its thumb slides to the new choice rather than being drawn again already there.
 */
object AppearanceScreen {
    private const val PERCENT = 100
    private const val OPACITY_STEPS = 20
    private const val PREVIEW_DP = 160f
    private const val MATCH = ViewGroup.LayoutParams.MATCH_PARENT
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

    /** The surface settings row's one-line summary, read as a phrase: the background, then the pattern. */
    fun summary(
        ui: Ui,
        settings: Settings,
    ): String =
        ui.string(
            R.string.appearance_summary,
            ui.string(backgroundNames.getValue(settings.background)),
            ui.string(patternNames.getValue(settings.pattern)),
        )

    fun build(
        ui: Ui,
        settings: Settings,
        onPickImage: () -> Unit,
        onBack: () -> Unit,
    ): View {
        val preview = Preview(ui.context, settings)
        val bar =
            ui.bar(ui.string(R.string.appearance_title), onBack, backLabel = ui.string(R.string.settings_surface_title))
        val sections: Column.() -> Unit = {
            add(panel(ui, ui.string(R.string.settings_controls), controlColor(ui, settings)), Space.M)
            add(panel(ui, ui.string(R.string.background), background(ui, settings, preview, onPickImage)), Space.L)
            add(panel(ui, ui.string(R.string.pattern), pattern(ui, settings, preview)), Space.L)
        }
        if (!ui.landscape) {
            // Upright: the preview first, so every change below it can be seen without scrolling back up.
            return ui.page(bar) {
                add(previewPanel(ui, preview, false), Space.M)
                sections()
            }
        }
        // Sideways: the settings scroll on the left and the preview holds still, large, on the right.
        val right =
            FrameLayout(ui.context).apply {
                ui.padToBars(this)
                addView(
                    previewPanel(ui, preview, true),
                    FrameLayout.LayoutParams(MATCH, MATCH).apply {
                        topMargin = ui.dp(Space.M)
                        bottomMargin = ui.dp(Space.L)
                        marginEnd = ui.dp(Space.PAGE)
                    },
                )
            }
        return LinearLayout(ui.context).apply {
            setBackgroundColor(ui.palette.background)
            addView(ui.page(bar, fill = sections), LinearLayout.LayoutParams(0, MATCH, 1f))
            addView(right, LinearLayout.LayoutParams(0, MATCH, 1f))
        }
    }

    /** One section in one rounded container: its name over its controls, spaced by the card's own padding. */
    private fun panel(
        ui: Ui,
        title: String,
        content: View,
    ): View =
        LinearLayout(ui.context).apply {
            orientation = LinearLayout.VERTICAL
            background = ui.rounded(ui.palette.card, Space.CARD_RADIUS)
            setPadding(ui.dp(Space.CARD_PAD), 0, ui.dp(Space.CARD_PAD), 0)
            addView(header(ui, title))
            addView(content)
        }

    private fun header(
        ui: Ui,
        title: String,
    ): TextView =
        ui.text(title, Type.SMALL, ui.palette.dim, face = Type.bold).apply {
            isAccessibilityHeading = true
            setPadding(0, ui.dp(Space.M), 0, ui.dp(Space.XS))
        }

    /**
     * The preview in a container of its own, its picture rounded a step tighter than the card round it. [tall]
     * fills the height it is given, which is how the sideways page makes it large; upright it is a strip.
     */
    private fun previewPanel(
        ui: Ui,
        preview: Preview,
        tall: Boolean,
    ): View =
        LinearLayout(ui.context).apply {
            orientation = LinearLayout.VERTICAL
            background = ui.rounded(ui.palette.card, Space.CARD_RADIUS)
            setPadding(ui.dp(Space.CARD_PAD), 0, ui.dp(Space.CARD_PAD), ui.dp(Space.CARD_PAD))
            preview.background = ui.rounded(ui.palette.background, Space.TILE_RADIUS)
            preview.clipToOutline = true
            addView(header(ui, ui.string(R.string.appearance_preview)))
            val at =
                if (tall) {
                    LinearLayout.LayoutParams(MATCH, 0, 1f)
                } else {
                    LinearLayout.LayoutParams(MATCH, ui.dp(PREVIEW_DP))
                }
            addView(preview, at)
        }

    /** Auto follows the background; custom shows a picker whose colour every control shares. */
    private fun controlColor(
        ui: Ui,
        settings: Settings,
    ): View =
        LinearLayout(ui.context).apply {
            orientation = LinearLayout.VERTICAL
            val details = block(ui)
            val fill = {
                details.removeAllViews()
                val chosen = settings.controlColor
                if (chosen != null) {
                    details.addView(line(ui))
                    details.addView(
                        ColorPicker.build(ui, ui.string(R.string.control_color), chosen, editable = true) {
                            settings.controlColor = it
                        },
                    )
                }
            }
            val modes = listOf(ui.string(R.string.control_color_auto), ui.string(R.string.control_color_custom))
            addView(
                pills(ui, modes, if (settings.controlColor == null) 0 else 1, full = true) { i ->
                    settings.controlColor = if (i == 0) null else ui.palette.ink
                    fill()
                },
            )
            addView(details)
            fill()
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
            val details = block(ui)
            addView(
                pills(ui, names, kinds.indexOf(settings.background), full = true) { i ->
                    settings.background = kinds[i]
                    preview.update()
                    backgroundRows(ui, settings, preview, onPickImage, details)
                },
            )
            addView(details)
            backgroundRows(ui, settings, preview, onPickImage, details)
        }

    /** The rows the chosen kind of background needs, into [into]: none, a colour, a gradient's three, a picture. */
    private fun backgroundRows(
        ui: Ui,
        settings: Settings,
        preview: Preview,
        onPickImage: () -> Unit,
        into: LinearLayout,
    ) {
        into.removeAllViews()
        into.apply {
            val colour = {
                addView(line(ui))
                addView(
                    ColorPicker.build(ui, ui.string(R.string.background_color), settings.backgroundColor, true) {
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
                        ColorPicker.build(ui, ui.string(R.string.gradient_end), settings.gradientEnd, true) {
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
            val details = block(ui)
            addView(
                pills(ui, names, patterns.indexOf(settings.pattern), full = true) { i ->
                    settings.pattern = patterns[i]
                    preview.update()
                    patternRows(ui, settings, preview, details)
                },
            )
            addView(details)
            patternRows(ui, settings, preview, details)
        }

    /** While there is a pattern, its size, strength and colour, into [into]. */
    private fun patternRows(
        ui: Ui,
        settings: Settings,
        preview: Preview,
        into: LinearLayout,
    ) {
        into.removeAllViews()
        if (settings.pattern == Settings.Pattern.NONE) return
        into.apply {
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
                ColorPicker.build(ui, ui.string(R.string.pattern_color), settings.patternColor, true) {
                    settings.patternColor = it
                    preview.update()
                },
            )
        }
    }

    /**
     * One segment per option, the chosen one on the raised thumb, scrolling sideways if they outgrow the card.
     * Until 3.2 this was a row of chips of its own, a second control for the job Settings' segmented control
     * already did; the segmented control moves its own thumb, so [onPick] only has the rows under it to redo.
     * [full] spans the whole card instead, its options sharing the width, and then has no need to scroll.
     */
    private fun pills(
        ui: Ui,
        names: List<CharSequence>,
        chosen: Int,
        full: Boolean = false,
        onPick: (Int) -> Unit,
    ): View {
        if (full) {
            return FrameLayout(ui.context).apply {
                setPadding(0, ui.dp(Space.S), 0, ui.dp(Space.S))
                val at = FrameLayout.LayoutParams(MATCH, ViewGroup.LayoutParams.WRAP_CONTENT)
                addView(ui.segmentedFull(names, chosen, onPick), at)
            }
        }
        return HorizontalScrollView(ui.context).apply {
            isHorizontalScrollBarEnabled = false
            setPadding(0, ui.dp(Space.S), 0, ui.dp(Space.S))
            addView(ui.segmented(names, chosen, onPick))
        }
    }

    /** An empty run of rows, filled and refilled by the block it sits in. */
    private fun block(ui: Ui): LinearLayout = LinearLayout(ui.context).apply { orientation = LinearLayout.VERTICAL }

    /** A separator between two rows of a block, which sits in a card. */
    private fun line(ui: Ui): View =
        ui.hairline().apply {
            layoutParams = LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, ui.dp(Space.HAIR))
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
        private var backdrop = Backdrop(settings, density, palette.background).also { it.load(this) }

        init {
            contentDescription = context.getString(R.string.background_preview_description)
        }

        /** Re-reads the settings; the backdrop reads them once, when it is made. */
        fun update() {
            backdrop = Backdrop(settings, density, palette.background).also { it.load(this) }
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
