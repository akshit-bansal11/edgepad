package me.akshitbansal.edgepad.screens

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.Shader
import android.graphics.Typeface
import android.graphics.drawable.BitmapDrawable
import android.graphics.drawable.Drawable
import android.graphics.drawable.GradientDrawable
import android.graphics.drawable.InsetDrawable
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
private const val RULER_NOTCH_DP = 11f
private const val RULER_HEIGHT_DP = 22f
private const val RULER_MINOR_FROM = 0.45f
private const val RULER_MAJOR_EVERY = 5
private const val RULER_MAJOR_ALPHA = 230
private const val RULER_MINOR_ALPHA = 128
private const val RULER_THUMB_WIDTH_DP = 2.5f
private const val RULER_THUMB_HEIGHT_DP = 26f

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
    ): TextView = text(value, sp, color, Type.label, tracking)

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
            setPadding(dp(Space.XL), 0, dp(Space.XL), 0)
            val border = GradientDrawable().apply { setStroke(dp(Space.HAIR), palette.dim) }
            background = InsetDrawable(border, 0, dp(Space.XS), 0, dp(Space.XS))
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

    /** Options side by side in one bordered strip; the selected one is filled. */
    fun segmented(
        options: List<CharSequence>,
        selected: Int,
        onSelect: (Int) -> Unit,
    ): LinearLayout =
        LinearLayout(context).apply {
            background = GradientDrawable().apply { setStroke(dp(Space.HAIR), palette.dim) }
            options.forEachIndexed { i, label ->
                val on = i == selected
                val option =
                    mono(label, Type.MICRO, if (on) palette.background else palette.dim).apply {
                        gravity = Gravity.CENTER
                        minHeight = dp(Space.TOUCH)
                        setPadding(dp(Space.L), 0, dp(Space.L), 0)
                        if (on) setBackgroundColor(palette.ink)
                        isSelected = on
                        tappable(this) { onSelect(i) }
                    }
                addView(option)
            }
        }

    /** A slider drawn as the dials' own ruler, with a line for its thumb. */
    fun ruler(
        max: Int,
        progress: Int,
        onChange: (Int) -> Unit,
    ): SeekBar =
        SeekBar(context).apply {
            this.max = max
            this.progress = progress
            progressDrawable = rulerTile()
            thumb =
                GradientDrawable().apply {
                    setColor(palette.ink)
                    setSize(dp(RULER_THUMB_WIDTH_DP), dp(RULER_THUMB_HEIGHT_DP))
                }
            progressTintList = null
            progressBackgroundTintList = null
            thumbTintList = null
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

    private fun rulerTile(): Drawable {
        val notch = dp(RULER_NOTCH_DP)
        val height = dp(RULER_HEIGHT_DP)
        val bitmap = Bitmap.createBitmap(notch * RULER_MAJOR_EVERY, height, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(bitmap)
        val paint =
            Paint(Paint.ANTI_ALIAS_FLAG).apply {
                color = palette.ink
                strokeWidth = dp(Space.HAIR).toFloat()
            }
        for (i in 0 until RULER_MAJOR_EVERY) {
            val major = i == 0
            paint.color = palette.ink
            paint.alpha = if (major) RULER_MAJOR_ALPHA else RULER_MINOR_ALPHA
            val x = i * notch + paint.strokeWidth / 2
            canvas.drawLine(x, if (major) 0f else height * RULER_MINOR_FROM, x, height.toFloat(), paint)
        }
        return BitmapDrawable(context.resources, bitmap).apply { tileModeX = Shader.TileMode.REPEAT }
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
