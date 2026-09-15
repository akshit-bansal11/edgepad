package me.akshitbansal.edgepad.screens

import android.content.Context
import android.content.res.Configuration
import android.graphics.drawable.ClipDrawable
import android.graphics.drawable.Drawable
import android.graphics.drawable.GradientDrawable
import android.graphics.drawable.InsetDrawable
import android.graphics.drawable.LayerDrawable
import android.graphics.drawable.StateListDrawable
import android.text.TextUtils
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.view.WindowInsets
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.SeekBar
import android.widget.Switch
import android.widget.TextView
import me.akshitbansal.edgepad.Palette
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type

private const val LEADING = 1.45f
private const val SECTION_TRACKING = 0.14f
private const val SUB_TRACKING = 0.1f
private const val TITLE_TRACKING = 0.02f
private const val SECTION_TOP_DP = 22f
private const val SUB_GAP_DP = 3f
private const val FOCUS_RING_DP = 2f
private const val TOGGLE_WIDTH_DP = 40f
private const val TOGGLE_HEIGHT_DP = 22f
private const val KNOB_DP = 16f
private const val TRACK_DP = 2f
private const val TICK_DP = 2f
private const val TICK_BELOW_DP = 16f
private const val THUMB_DP = 16f
private const val SEGMENT_HEIGHT_DP = 28f
private const val SEGMENT_PAD_DP = 10f
private const val CHIP_DP = 32f
private const val ICON_TOUCH_DP = 40f
private const val BAR_START_DP = 20f
private const val CHOSEN_DOT_DP = 8f

/**
 * The screens' shared look, after the owner's 2026-09-15 redesign: ink on a panel, JetBrains Mono
 * throughout, a 52 dp title row, hairline rows, square buttons and segments, small spaced capitals.
 * Builders only; each screen assembles its own page from them.
 */
class Ui(
    val context: Context,
) {
    enum class Style {
        /** The one main action on a page. */
        FILLED,

        /** A strong secondary action. */
        OUTLINED,

        /** A way out or aside. */
        QUIET,
    }

    val palette = Palette.of(context)
    private val density = context.resources.displayMetrics.density

    fun dp(value: Float): Int = (value * density).toInt().coerceAtLeast(1)

    fun string(
        resId: Int,
        vararg args: Any,
    ): String = context.getString(resId, *args)

    /**
     * A page that scrolls when it must and otherwise fills the screen, so [Column.grow] can push content
     * down. A [bar] stays put above the scrolling part.
     */
    fun page(
        bar: View? = null,
        centred: Boolean = false,
        fill: Column.() -> Unit,
    ): View {
        val column =
            LinearLayout(context).apply {
                orientation = LinearLayout.VERTICAL
                if (centred) gravity = Gravity.CENTER_HORIZONTAL
            }
        Column(this, column).fill()
        val side = dp(Space.PAGE)
        val end = dp(Space.XL)
        val top = if (bar == null) end else 0
        val scroll =
            ScrollView(context).apply {
                isFillViewport = true
                isVerticalScrollBarEnabled = false
                setPadding(side, top, side, end)
                clipToPadding = false
                addView(
                    column,
                    ViewGroup.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT),
                )
            }
        val root =
            LinearLayout(context).apply {
                orientation = LinearLayout.VERTICAL
                setBackgroundColor(palette.background)
                if (bar != null) addView(bar)
                addView(scroll, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f))
            }
        // Edge-to-edge is enforced from targetSdk 35: keep content clear of the system bars and the cutout.
        root.setOnApplyWindowInsetsListener { page, insets ->
            val bars = insets.getInsets(WindowInsets.Type.systemBars() or WindowInsets.Type.displayCutout())
            page.setPadding(bars.left, bars.top, bars.right, 0)
            scroll.setPadding(side, top, side, end + bars.bottom)
            insets
        }
        return root
    }

    /** A screen's title row: back when there is somewhere to go back to, the name, then [actions] at the far end. */
    fun bar(
        title: CharSequence,
        onBack: (() -> Unit)?,
        vararg actions: View,
    ): LinearLayout =
        LinearLayout(context).apply {
            gravity = Gravity.CENTER_VERTICAL
            minimumHeight = dp(Space.BAR)
            setPadding(if (onBack == null) dp(BAR_START_DP) else dp(Space.S), 0, dp(Space.S), 0)
            if (onBack != null) addView(icon(Glyph.Shape.CHEVRON_LEFT, string(R.string.back), onBack))
            val name =
                text(title, Type.HEADING, palette.ink, TITLE_TRACKING).apply {
                    isAccessibilityHeading = true
                    setPadding(dp(Space.XS), 0, 0, 0)
                }
            addView(name, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
            actions.forEach { addView(it) }
        }

    /** An icon-only button with a 40 dp target, named for screen readers and long-press alike. */
    fun icon(
        shape: Glyph.Shape,
        label: CharSequence,
        onTap: () -> Unit,
    ): Glyph =
        Glyph(context, shape, palette.ink).apply {
            contentDescription = label
            tooltipText = label
            layoutParams = LinearLayout.LayoutParams(dp(ICON_TOUCH_DP), dp(ICON_TOUCH_DP))
            tappable(this, onTap)
        }

    fun text(
        value: CharSequence,
        sp: Float,
        color: Int,
        tracking: Float = 0f,
    ): TextView =
        TextView(context).apply {
            text = value
            setTextSize(TypedValue.COMPLEX_UNIT_SP, sp)
            setTextColor(color)
            typeface = Type.face
            letterSpacing = tracking
        }

    fun mono(
        value: CharSequence,
        sp: Float = Type.MICRO,
        color: Int = palette.dim,
        tracking: Float = Type.TRACKING_WIDE,
    ): TextView = text(value, sp, color, tracking)

    /** A full-width square button. */
    fun button(
        label: CharSequence,
        style: Style,
        onClick: () -> Unit,
    ): TextView {
        val labelColor =
            when (style) {
                Style.FILLED -> palette.background
                Style.OUTLINED -> palette.ink
                Style.QUIET -> palette.dim
            }
        return mono(label, Type.LABEL, labelColor, Type.TRACKING_BUTTON).apply {
            gravity = Gravity.CENTER
            minHeight = dp(Space.BUTTON)
            background =
                GradientDrawable().apply {
                    when (style) {
                        Style.FILLED -> setColor(palette.ink)
                        Style.OUTLINED -> setStroke(dp(Space.HAIR), palette.dim)
                        Style.QUIET -> setStroke(dp(Space.HAIR), palette.line)
                    }
                }
            tappable(this, onClick)
        }
    }

    /** A small outlined action beside a row, with a full-size touch target around it. */
    fun chip(
        label: CharSequence,
        onClick: () -> Unit,
    ): TextView =
        mono(label, Type.MICRO, palette.ink, Type.TRACKING_BUTTON).apply {
            gravity = Gravity.CENTER
            minHeight = dp(Space.TOUCH)
            val border = GradientDrawable().apply { setStroke(dp(Space.HAIR), palette.dim) }
            val inset = (dp(Space.TOUCH) - dp(CHIP_DP)) / 2
            background = InsetDrawable(border, 0, inset, 0, inset)
            // After the background: a drawable with insets resets the view's padding to those insets.
            setPadding(dp(Space.M), 0, dp(Space.M), 0)
            tappable(this, onClick)
        }

    /** Pressed and focused states for a view drawn without the platform's own button background. */
    fun tappable(
        view: View,
        onClick: () -> Unit,
    ) {
        view.isClickable = true
        view.isFocusable = true
        view.foreground =
            StateListDrawable().apply {
                addState(
                    intArrayOf(android.R.attr.state_pressed),
                    GradientDrawable().apply { setColor(palette.faint) },
                )
                addState(
                    intArrayOf(android.R.attr.state_focused),
                    GradientDrawable().apply { setStroke(dp(FOCUS_RING_DP), palette.ink) },
                )
            }
        view.setOnClickListener { onClick() }
    }

    /** A labelled on/off row: the platform Switch, so accessibility and focus come with it, drawn as the design's pill. */
    fun toggle(
        label: CharSequence,
        checked: Boolean,
        onChange: (Boolean) -> Unit,
    ): Switch =
        Switch(context).apply {
            text = label
            setTextSize(TypedValue.COMPLEX_UNIT_SP, Type.BODY)
            setTextColor(palette.ink)
            typeface = Type.face
            isChecked = checked
            thumbDrawable = thumb()
            trackDrawable = track()
            thumbTintList = null
            trackTintList = null
            minHeight = dp(Space.ROW)
            setOnCheckedChangeListener { _, on -> onChange(on) }
        }

    /** Options side by side in one square outlined strip; the selected one is filled with ink. */
    fun segmented(
        options: List<CharSequence>,
        selected: Int,
        onSelect: (Int) -> Unit,
    ): LinearLayout =
        LinearLayout(context).apply {
            background = GradientDrawable().apply { setStroke(dp(Space.HAIR), palette.dim) }
            val hair = dp(Space.HAIR)
            setPadding(hair, hair, hair, hair)
            options.forEachIndexed { i, label ->
                val on = i == selected
                val option =
                    mono(label, Type.MICRO, if (on) palette.background else palette.dim).apply {
                        gravity = Gravity.CENTER
                        minHeight = dp(SEGMENT_HEIGHT_DP) - 2 * hair
                        if (on) setBackgroundColor(palette.ink)
                        setPadding(dp(SEGMENT_PAD_DP), 0, dp(SEGMENT_PAD_DP), 0)
                        isSelected = on
                        tappable(this) { onSelect(i) }
                    }
                addView(option)
            }
        }

    /** A plain slider: a hairline track, a small dot under each step, and a round thumb. */
    fun ruler(
        max: Int,
        progress: Int,
        onChange: (Int) -> Unit,
    ): SeekBar =
        SeekBar(context).apply {
            this.max = max
            this.progress = progress
            progressDrawable = sliderTrack()
            // The dot sits under the track: the inset above it shifts it down by half the inset.
            tickMark =
                InsetDrawable(
                    GradientDrawable().apply {
                        shape = GradientDrawable.OVAL
                        setColor(palette.dim)
                        setSize(dp(TICK_DP), dp(TICK_DP))
                    },
                    0,
                    dp(TICK_BELOW_DP),
                    0,
                    0,
                )
            thumb =
                GradientDrawable().apply {
                    shape = GradientDrawable.OVAL
                    setColor(palette.ink)
                    setSize(dp(THUMB_DP), dp(THUMB_DP))
                }
            progressTintList = null
            progressBackgroundTintList = null
            thumbTintList = null
            tickMarkTintList = null
            splitTrack = false
            minimumHeight = dp(Space.TOUCH)
            setPadding(paddingLeft, paddingTop, paddingRight, dp(Space.M))
            setOnSeekBarChangeListener(
                object : SeekBar.OnSeekBarChangeListener {
                    override fun onProgressChanged(
                        seekBar: SeekBar?,
                        value: Int,
                        fromUser: Boolean,
                    ) {
                        if (fromUser) onChange(value)
                    }

                    override fun onStartTrackingTouch(seekBar: SeekBar?) = Unit

                    override fun onStopTrackingTouch(seekBar: SeekBar?) = Unit
                },
            )
        }

    /** A row: [start] takes the room, [end] sits at the far side. */
    fun row(
        start: View,
        end: View? = null,
    ): LinearLayout =
        LinearLayout(context).apply {
            gravity = Gravity.CENTER_VERTICAL
            minimumHeight = dp(Space.ROW)
            addView(start, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
            if (end != null) addView(end)
        }

    /** A name over a monospace sub-line. */
    fun stack(
        title: CharSequence,
        sub: CharSequence?,
        titleSp: Float = Type.LEAD,
    ): LinearLayout =
        LinearLayout(context).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(0, dp(Space.M), 0, dp(Space.M))
            addView(text(title, titleSp, palette.ink))
            if (!sub.isNullOrEmpty()) {
                addView(mono(sub, Type.MICRO, palette.dim, SUB_TRACKING).apply { setPadding(0, dp(SUB_GAP_DP), 0, 0) })
            }
        }

    fun hairline(): View = View(context).apply { setBackgroundColor(palette.line) }

    /** True when the screen is wider than tall; pages then split into two columns. */
    val landscape: Boolean
        get() = context.resources.configuration.orientation == Configuration.ORIENTATION_LANDSCAPE

    /** A small spaced label over a group of rows. */
    fun section(value: CharSequence): TextView =
        mono(value, Type.MICRO, palette.dim, SECTION_TRACKING).apply {
            isAccessibilityHeading = true
            setPadding(0, dp(SECTION_TOP_DP), 0, dp(Space.XS))
        }

    /** A row with a plain label and [end] at the far side. */
    fun field(
        label: CharSequence,
        end: View? = null,
    ): LinearLayout = row(text(label, Type.BODY, palette.ink), end)

    /** A row that opens another screen: its name, a short summary of what is set there, and a chevron. */
    fun link(
        label: CharSequence,
        summary: CharSequence?,
        onOpen: () -> Unit,
    ): LinearLayout {
        val end =
            LinearLayout(context).apply {
                gravity = Gravity.CENTER_VERTICAL
                if (!summary.isNullOrEmpty()) {
                    addView(
                        mono(summary, Type.MICRO, palette.dim, Type.TRACKING_ROW).apply {
                            maxLines = 1
                            ellipsize = TextUtils.TruncateAt.END
                        },
                    )
                }
                addView(
                    Glyph(context, Glyph.Shape.CHEVRON_RIGHT, palette.dim),
                    LinearLayout.LayoutParams(dp(Space.XL), dp(Space.XL)),
                )
            }
        return field(label, end).apply { tappable(this, onOpen) }
    }

    /** A labelled slider over [steps] steps, with what the step means written beside the label as it moves. */
    fun slider(
        label: CharSequence,
        steps: Int,
        progress: Int,
        valueOf: (Int) -> CharSequence,
        onChange: (Int) -> Unit,
    ): LinearLayout =
        LinearLayout(context).apply {
            orientation = LinearLayout.VERTICAL
            val value = mono(valueOf(progress), Type.SMALL, palette.dim, Type.TRACKING_ROW)
            addView(
                field(label, value).apply {
                    minimumHeight = 0
                    setPadding(0, dp(Space.M), 0, 0)
                },
            )
            val track =
                ruler(steps, progress) { step ->
                    value.text = valueOf(step)
                    onChange(step)
                }
            track.contentDescription = label
            addView(track)
        }

    /** One of [names], a row each under a hairline, the chosen one marked with a dot. [onPick] gets the index. */
    fun choices(
        names: List<CharSequence>,
        selected: Int,
        onPick: (Int) -> Unit,
    ): LinearLayout =
        LinearLayout(context).apply {
            orientation = LinearLayout.VERTICAL
            names.forEachIndexed { i, name ->
                val chosen = i == selected
                val row = field(name)
                row.minimumHeight = dp(Space.TOUCH)
                val dot =
                    View(context).apply {
                        background =
                            GradientDrawable().apply {
                                shape = GradientDrawable.OVAL
                                setColor(palette.ink)
                            }
                        visibility = if (chosen) View.VISIBLE else View.INVISIBLE
                        importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO
                    }
                row.addView(
                    dot,
                    LinearLayout.LayoutParams(dp(CHOSEN_DOT_DP), dp(CHOSEN_DOT_DP)).apply { marginEnd = dp(Space.S) },
                )
                row.isSelected = chosen
                if (chosen) row.stateDescription = string(R.string.chosen)
                tappable(row) { onPick(i) }
                addView(row)
                addView(hairline(), ViewGroup.LayoutParams.MATCH_PARENT, dp(Space.HAIR))
            }
        }

    private fun track(): Drawable =
        StateListDrawable().apply {
            addState(intArrayOf(android.R.attr.state_checked), pill(filled = true))
            addState(intArrayOf(), pill(filled = false))
        }

    private fun pill(filled: Boolean): Drawable =
        GradientDrawable().apply {
            cornerRadius = dp(TOGGLE_HEIGHT_DP) / 2f
            setSize(dp(TOGGLE_WIDTH_DP), dp(TOGGLE_HEIGHT_DP))
            if (filled) setColor(palette.ink) else setStroke(dp(Space.HAIR), palette.dim)
        }

    private fun thumb(): Drawable =
        StateListDrawable().apply {
            addState(intArrayOf(android.R.attr.state_checked), knob(palette.background))
            addState(intArrayOf(), knob(palette.dim))
        }

    // Half the track wide, so the platform Switch's travel is exactly the track's free width.
    private fun knob(color: Int): Drawable {
        val size = dp(KNOB_DP)
        val side = (dp(TOGGLE_WIDTH_DP) / 2 - size) / 2
        val top = (dp(TOGGLE_HEIGHT_DP) - size) / 2
        val dot =
            GradientDrawable().apply {
                shape = GradientDrawable.OVAL
                setColor(color)
                setSize(size, size)
            }
        return InsetDrawable(dot, side, top, side, top)
    }

    /** The track: the line in the theme's line colour, with the part up to the thumb in ink. */
    private fun sliderTrack(): Drawable {
        fun bar(color: Int) =
            GradientDrawable().apply {
                setColor(color)
                setSize(0, dp(TRACK_DP))
            }
        val layers =
            LayerDrawable(
                arrayOf(bar(palette.line), ClipDrawable(bar(palette.ink), Gravity.START, ClipDrawable.HORIZONTAL)),
            )
        layers.setId(0, android.R.id.background)
        layers.setId(1, android.R.id.progress)
        for (i in 0 until 2) {
            layers.setLayerGravity(i, Gravity.CENTER_VERTICAL)
            layers.setLayerHeight(i, dp(TRACK_DP))
        }
        return layers
    }
}

/** A vertical run of views on a page, with the design's spacing built in. */
class Column(
    val ui: Ui,
    private val layout: LinearLayout,
) {
    fun <T : View> add(
        view: T,
        topDp: Float = 0f,
        height: Int = ViewGroup.LayoutParams.WRAP_CONTENT,
        width: Int = ViewGroup.LayoutParams.MATCH_PARENT,
    ): T {
        layout.addView(
            view,
            LinearLayout.LayoutParams(width, height).apply {
                topMargin =
                    if (topDp > 0f) ui.dp(topDp) else 0
            },
        )
        return view
    }

    fun mono(
        value: CharSequence,
        sp: Float = Type.MICRO,
        color: Int = ui.palette.dim,
        topDp: Float = 0f,
    ): TextView = add(ui.mono(value, sp, color), topDp)

    fun headline(
        value: CharSequence,
        sp: Float,
        topDp: Float = 0f,
    ): TextView = add(ui.text(value, sp, ui.palette.ink, Type.TRACKING_TIGHT), topDp)

    fun body(
        value: CharSequence,
        topDp: Float = 0f,
    ): TextView = add(ui.text(value, Type.BODY, ui.palette.dim).apply { setLineSpacing(0f, LEADING) }, topDp)

    fun section(value: CharSequence) {
        add(ui.section(value))
    }

    /** Two runs of rows: side by side when the screen is sideways, one after the other when it is upright. */
    fun columns(
        first: Column.() -> Unit,
        second: Column.() -> Unit,
    ) {
        if (!ui.landscape) {
            first()
            second()
            return
        }
        val pair = LinearLayout(ui.context)
        listOf(first, second).forEachIndexed { i, fill ->
            val run = LinearLayout(ui.context).apply { orientation = LinearLayout.VERTICAL }
            Column(ui, run).fill()
            pair.addView(
                run,
                LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply {
                    if (i == 1) marginStart = ui.dp(Space.XXL)
                },
            )
        }
        add(pair)
    }

    fun hairline(topDp: Float = 0f) {
        add(ui.hairline(), topDp, ui.dp(Space.HAIR))
    }

    /** Takes whatever height is left, so what follows sits at the bottom of a short page. */
    fun grow() {
        layout.addView(View(ui.context), LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f))
    }
}
