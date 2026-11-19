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
 * One agent session as a pill: the agent's initial on an accent tile, the session's title, and a dot with the state
 * under it. It draws the initial, never a vendor logo. Its height is [Space.TOUCH]; its width is the parent's.
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
    private val tile = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = palette.accent }
    private val dot = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = palette.dim }
    private val initialPaint =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            typeface = Type.bold
            textSize = sp(Type.VALUE)
            color = palette.onAccent
            textAlign = Paint.Align.CENTER
        }
    private val titlePaint =
        TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
            typeface = Type.face
            textSize = sp(Type.VALUE)
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
    private val pill = RectF()
    private val square = RectF()

    private var initial = ""
    private var title = ""
    private var stateLabel = ""
    private var shownTitle = ""
    private var shownState = ""

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
        dot.color = if (state == SessionStateToken.IDLE) palette.dim else palette.accent
        if (state == SessionStateToken.WAIT) dot.alpha = HALF
        contentDescription = "$title, $stateLabel"
        shorten()
        invalidate()
    }

    override fun onMeasure(
        widthMeasureSpec: Int,
        heightMeasureSpec: Int,
    ) {
        val width = resolveSize((DEFAULT_DP * density).toInt(), widthMeasureSpec)
        setMeasuredDimension(width, (Space.TOUCH * density).toInt())
    }

    override fun onSizeChanged(
        w: Int,
        h: Int,
        oldw: Int,
        oldh: Int,
    ) {
        super.onSizeChanged(w, h, oldw, oldh)
        pill.set(0f, 0f, w.toFloat(), h.toFloat())
        val inset = Space.XS * density
        square.set(inset, inset, h - inset, h - inset)
        shorten()
    }

    /** Fits the title and the state to the room beside the tile, with an ellipsis; done here so drawing does not. */
    private fun shorten() {
        val room = width - square.right - (Space.M + Space.L) * density
        if (room <= 0f) return
        shownTitle = TextUtils.ellipsize(title, titlePaint, room, TextUtils.TruncateAt.END).toString()
        val stateRoom = (room - (DOT_DP + Space.S) * density).coerceAtLeast(0f)
        shownState = TextUtils.ellipsize(stateLabel, statePaint, stateRoom, TextUtils.TruncateAt.END).toString()
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        canvas.drawRoundRect(pill, height / 2f, height / 2f, body)
        val tileRadius = Space.TILE_RADIUS * density
        canvas.drawRoundRect(square, tileRadius, tileRadius, tile)
        val initialY = square.centerY() - (initialMetrics.ascent + initialMetrics.descent) / 2f
        canvas.drawText(initial, square.centerX(), initialY, initialPaint)
        val textX = square.right + Space.M * density
        val titleHeight = titleMetrics.descent - titleMetrics.ascent
        val block = titleHeight + stateMetrics.descent - stateMetrics.ascent
        val top = (height - block) / 2f
        canvas.drawText(shownTitle, textX, top - titleMetrics.ascent, titlePaint)
        val baseline = top + titleHeight - stateMetrics.ascent
        val radius = DOT_DP * density / 2f
        canvas.drawCircle(textX + radius, baseline + (stateMetrics.ascent + stateMetrics.descent) / 2f, radius, dot)
        canvas.drawText(shownState, textX + (DOT_DP + Space.S) * density, baseline, statePaint)
    }

    private fun sp(value: Float): Float = TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_SP, value, metrics)

    private companion object {
        const val DEFAULT_DP = 200f
        const val DOT_DP = 8f
        const val HALF = 128
    }
}
