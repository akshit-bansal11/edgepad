package me.akshitbansal.edgepad.screens

import android.animation.ValueAnimator
import android.app.AlertDialog
import android.content.Context
import android.content.res.Configuration
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.ClipDrawable
import android.graphics.drawable.Drawable
import android.graphics.drawable.GradientDrawable
import android.graphics.drawable.InsetDrawable
import android.graphics.drawable.LayerDrawable
import android.graphics.drawable.StateListDrawable
import android.text.TextPaint
import android.text.TextUtils
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.view.WindowInsets
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.SeekBar
import android.widget.Switch
import android.widget.TextView
import me.akshitbansal.edgepad.Palette
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type

private const val SECTION_TOP_DP = 24f
private const val FOCUS_RING_DP = 2f
private const val KNOB_DP = 26f
private const val KNOB_GAP_DP = 2f
private const val TRACK_DP = 4f
private const val TICK_DP = 3f
private const val TICK_BELOW_DP = 18f
private const val THUMB_DP = 28f

/** The segmented control's thumb at the default font size; a larger font grows it to fit its text. */
private const val SEGMENT_HEIGHT_DP = 28f

/** Above and below an option's text, inside the thumb, once the font has outgrown [SEGMENT_HEIGHT_DP]. */
private const val SEGMENT_TEXT_PAD_DP = 6f
private const val SEGMENT_INSET_DP = 2f
private const val SEGMENT_PAD_DP = 14f
private const val SEGMENT_TRACK_RADIUS_DP = 9f
private const val SEGMENT_THUMB_RADIUS_DP = 7f

/** How long the segmented control's thumb takes to slide to the option just tapped. */
private const val SEGMENT_SLIDE_MS = 200L
private const val CHIP_DP = 32f
private const val BACK_ICON_DP = 26f
private const val CHECK_DP = 20f
private const val LEAD_DP = 24f

// A laptop's row: the Devices list's, and the larger one that heads the Settings hub.
private const val LAPTOP_ROW_DP = 64f
private const val LAPTOP_ROW_LARGE_DP = 72f
private const val LAPTOP_TILE_DP = 36f
private const val LAPTOP_TILE_LARGE_DP = 44f

/**
 * The screens' shared look, after Edgepad 2.0: iOS-style grouped cards on a soft ground, Lato in three
 * weights, sentence case, a nav bar with an accent back link, rounded buttons, an iOS switch and segmented
 * control, and one accent for what is selected, on or the main action. Builders only; each screen assembles
 * its own page from them.
 */
class Ui(
    val context: Context,
) {
    enum class Style {
        /** The one main action on a page: an accent fill. */
        FILLED,

        /** A strong secondary action: a tinted accent fill. */
        OUTLINED,

        /** A way out or aside: accent text alone. */
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
    ): LinearLayout {
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

    /**
     * A screen's nav bar. With somewhere to go back to, an accent chevron and [backLabel] (the screen it
     * returns to) sit at the start and [title] is centred; a top-level screen passes no [onBack] and shows its
     * name as a [Column.largeTitle] instead, so [title] is then only read out, not drawn, unless there is a
     * [lead] such as the mark. [actions] sit at the far end.
     */
    fun bar(
        title: CharSequence,
        onBack: (() -> Unit)?,
        vararg actions: View,
        lead: View? = null,
        backLabel: CharSequence? = null,
    ): FrameLayout =
        NavBar(context).apply {
            minimumHeight = dp(Space.BAR)
            setPadding(dp(Space.XS), 0, dp(Space.XS), 0)
            val wrap = ViewGroup.LayoutParams.WRAP_CONTENT
            val match = ViewGroup.LayoutParams.MATCH_PARENT
            val start =
                LinearLayout(context).apply {
                    gravity = Gravity.CENTER_VERTICAL
                    if (onBack != null) {
                        addView(back(backLabel ?: string(R.string.back), onBack))
                    } else if (lead != null) {
                        addView(
                            lead,
                            LinearLayout.LayoutParams(dp(LEAD_DP), dp(LEAD_DP)).apply {
                                marginStart = dp(Space.M)
                                marginEnd = dp(Space.S)
                            },
                        )
                    }
                }
            val drawn = onBack != null || lead != null
            val name =
                text(title, Type.LABEL, palette.ink, face = Type.bold).apply {
                    isAccessibilityHeading = true
                    maxLines = 1
                    ellipsize = TextUtils.TruncateAt.END
                    gravity = Gravity.CENTER
                    visibility = if (drawn && title.isNotEmpty()) View.VISIBLE else View.GONE
                }
            if (lead != null && onBack == null) {
                // Beside the mark the name reads as a label, not a centred title.
                start.addView(name)
            } else {
                addView(name, FrameLayout.LayoutParams(match, wrap, Gravity.CENTER_VERTICAL))
                centred = name
            }
            addView(
                start,
                FrameLayout.LayoutParams(wrap, match).apply {
                    gravity = Gravity.START or Gravity.CENTER_VERTICAL
                },
            )
            val end =
                LinearLayout(context).apply {
                    gravity = Gravity.CENTER_VERTICAL
                    actions.forEach { addView(it) }
                }
            addView(
                end,
                FrameLayout.LayoutParams(wrap, match).apply {
                    gravity = Gravity.END or Gravity.CENTER_VERTICAL
                },
            )
            ends = listOf(start, end)
        }

    /** The nav bar's back link: an accent chevron and the name of the screen it returns to. */
    private fun back(
        label: CharSequence,
        onBack: () -> Unit,
    ): LinearLayout =
        LinearLayout(context).apply {
            gravity = Gravity.CENTER_VERTICAL
            minimumHeight = dp(Space.TOUCH)
            setPadding(0, 0, dp(Space.S), 0)
            addView(
                Glyph(context, Glyph.Shape.CHEVRON_LEFT, palette.accent),
                LinearLayout.LayoutParams(dp(BACK_ICON_DP), dp(BACK_ICON_DP)),
            )
            addView(text(label, Type.LABEL, palette.accent))
            contentDescription = string(R.string.back)
            tappable(this, Space.S, onBack)
        }

    /** An icon-only button in the accent, on a full-size touch target, named for screen readers and long-press alike. */
    fun icon(
        shape: Glyph.Shape,
        label: CharSequence,
        onTap: () -> Unit,
    ): Glyph = icon(shape, label, palette.accent, onTap)

    /** An icon-only button drawn in [color]. */
    fun icon(
        shape: Glyph.Shape,
        label: CharSequence,
        color: Int,
        onTap: () -> Unit,
    ): Glyph =
        Glyph(context, shape, color).apply {
            contentDescription = label
            tooltipText = label
            layoutParams = LinearLayout.LayoutParams(dp(Space.TOUCH), dp(Space.TOUCH))
            tappable(this, Space.TOUCH / 2, onTap)
        }

    fun text(
        value: CharSequence,
        sp: Float,
        color: Int,
        tracking: Float = 0f,
        face: Typeface = Type.face,
    ): TextView =
        TextView(context).apply {
            text = value
            setTextSize(TypedValue.COMPLEX_UNIT_SP, sp)
            setTextColor(color)
            typeface = face
            letterSpacing = tracking
        }

    /**
     * Secondary text: a sub-line, a value beside a row, a footnote. The name is left over from the monospace
     * design; it is Lato now, like everything else.
     */
    fun mono(
        value: CharSequence,
        sp: Float = Type.SMALL,
        color: Int = palette.dim,
        tracking: Float = 0f,
    ): TextView = text(value, sp, color, tracking)

    /** A full-width rounded button. */
    fun button(
        label: CharSequence,
        style: Style,
        onClick: () -> Unit,
    ): TextView {
        val (fill, ink) =
            when (style) {
                Style.FILLED -> palette.accent to palette.onAccent
                Style.OUTLINED -> palette.accentSoft to palette.accent
                Style.QUIET -> Color.TRANSPARENT to palette.accent
            }
        return text(label, Type.LABEL, ink, face = Type.bold).apply {
            gravity = Gravity.CENTER
            minHeight = dp(Space.BUTTON)
            setPadding(dp(Space.L), 0, dp(Space.L), 0)
            background = rounded(fill, Space.CARD_RADIUS)
            tappable(this, Space.CARD_RADIUS, onClick)
        }
    }

    /**
     * A small pill action beside a row, with a full-size touch target around it. [danger] is for something
     * that cannot be taken back from here, such as forgetting the laptop.
     */
    fun chip(
        label: CharSequence,
        onClick: () -> Unit,
    ): TextView = chip(label, false, onClick)

    /** A chip in the danger colour when [danger] is set. */
    fun chip(
        label: CharSequence,
        danger: Boolean,
        onClick: () -> Unit,
    ): TextView =
        text(label, Type.VALUE, if (danger) palette.danger else palette.ink, face = Type.bold).apply {
            gravity = Gravity.CENTER
            minHeight = dp(Space.TOUCH)
            val inset = (dp(Space.TOUCH) - dp(CHIP_DP)) / 2
            background = InsetDrawable(rounded(palette.faint, CHIP_DP / 2), 0, inset, 0, inset)
            // After the background: a drawable with insets resets the view's padding to those insets.
            setPadding(dp(Space.L), 0, dp(Space.L), 0)
            tappable(this, CHIP_DP / 2, onClick)
        }

    /**
     * Turns [view] on or off: off, it stops answering taps and fades back. Screen readers still reach it and
     * say it is unavailable, so the reason beside it can be found.
     */
    fun enable(
        view: View,
        on: Boolean,
    ) {
        view.isEnabled = on
        view.alpha = if (on) 1f else DISABLED_ALPHA
    }

    /**
     * Asks before something that cannot be taken back from here: [title] and [message] over Cancel and
     * [action], which alone runs [onConfirm]. [owner] is the view the question came from; see [showOver].
     */
    fun confirm(
        owner: View,
        title: CharSequence,
        message: CharSequence,
        action: CharSequence,
        onConfirm: () -> Unit,
    ) {
        AlertDialog
            .Builder(context)
            .setTitle(title)
            .setMessage(message)
            .setPositiveButton(action) { _, _ -> onConfirm() }
            .setNegativeButton(android.R.string.cancel, null)
            .showOver(owner)
    }

    /** A filled rounded rectangle: the one shape behind cards, buttons, chips, tiles and keys. */
    fun rounded(
        color: Int,
        radiusDp: Float,
    ): GradientDrawable =
        GradientDrawable().apply {
            setColor(color)
            cornerRadius = dp(radiusDp).toFloat()
        }

    /** Pressed and focused states for a view drawn without the platform's own button background. */
    fun tappable(
        view: View,
        onClick: () -> Unit,
    ) = tappable(view, 0f, onClick)

    /** As [tappable], with the press and the focus ring rounded to [radius] dp to match the shape they land on. */
    fun tappable(
        view: View,
        radius: Float,
        onClick: () -> Unit,
    ) {
        view.isClickable = true
        view.isFocusable = true
        val corner = if (radius > 0f) dp(radius).toFloat() else 0f
        view.foreground =
            StateListDrawable().apply {
                addState(
                    intArrayOf(android.R.attr.state_pressed),
                    GradientDrawable().apply {
                        setColor(palette.faint)
                        alpha = PRESS_ALPHA
                        cornerRadius = corner
                    },
                )
                addState(
                    intArrayOf(android.R.attr.state_focused),
                    GradientDrawable().apply {
                        setStroke(dp(FOCUS_RING_DP), palette.accent)
                        cornerRadius = corner
                    },
                )
            }
        view.setOnClickListener { onClick() }
    }

    /**
     * A labelled on/off row: the platform Switch, so accessibility and focus come with it, drawn as an iOS
     * switch -- an accent track when on, a round white knob.
     */
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
            thumbDrawable = knob()
            trackDrawable = track()
            thumbTintList = null
            trackTintList = null
            minHeight = dp(Space.ROW)
            setOnCheckedChangeListener { _, on -> onChange(on) }
        }

    /**
     * An iOS segmented control: options side by side on a tinted track, the selected one on a raised thumb
     * that slides. The thumb is a block behind the labels rather than a background on the chosen one, so it
     * can slide from the old option to the new instead of jumping.
     *
     * It paints itself. Until 3.0.1 it drew the selection once and left it, which was invisible on Theme
     * and Orientation -- both rebuild the activity, so a fresh control was built already showing the new
     * choice -- and plainly broken on Macro buttons, which rebuilds nothing and so never moved at all.
     */
    fun segmented(
        options: List<CharSequence>,
        selected: Int,
        onSelect: (Int) -> Unit,
    ): View {
        val inset = dp(SEGMENT_INSET_DP)
        // Every option is the width of the widest, measured in the bold face it takes when chosen, rather than
        // left to wrap. Equal widths are what let the thumb move by sliding alone: a thumb that had to change
        // width as it went would set its own layout params mid-slide, and the layout that followed would
        // cancel the animation it was in.
        val paint =
            TextPaint().apply {
                typeface = Type.bold
                textSize =
                    TypedValue.applyDimension(
                        TypedValue.COMPLEX_UNIT_SP,
                        Type.SMALL,
                        context.resources.displayMetrics,
                    )
            }
        val width = options.maxOf { paint.measureText(it.toString()) }.toInt() + 2 * dp(SEGMENT_PAD_DP)
        // The height is worked out the same way, from the text, so a large font size grows the thumb instead
        // of clipping its labels. The whole control is then at least a touch target tall: the room above and
        // below the drawn track is clear, and every option's label reaches through it to take the tap.
        val lines = paint.fontMetricsInt
        val height = maxOf(dp(SEGMENT_HEIGHT_DP), lines.bottom - lines.top + 2 * dp(SEGMENT_TEXT_PAD_DP))
        val touch = maxOf(dp(Space.TOUCH), height + 2 * inset)
        val slack = (touch - height) / 2 - inset
        val labels =
            LinearLayout(context).apply {
                options.forEach { label ->
                    addView(
                        text(label, Type.SMALL, palette.ink).apply { gravity = Gravity.CENTER },
                        LinearLayout.LayoutParams(width, ViewGroup.LayoutParams.MATCH_PARENT),
                    )
                }
            }
        val fill =
            View(context).apply {
                background =
                    rounded(if (palette.dark) palette.off else palette.card, SEGMENT_THUMB_RADIUS_DP).apply {
                        if (!palette.dark) setStroke(dp(Space.HAIR), palette.line)
                    }
                layoutParams =
                    FrameLayout.LayoutParams(width, height, Gravity.CENTER_VERTICAL).apply { leftMargin = inset }
            }
        var current = selected
        var shown = false

        fun paint(animate: Boolean) {
            for (i in options.indices) {
                val option = labels.getChildAt(i) as TextView
                val on = i == current
                option.typeface = if (on) Type.bold else Type.face
                option.isSelected = on
                option.stateDescription = if (on) string(R.string.chosen) else null
            }
            val x = (current * width).toFloat()
            // Slid only once the control has been seen somewhere. The first placement is the control
            // appearing, and a thumb sliding in from the left on every page open would read as the choice
            // having just changed when nothing has. Animations off in system settings means it never slides.
            if (animate && shown && ValueAnimator.areAnimatorsEnabled()) {
                fill
                    .animate()
                    .translationX(x)
                    .setDuration(SEGMENT_SLIDE_MS)
                    .start()
            } else {
                fill.animate().cancel()
                fill.translationX = x
            }
            shown = true
        }

        for (i in options.indices) {
            val option = labels.getChildAt(i)
            tappable(option, SEGMENT_THUMB_RADIUS_DP) {
                if (current != i) {
                    current = i
                    paint(animate = true)
                    onSelect(i)
                }
            }
            // The press and the focus ring land on the thumb's shape, not on the taller target around it.
            option.foreground = InsetDrawable(option.foreground, 0, slack + inset, 0, slack + inset)
        }
        return FrameLayout(context).apply {
            background = InsetDrawable(rounded(palette.faint, SEGMENT_TRACK_RADIUS_DP), 0, slack, 0, slack)
            // After the background: a drawable with insets resets the view's padding to those insets.
            setPadding(0, 0, 0, 0)
            addView(fill)
            addView(
                labels,
                FrameLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, touch).apply {
                    leftMargin = inset
                    rightMargin = inset
                },
            )
            paint(animate = false)
        }
    }

    /** A plain slider: a rounded track filled in the accent, a dot under each step, and a round white thumb. */
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
                        alpha = TICK_ALPHA
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
                    setColor(KNOB_COLOR)
                    setStroke(dp(Space.HAIR), palette.line)
                    setSize(dp(THUMB_DP), dp(THUMB_DP))
                }
            progressTintList = null
            progressBackgroundTintList = null
            thumbTintList = null
            tickMarkTintList = null
            splitTrack = false
            minimumHeight = dp(Space.TOUCH)
            setPadding(paddingLeft, paddingTop, paddingRight, dp(Space.L))
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

    /** A bold name over a secondary sub-line. */
    fun stack(
        title: CharSequence,
        sub: CharSequence?,
        titleSp: Float = Type.BODY,
    ): LinearLayout =
        LinearLayout(context).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(0, dp(Space.M), 0, dp(Space.M))
            addView(text(title, titleSp, palette.ink, face = Type.bold))
            if (!sub.isNullOrEmpty()) {
                addView(mono(sub, Type.SMALL, palette.dim).apply { setPadding(0, dp(Space.SUB_GAP), 0, 0) })
            }
        }

    /** A separator between two rows in a card. */
    fun hairline(): View = View(context).apply { setBackgroundColor(palette.line) }

    /** True when the screen is wider than tall; pages then split into two columns. */
    val landscape: Boolean
        get() = context.resources.configuration.orientation == Configuration.ORIENTATION_LANDSCAPE

    /** A small bold header over a card, in sentence case, lined up with the card's text. */
    fun section(value: CharSequence): TextView =
        text(value, Type.SMALL, palette.dim, face = Type.bold).apply {
            isAccessibilityHeading = true
            setPadding(dp(Space.CARD_PAD), dp(SECTION_TOP_DP), dp(Space.CARD_PAD), dp(Space.S))
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
                        mono(summary, Type.VALUE, palette.dim).apply {
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
            val shown = valueOf(progress)
            val value = mono(shown, Type.VALUE, palette.dim)
            addView(
                field(label, value).apply {
                    minimumHeight = 0
                    setPadding(0, dp(Space.M), 0, 0)
                },
            )
            // A screen reader reads a slider as a percentage of its range unless told otherwise, and "40
            // percent" says nothing about a key size of 1.4x. It is told what the row shows instead.
            lateinit var track: SeekBar
            track =
                ruler(steps, progress) { step ->
                    val now = valueOf(step)
                    value.text = now
                    track.stateDescription = now
                    onChange(step)
                }
            track.contentDescription = label
            track.stateDescription = shown
            addView(track)
        }

    /**
     * One of [names], a row each with a separator between, the chosen one marked with an accent check. Meant
     * to sit in a [Column.card]. [onPick] gets the index.
     */
    fun choices(
        names: List<CharSequence>,
        selected: Int,
        onPick: (Int) -> Unit,
    ): LinearLayout =
        LinearLayout(context).apply {
            orientation = LinearLayout.VERTICAL
            // The check moves itself. A caller that rebuilt the page to move it would throw away the scroll
            // position with it, which on a list longer than the screen puts the user back at the top every
            // time they pick -- the list of things a shape can run is exactly that long.
            val rows = ArrayList<LinearLayout>(names.size)
            val checks = ArrayList<View>(names.size)
            var current = selected

            fun paint() {
                rows.forEachIndexed { i, row ->
                    val chosen = i == current
                    checks[i].visibility = if (chosen) View.VISIBLE else View.INVISIBLE
                    row.isSelected = chosen
                    row.stateDescription = if (chosen) string(R.string.chosen) else null
                }
            }
            names.forEachIndexed { i, name ->
                if (i > 0) addView(hairline(), ViewGroup.LayoutParams.MATCH_PARENT, dp(Space.HAIR))
                val row = field(name)
                row.minimumHeight = dp(Space.TOUCH)
                val check =
                    Glyph(context, Glyph.Shape.CHECK, palette.accent).apply {
                        importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO
                    }
                row.addView(check, LinearLayout.LayoutParams(dp(CHECK_DP), dp(CHECK_DP)))
                rows += row
                checks += check
                tappable(row) {
                    current = i
                    paint()
                    onPick(i)
                }
                addView(row)
            }
            paint()
        }

    /**
     * A laptop in a card: its icon on an accent tile while [connected] and a faint one otherwise, its [name]
     * in bold over [sub], and [end] at the far side. [large] is the remembered laptop heading the Settings
     * hub; the Devices list's rows are the regular size. One builder for both, so the two cannot drift.
     */
    fun laptop(
        name: CharSequence,
        sub: View?,
        connected: Boolean,
        end: View?,
        large: Boolean = false,
    ): LinearLayout {
        val tile =
            FrameLayout(context).apply {
                background = rounded(if (connected) palette.accent else palette.faint, Space.TILE_RADIUS)
                addView(Glyph(context, Glyph.Shape.LAPTOP, if (connected) palette.onAccent else palette.dim))
            }
        val lines =
            LinearLayout(context).apply {
                orientation = LinearLayout.VERTICAL
                setPadding(0, dp(Space.M), 0, dp(Space.M))
                addView(text(name, if (large) Type.LABEL else Type.BODY, palette.ink, face = Type.bold))
                if (sub != null) {
                    addView(
                        sub,
                        LinearLayout
                            .LayoutParams(
                                ViewGroup.LayoutParams.WRAP_CONTENT,
                                ViewGroup.LayoutParams.WRAP_CONTENT,
                            ).apply { topMargin = dp(Space.SUB_GAP) },
                    )
                }
            }
        val side = dp(if (large) LAPTOP_TILE_LARGE_DP else LAPTOP_TILE_DP)
        val start =
            LinearLayout(context).apply {
                gravity = Gravity.CENTER_VERTICAL
                addView(tile, LinearLayout.LayoutParams(side, side).apply { marginEnd = dp(Space.M) })
                addView(lines, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
            }
        return row(start, end).apply { minimumHeight = dp(if (large) LAPTOP_ROW_LARGE_DP else LAPTOP_ROW_DP) }
    }

    private fun track(): Drawable =
        StateListDrawable().apply {
            addState(intArrayOf(android.R.attr.state_checked), pill(palette.accent))
            addState(intArrayOf(), pill(palette.off))
        }

    // The platform Switch is twice its thumb wide and slides the thumb by one thumb width, so the track is
    // drawn at that size: a 60 by 30 pill whose knob keeps the same 2 dp gap from every edge at both ends.
    private fun pill(color: Int): Drawable =
        rounded(color, (KNOB_DP + 2 * KNOB_GAP_DP) / 2).apply {
            val size = dp(KNOB_DP + 2 * KNOB_GAP_DP)
            setSize(2 * size, size)
        }

    private fun knob(): Drawable {
        val dot =
            GradientDrawable().apply {
                shape = GradientDrawable.OVAL
                setColor(KNOB_COLOR)
                if (!palette.dark) setStroke(dp(Space.HAIR), palette.line)
                setSize(dp(KNOB_DP), dp(KNOB_DP))
            }
        val gap = dp(KNOB_GAP_DP)
        return InsetDrawable(dot, gap, gap, gap, gap)
    }

    /** The track: a rounded line in the off colour, with the part up to the thumb in the accent. */
    private fun sliderTrack(): Drawable {
        fun bar(color: Int) = rounded(color, TRACK_DP / 2).apply { setSize(0, dp(TRACK_DP)) }
        val layers =
            LayerDrawable(
                arrayOf(bar(palette.off), ClipDrawable(bar(palette.accent), Gravity.START, ClipDrawable.HORIZONTAL)),
            )
        layers.setId(0, android.R.id.background)
        layers.setId(1, android.R.id.progress)
        for (i in 0 until 2) {
            layers.setLayerGravity(i, Gravity.CENTER_VERTICAL)
            layers.setLayerHeight(i, dp(TRACK_DP))
        }
        return layers
    }

    private companion object {
        /** Switch knobs and slider thumbs are white in both themes, as on iOS. */
        const val KNOB_COLOR = 0xFFFFFFFF.toInt()

        /** A press darkens by the faint colour at this alpha, so it shows on a card and on the ground alike. */
        const val PRESS_ALPHA = 200

        const val TICK_ALPHA = 128

        /** How far a control that cannot be used right now fades back. */
        const val DISABLED_ALPHA = 0.4f
    }
}

/**
 * The nav bar's frame. Its centred title is kept clear of whatever sits at either end by measuring both ends
 * first, rather than by a fixed margin: at a large font size the back link alone outgrows any margin chosen
 * for the default size, and the title then ran under it.
 */
private class NavBar(
    context: Context,
) : FrameLayout(context) {
    /** What sits at either end: the back link or the mark, and the actions. */
    var ends: List<View> = emptyList()

    /** The title, when it is centred over the whole bar rather than set beside the mark. */
    var centred: View? = null

    override fun onMeasure(
        widthMeasureSpec: Int,
        heightMeasureSpec: Int,
    ) {
        centred?.let { title ->
            ends.forEach { measureChild(it, widthMeasureSpec, heightMeasureSpec) }
            // The same room on both sides, so the title stays centred on the bar and not between its ends.
            val clear = ends.maxOfOrNull { it.measuredWidth } ?: 0
            (title.layoutParams as ViewGroup.MarginLayoutParams).apply {
                marginStart = clear
                marginEnd = clear
            }
        }
        super.onMeasure(widthMeasureSpec, heightMeasureSpec)
    }
}

/**
 * Shows the dialog, and closes it if [owner] leaves the window first. Left to itself a dialog outlives the
 * page that opened it: a rotation or a theme change rebuilds the activity underneath, and the window the
 * dialog still holds leaks. Nothing it asked about is on screen any more by then, so closing it loses nothing.
 */
fun AlertDialog.Builder.showOver(owner: View) {
    val dialog = show()
    val closer =
        object : View.OnAttachStateChangeListener {
            override fun onViewAttachedToWindow(v: View) = Unit

            override fun onViewDetachedFromWindow(v: View) = dialog.dismiss()
        }
    owner.addOnAttachStateChangeListener(closer)
    dialog.setOnDismissListener { owner.removeOnAttachStateChangeListener(closer) }
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
        sp: Float = Type.SMALL,
        color: Int = ui.palette.dim,
        topDp: Float = 0f,
    ): TextView = add(ui.mono(value, sp, color), topDp)

    fun headline(
        value: CharSequence,
        sp: Float,
        topDp: Float = 0f,
    ): TextView = add(ui.text(value, sp, ui.palette.ink, Type.TRACKING_TIGHT, Type.black), topDp)

    /** A top-level screen's name, large and heavy, under a nav bar that carries only actions. */
    fun largeTitle(value: CharSequence): TextView =
        add(
            ui.text(value, Type.LARGE_TITLE, ui.palette.ink, Type.TRACKING_TIGHT, Type.black).apply {
                isAccessibilityHeading = true
                setPadding(ui.dp(Space.XS), 0, 0, ui.dp(Space.S))
            },
        )

    fun body(
        value: CharSequence,
        topDp: Float = 0f,
    ): TextView = add(ui.text(value, Type.VALUE, ui.palette.dim).apply { setLineSpacing(0f, Type.LEADING) }, topDp)

    /** A line of explanation under a card, lined up with the card's text. */
    fun footnote(value: CharSequence): TextView =
        add(
            ui.text(value, Type.SMALL, ui.palette.dim).apply {
                setLineSpacing(0f, Type.LEADING)
                setPadding(ui.dp(Space.CARD_PAD), ui.dp(Space.S), ui.dp(Space.CARD_PAD), 0)
            },
        )

    fun section(value: CharSequence) {
        add(ui.section(value))
    }

    /**
     * A grouped card: rows on a rounded [Palette.card] fill with [Space.CARD_PAD] either side. Put a
     * [hairline] between rows, never after the last.
     */
    fun card(
        topDp: Float = 0f,
        fill: Column.() -> Unit,
    ): LinearLayout {
        val inner =
            LinearLayout(ui.context).apply {
                orientation = LinearLayout.VERTICAL
                background = ui.rounded(ui.palette.card, Space.CARD_RADIUS)
                setPadding(ui.dp(Space.CARD_PAD), 0, ui.dp(Space.CARD_PAD), 0)
            }
        Column(ui, inner).fill()
        return add(inner, topDp)
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
                    if (i == 1) marginStart = ui.dp(Space.XL)
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
