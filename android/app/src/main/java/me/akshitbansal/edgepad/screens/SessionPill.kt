package me.akshitbansal.edgepad.screens

import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import android.graphics.drawable.Drawable
import android.text.TextPaint
import android.text.TextUtils
import android.util.TypedValue
import android.view.View
import me.akshitbansal.edgepad.Palette
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.protocol.AgentToken
import me.akshitbansal.edgepad.protocol.SessionStateToken

/** The widths a session row shares between its title and its state word. */
object SessionRow {
    private const val STATE_SHARE = 0.4f

    /** The most the state word may take: 40 percent of the row, so the title always keeps the rest. */
    fun stateRoom(rowWidth: Float): Float = rowWidth * STATE_SHARE

    /** What is left for the title once the circle, the fixed gaps and the (already clamped) state are taken. */
    fun titleRoom(
        rowWidth: Float,
        circleRight: Float,
        gaps: Float,
        stateWidth: Float,
    ): Float = rowWidth - circleRight - gaps - stateWidth

    /** The state word shows only when all of it fits within its share and the title keeps at least [minTitle]. */
    fun showsState(
        rowWidth: Float,
        circleRight: Float,
        gaps: Float,
        fullStateWidth: Float,
        minTitle: Float,
    ): Boolean =
        fullStateWidth <= stateRoom(rowWidth) && titleRoom(rowWidth, circleRight, gaps, fullStateWidth) >= minTitle

    /** Whether [pct] is a real context fill; anything else draws a dash. */
    fun knowsContext(pct: Int): Boolean = pct in 0..FULL

    /** The arc of the context ring for [pct], in degrees: 0 for unknown, a full turn at 100. */
    fun contextSweep(pct: Int): Float = if (knowsContext(pct)) pct * FULL_TURN / FULL else 0f

    private const val FULL = 100
    private const val FULL_TURN = 360f
}

/**
 * One agent session as a plain row: the agent's mark in a circle, the session's title, a state dot with its word, and
 * at the very end a small ring for how full that session's context window is (a dash when that is not known). The
 * row is [HEIGHT_DP] tall and the parent's width.
 */
class SessionPill(
    context: Context,
) : View(context) {
    private val metrics = resources.displayMetrics
    private val density = metrics.density
    private val palette = Palette.of(context)
    private val none = context.getString(R.string.sys_none)
    private val body = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = palette.faint }
    private val badge = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = palette.accent }
    private val dot = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = palette.track }
    private val initialPaint =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            typeface = Type.bold
            textSize = sp(Type.MICRO)
            color = palette.onAccent
            textAlign = Paint.Align.CENTER
        }
    private val titlePaint =
        TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
            typeface = Type.bold
            textSize = sp(Type.SMALL)
            color = palette.ink
        }
    private val statePaint =
        TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
            typeface = Type.face
            textSize = sp(Type.MICRO)
            color = palette.dim
        }
    private val contextTrack =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            style = Paint.Style.STROKE
            strokeWidth = CTX_STROKE_DP * density
            color = palette.track
        }
    private val contextArc =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            style = Paint.Style.STROKE
            strokeWidth = CTX_STROKE_DP * density
            strokeCap = Paint.Cap.ROUND
            color = palette.accent
        }
    private val levels = intArrayOf(palette.levelGreen, palette.levelYellow, palette.levelOrange, palette.levelRed)
    private val initialMetrics = initialPaint.fontMetrics
    private val titleMetrics = titlePaint.fontMetrics
    private val stateMetrics = statePaint.fontMetrics
    private val row = RectF()
    private val circle = RectF()
    private val contextRing = RectF()
    private var contextPct = -1
    private var contextSweep = 0f

    private var initial = ""
    private var title = ""
    private var stateLabel = ""
    private var shownTitle = ""
    private var shownState = ""
    private var stateWidth = 0f
    private var logo: Drawable? = null
    private var outline = false
    private val ring =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            style = Paint.Style.STROKE
            strokeWidth = density
            color = palette.line
        }

    /**
     * Shows a session of [agent] called [title] in [state], whose words are [stateLabel] from the screen's strings.
     * The avatar is the tool's [logoRes] when it has one, else its [brand] colour with the agent's initial. [context]
     * is how full its context window is, 0 to 100 or below 0 for unknown, and [contextLabel] says so in words.
     */
    fun set(
        agent: AgentToken,
        title: String,
        state: SessionStateToken,
        stateLabel: String,
        brand: Int,
        logoRes: Int?,
        context: Int = -1,
        contextLabel: String = "",
    ) {
        initial = agent.token.take(1).uppercase()
        badge.color = brand
        initialPaint.color = TileContrast.letter(brand)
        outline = TileContrast.needsOutline(brand, palette.faint)
        logo = logoRes?.let { this.context.getDrawable(it) }
        placeLogo()
        this.title = title
        this.stateLabel = stateLabel
        dot.color =
            when (state) {
                SessionStateToken.BUSY -> palette.levelGreen
                SessionStateToken.WAIT -> palette.levelOrange
                SessionStateToken.IDLE -> palette.track
            }
        contextPct = if (SessionRow.knowsContext(context)) context else -1
        contextSweep = SessionRow.contextSweep(contextPct)
        contextArc.color = if (contextPct < 0) palette.track else levels[RingLevel.colourIndex(contextPct)]
        contentDescription = listOf(title, stateLabel, contextLabel).filter { it.isNotEmpty() }.joinToString(", ")
        shorten()
        invalidate()
    }

    override fun onMeasure(
        widthMeasureSpec: Int,
        heightMeasureSpec: Int,
    ) {
        val width = resolveSize((DEFAULT_DP * density).toInt(), widthMeasureSpec)
        setMeasuredDimension(width, (HEIGHT_DP * density).toInt())
    }

    override fun onSizeChanged(
        w: Int,
        h: Int,
        oldw: Int,
        oldh: Int,
    ) {
        super.onSizeChanged(w, h, oldw, oldh)
        row.set(0f, 0f, w.toFloat(), h.toFloat())
        // The circle sits XS inside the row, so its radius is half the row less XS: 14 dp in a 36 dp row.
        val inset = Space.XS * density
        circle.set(inset, inset, h - inset, h - inset)
        // The context ring sits at the end of the row, in a slot of its own.
        val side = CTX_DP * density
        val right = w - Space.M * density
        val half = contextTrack.strokeWidth / 2f
        contextRing.set(
            right - side + half,
            (h - side) / 2f + half,
            right - half,
            (h + side) / 2f - half,
        )
        placeLogo()
        shorten()
    }

    private fun placeLogo() {
        logo?.setBounds(circle.left.toInt(), circle.top.toInt(), circle.right.toInt(), circle.bottom.toInt())
    }

    /** Fits the title to the room between the circle and the state, with an ellipsis; done here so drawing does not. */
    private fun shorten() {
        if (width <= 0) return
        val stateRoom = SessionRow.stateRoom(width.toFloat())
        val gaps = (Space.M * 4 + DOT_DP + Space.S + CTX_DP) * density
        val full = statePaint.measureText(stateLabel)
        val minTitle = MIN_TITLE_DP * density
        if (SessionRow.showsState(width.toFloat(), circle.right, gaps, full, minTitle)) {
            shownState = TextUtils.ellipsize(stateLabel, statePaint, stateRoom, TextUtils.TruncateAt.END).toString()
            stateWidth = statePaint.measureText(shownState)
        } else {
            // Too narrow: the dot alone says the state, and the words stay in the content description.
            shownState = ""
            stateWidth = 0f
        }
        val room = SessionRow.titleRoom(width.toFloat(), circle.right, gaps, stateWidth)
        if (room <= 0f) return
        shownTitle = TextUtils.ellipsize(title, titlePaint, room, TextUtils.TruncateAt.END).toString()
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val corner = Space.TILE_RADIUS * density
        canvas.drawRoundRect(row, corner, corner, body)
        val mark = logo
        if (mark != null) {
            mark.draw(canvas)
        } else {
            canvas.drawCircle(circle.centerX(), circle.centerY(), circle.width() / 2f, badge)
            if (outline) canvas.drawCircle(circle.centerX(), circle.centerY(), circle.width() / 2f, ring)
            val initialY = circle.centerY() - (initialMetrics.ascent + initialMetrics.descent) / 2f
            canvas.drawText(initial, circle.centerX(), initialY, initialPaint)
        }
        val centreY = height / 2f
        val titleY = centreY - (titleMetrics.ascent + titleMetrics.descent) / 2f
        canvas.drawText(shownTitle, circle.right + Space.M * density, titleY, titlePaint)
        drawContext(canvas)
        val stateX = width - Space.M * 2 * density - CTX_DP * density - stateWidth
        val stateY = centreY - (stateMetrics.ascent + stateMetrics.descent) / 2f
        canvas.drawText(shownState, stateX, stateY, statePaint)
        val radius = DOT_DP * density / 2f
        val gap = if (shownState.isEmpty()) 0f else Space.S * density
        canvas.drawCircle(stateX - gap - radius, centreY, radius, dot)
    }

    // A ring in the level colour for a known fill, else a dash on the track's colour.
    private fun drawContext(canvas: Canvas) {
        if (contextPct < 0) {
            val y = contextRing.centerY() - (stateMetrics.ascent + stateMetrics.descent) / 2f
            statePaint.textAlign = Paint.Align.CENTER
            canvas.drawText(none, contextRing.centerX(), y, statePaint)
            statePaint.textAlign = Paint.Align.LEFT
            return
        }
        canvas.drawArc(contextRing, 0f, FULL_TURN, false, contextTrack)
        canvas.drawArc(contextRing, START, contextSweep, false, contextArc)
    }

    private fun sp(value: Float): Float = TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_SP, value, metrics)

    private companion object {
        const val DEFAULT_DP = 200f
        const val HEIGHT_DP = 36f
        const val DOT_DP = 8f
        const val MIN_TITLE_DP = 90f
        const val CTX_DP = 16f
        const val CTX_STROKE_DP = 2.5f
        const val START = -90f
        const val FULL_TURN = 360f
    }
}
