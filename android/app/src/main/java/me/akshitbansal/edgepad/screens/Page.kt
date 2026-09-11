package me.akshitbansal.edgepad.screens

import android.content.Context
import android.graphics.Typeface
import android.graphics.drawable.ClipDrawable
import android.graphics.drawable.Drawable
import android.graphics.drawable.GradientDrawable
import android.graphics.drawable.InsetDrawable
import android.graphics.drawable.LayerDrawable
import android.graphics.drawable.StateListDrawable
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
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type

private const val LEADING = 1.5f
private const val SECTION_TRACKING = 0.22f
private const val SUB_TRACKING = 0.12f
private const val PRESSED_ALPHA = 31
private const val FOCUS_RING_DP = 2f
private const val TOGGLE_WIDTH_DP = 40f
private const val TOGGLE_HEIGHT_DP = 23f
private const val KNOB_DP = 15f
private const val TRACK_DP = 2f
private const val TICK_DP = 8f
private const val THUMB_DP = 16f
private const val SEGMENT_INSET_DP = 3f

/**
 * The screens' shared look, after the owner's design: ink on a panel, hairline rows, square buttons,
 * small spaced monospace labels. Builders only; each screen assembles its own page from them.
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

    /** A page that scrolls when it must and otherwise fills the screen, so [Column.grow] can push content down. */
    fun page(
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
        return ScrollView(context).apply {
            isFillViewport = true
            isVerticalScrollBarEnabled = false
            setPadding(side, end, side, end)
            clipToPadding = false
            addView(
                column,
                ViewGroup.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT),
            )
            // Edge-to-edge is enforced from targetSdk 35: keep content clear of the system bars and the cutout.
            setOnApplyWindowInsetsListener { page, insets ->
                val bars = insets.getInsets(WindowInsets.Type.systemBars() or WindowInsets.Type.displayCutout())
                page.setPadding(side + bars.left, end + bars.top, side + bars.right, end + bars.bottom)
                insets
            }
        }
    }

    fun text(
        value: CharSequence,
        sp: Float,
        color: Int,
        face: Typeface,
        tracking: Float = 0f,
    ): TextView =
        TextView(context).apply {
            text = value
            setTextSize(TypedValue.COMPLEX_UNIT_SP, sp)
            setTextColor(color)
            typeface = face
            letterSpacing = tracking
        }

    fun mono(
        value: CharSequence,
        sp: Float = Type.MICRO,
        color: Int = palette.dim,
        tracking: Float = Type.TRACKING_WIDE,
    ): TextView = text(value, sp, color, Type.sans, tracking)

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
                        Style.OUTLINED -> setStroke(dp(Space.HAIR), palette.ink)
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
        mono(label, Type.MICRO, palette.dim).apply {
            gravity = Gravity.CENTER
            minHeight = dp(Space.TOUCH)
            val border = GradientDrawable().apply { setStroke(dp(Space.HAIR), palette.dim) }
            background = InsetDrawable(border, 0, dp(Space.XS), 0, dp(Space.XS))
            // After the background: a drawable with insets resets the view's padding to those insets.
            setPadding(dp(Space.XL), 0, dp(Space.XL), 0)
            tappable(this, onClick)
        }

    /** A centred monospace text link, for a way into another screen. */
    fun link(
        label: CharSequence,
        onClick: () -> Unit,
    ): TextView =
        mono(label, Type.MICRO, palette.dim).apply {
            gravity = Gravity.CENTER
            minHeight = dp(Space.TOUCH)
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
                    GradientDrawable().apply {
                        setColor(palette.ink)
                        alpha = PRESSED_ALPHA
                    },
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
            typeface = Type.plain
            isChecked = checked
            thumbDrawable = thumb()
            trackDrawable = track()
            thumbTintList = null
            trackTintList = null
            minHeight = dp(Space.ROW)
            setOnCheckedChangeListener { _, on -> onChange(on) }
        }

    /** Options side by side in one rounded strip; the selected one is a filled pill. */
    fun segmented(
        options: List<CharSequence>,
        selected: Int,
        onSelect: (Int) -> Unit,
    ): LinearLayout =
        LinearLayout(context).apply {
            val radius = dp(Space.TOUCH) / 2f
            background =
                GradientDrawable().apply {
                    setStroke(dp(Space.HAIR), palette.dim)
                    cornerRadius = radius
                }
            val inset = dp(SEGMENT_INSET_DP)
            setPadding(inset, inset, inset, inset)
            options.forEachIndexed { i, label ->
                val on = i == selected
                val option =
                    mono(label, Type.MICRO, if (on) palette.background else palette.dim).apply {
                        gravity = Gravity.CENTER
                        minHeight = dp(Space.TOUCH) - 2 * inset
                        background =
                            GradientDrawable().apply {
                                cornerRadius = radius
                                setColor(if (on) palette.ink else 0)
                            }
                        setPadding(dp(Space.L), 0, dp(Space.L), 0)
                        isSelected = on
                        tappable(this) { onSelect(i) }
                    }
                addView(option)
            }
        }

    /** A plain slider: a hairline track, a small line at each step, and a round thumb. */
    fun ruler(
        max: Int,
        progress: Int,
        onChange: (Int) -> Unit,
    ): SeekBar =
        SeekBar(context).apply {
            this.max = max
            this.progress = progress
            progressDrawable = sliderTrack()
            tickMark =
                GradientDrawable().apply {
                    setColor(palette.dim)
                    setSize(dp(Space.HAIR), dp(TICK_DP))
                }
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
        titleSp: Float = Type.BODY,
    ): LinearLayout =
        LinearLayout(context).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(0, dp(Space.M), 0, dp(Space.M))
            addView(text(title, titleSp, palette.ink, Type.plain))
            if (!sub.isNullOrEmpty()) {
                addView(mono(sub, Type.SMALL, palette.dim, SUB_TRACKING).apply { setPadding(0, dp(Space.XS), 0, 0) })
            }
        }

    fun hairline(): View = View(context).apply { setBackgroundColor(palette.line) }

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
    ): TextView = add(ui.text(value, sp, ui.palette.ink, Type.sans, Type.TRACKING_TIGHT), topDp)

    fun body(
        value: CharSequence,
        topDp: Float = 0f,
    ): TextView =
        add(ui.text(value, Type.BODY, ui.palette.dim, Type.plain).apply { setLineSpacing(0f, LEADING) }, topDp)

    fun section(value: CharSequence) {
        add(
            ui.mono(value, Type.MICRO, ui.palette.dim, SECTION_TRACKING).apply {
                setPadding(0, ui.dp(Space.XXL), 0, ui.dp(Space.M))
            },
        )
    }

    fun hairline(topDp: Float = 0f) {
        add(ui.hairline(), topDp, ui.dp(Space.HAIR))
    }

    /** Takes whatever height is left, so what follows sits at the bottom of a short page. */
    fun grow() {
        layout.addView(View(ui.context), LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f))
    }
}
