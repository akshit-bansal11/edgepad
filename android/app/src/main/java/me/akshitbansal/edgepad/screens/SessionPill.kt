package me.akshitbansal.edgepad.screens

import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
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

/**
 * One agent session as a plain row: the agent's initial in a circle, the session's title, and at the end a state dot
 * with its word. It draws the initial, never a vendor logo. The row is [HEIGHT_DP] tall and the parent's width.
 */
class SessionPill(
    context: Context,
) : View(context) {
    private val metrics = resources.displayMetrics
    private val density = metrics.density
    private val palette = Palette.of(context)
    private val claude = context.getString(R.string.agent_claude).take(1)
    private val codex = context.getString(R.string.agent_codex).take(1)
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
    private val initialMetrics = initialPaint.fontMetrics
    private val titleMetrics = titlePaint.fontMetrics
    private val stateMetrics = statePaint.fontMetrics
    private val row = RectF()
    private val circle = RectF()

    private var initial = ""
    private var title = ""
    private var stateLabel = ""
    private var shownTitle = ""
    private var stateWidth = 0f

    /** Shows a session of [agent] called [title] in [state], whose words are [stateLabel] from the screen's strings. */
    fun set(
        agent: AgentToken,
        title: String,
        state: SessionStateToken,
        stateLabel: String,
    ) {
        initial = if (agent == AgentToken.CLAUDE) claude else codex
        this.title = title
        this.stateLabel = stateLabel
        dot.color =
            when (state) {
                SessionStateToken.BUSY -> palette.levelGreen
                SessionStateToken.WAIT -> palette.levelOrange
                SessionStateToken.IDLE -> palette.track
            }
        contentDescription = "$title, $stateLabel"
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
        shorten()
    }

    /** Fits the title to the room between the circle and the state, with an ellipsis; done here so drawing does not. */
    private fun shorten() {
        stateWidth = statePaint.measureText(stateLabel)
        val room = width - circle.right - (Space.M * 3 + DOT_DP + Space.S) * density - stateWidth
        if (room <= 0f) return
        shownTitle = TextUtils.ellipsize(title, titlePaint, room, TextUtils.TruncateAt.END).toString()
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val corner = Space.TILE_RADIUS * density
        canvas.drawRoundRect(row, corner, corner, body)
        canvas.drawCircle(circle.centerX(), circle.centerY(), circle.width() / 2f, badge)
        val initialY = circle.centerY() - (initialMetrics.ascent + initialMetrics.descent) / 2f
        canvas.drawText(initial, circle.centerX(), initialY, initialPaint)
        val centreY = height / 2f
        val titleY = centreY - (titleMetrics.ascent + titleMetrics.descent) / 2f
        canvas.drawText(shownTitle, circle.right + Space.M * density, titleY, titlePaint)
        val stateX = width - Space.M * density - stateWidth
        val stateY = centreY - (stateMetrics.ascent + stateMetrics.descent) / 2f
        canvas.drawText(stateLabel, stateX, stateY, statePaint)
        val radius = DOT_DP * density / 2f
        canvas.drawCircle(stateX - Space.S * density - radius, centreY, radius, dot)
    }

    private fun sp(value: Float): Float = TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_SP, value, metrics)

    private companion object {
        const val DEFAULT_DP = 200f
        const val HEIGHT_DP = 36f
        const val DOT_DP = 8f
    }
}
