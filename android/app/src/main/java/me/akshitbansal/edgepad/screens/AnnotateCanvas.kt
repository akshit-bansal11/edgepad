package me.akshitbansal.edgepad.screens

import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF
import android.view.MotionEvent
import android.view.View
import me.akshitbansal.edgepad.Palette
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.protocol.PenColor
import me.akshitbansal.edgepad.surface.Letterbox
import me.akshitbansal.edgepad.surface.PenTool
import me.akshitbansal.edgepad.surface.Stroke
import me.akshitbansal.edgepad.surface.Strokes
import me.akshitbansal.edgepad.surface.WirePoint

/**
 * The phone as an absolute pen: a panel in the laptop's shape where a touch at a point is a stroke at the
 * same point of the laptop's screen. The laptop draws the real ink; the preview here is only so the finger
 * has something to follow. [screenSize] is read on every layout and draw, since the laptop may change its
 * display mid-session.
 */
class AnnotateCanvas(
    context: Context,
    private val strokes: Strokes,
    private val screenSize: () -> Pair<Int, Int>?,
    private val send: (Frame) -> Unit,
) : View(context) {
    /** Android lint requires a (Context) constructor on every custom View; nothing inflates this one. */
    constructor(context: Context) : this(context, Strokes(), { null }, {})

    private val palette = Palette.of(context)
    private val paint = Paint(Paint.ANTI_ALIAS_FLAG)
    private val path = Path()
    private val oval = RectF()

    // The last point sent, so a finger that moved less than one wire step sends nothing.
    private var lastSent: WirePoint? = null
    private var fingerDown = false

    private fun box(): Letterbox? {
        val (w, h) = screenSize() ?: return null
        return Letterbox.fit(width.toFloat(), height.toFloat(), w, h)
    }

    /** Redraws after the strokes changed from outside the canvas (undo, clear, a new colour). */
    fun refresh() = invalidate()

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val box = box() ?: return
        paint.style = Paint.Style.FILL
        paint.color = palette.card
        canvas.drawRect(box.left, box.top, box.left + box.width, box.top + box.height, paint)
        canvas.save()
        canvas.clipRect(box.left, box.top, box.left + box.width, box.top + box.height)
        for (stroke in strokes.strokes) drawStroke(canvas, box, stroke)
        strokes.active?.let { drawStroke(canvas, box, it) }
        canvas.restore()
    }

    private fun drawStroke(
        canvas: Canvas,
        box: Letterbox,
        stroke: Stroke,
    ) {
        val points = stroke.points.map { box.toPixels(it.x, it.y) }
        if (points.isEmpty()) return
        val penWidth = maxOf(1f, Strokes.PEN_SHARE * box.height)
        paint.style = Paint.Style.STROKE
        paint.strokeCap = Paint.Cap.ROUND
        paint.strokeJoin = Paint.Join.ROUND
        paint.strokeWidth = maxOf(1f, Strokes.widthShare(stroke.tool) * box.height)
        paint.color = OPAQUE or PenColor.entries[stroke.colour.coerceIn(0, Strokes.PALETTE_SIZE - 1)].rgb
        // The whole path is one stroke() call, so an overlapping stretch is not darker than the rest.
        if (stroke.tool == PenTool.HIGHLIGHTER) paint.alpha = (Strokes.HIGHLIGHTER_ALPHA * OPAQUE_ALPHA).toInt()
        val (x0, y0) = points.first()
        val (x1, y1) = points.last()
        when (stroke.tool) {
            PenTool.PEN, PenTool.HIGHLIGHTER -> {
                path.rewind()
                path.moveTo(x0, y0)
                // A lone down point still shows as a dot, because a round cap draws a zero-length line.
                if (points.size == 1) path.lineTo(x0, y0)
                for ((x, y) in points.drop(1)) path.lineTo(x, y)
                canvas.drawPath(path, paint)
            }

            PenTool.LINE -> {
                canvas.drawLine(x0, y0, x1, y1, paint)
            }

            PenTool.ARROW -> {
                canvas.drawLine(x0, y0, x1, y1, paint)
                Strokes.arrowWings(x0, y0, x1, y1, penWidth)?.let {
                    canvas.drawLine(x1, y1, it[0], it[1], paint)
                    canvas.drawLine(x1, y1, it[2], it[3], paint)
                }
            }

            PenTool.RECTANGLE -> {
                canvas.drawRect(minOf(x0, x1), minOf(y0, y1), maxOf(x0, x1), maxOf(y0, y1), paint)
            }

            PenTool.ELLIPSE -> {
                oval.set(minOf(x0, x1), minOf(y0, y1), maxOf(x0, x1), maxOf(y0, y1))
                canvas.drawOval(oval, paint)
            }
        }
    }

    override fun performClick(): Boolean {
        super.performClick()
        return true
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        val box = box() ?: return super.onTouchEvent(event)
        when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                requestUnbufferedDispatch(event)
                parent?.requestDisallowInterceptTouchEvent(true)
                fingerDown = true
                lastSent = null
                emit(box.toWire(event.x, event.y), Strokes.STATE_DOWN)
            }

            MotionEvent.ACTION_MOVE -> {
                if (fingerDown) {
                    // Every sample since the last event, not just the newest: a fast stroke is otherwise
                    // drawn as straight chords between the few points the frame rate allowed.
                    for (i in 0 until event.historySize) {
                        emit(box.toWire(event.getHistoricalX(i), event.getHistoricalY(i)), Strokes.STATE_MOVE)
                    }
                    emit(box.toWire(event.x, event.y), Strokes.STATE_MOVE)
                }
            }

            MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> {
                if (fingerDown) {
                    fingerDown = false
                    // The up is always sent, even on a cancel: a stroke the laptop never sees end stays open.
                    emit(box.toWire(event.x, event.y), Strokes.STATE_UP, force = true)
                    if (event.actionMasked == MotionEvent.ACTION_UP) performClick()
                }
            }

            else -> {
                return super.onTouchEvent(event)
            }
        }
        return true
    }

    private fun emit(
        wire: Pair<Int, Int>,
        state: Int,
        force: Boolean = false,
    ) {
        val point = WirePoint(wire.first, wire.second)
        // Down and up are edges and always go; only a move to the point already sent is redundant.
        if (state == Strokes.STATE_MOVE && !force && point == lastSent) return
        lastSent = point
        strokes.pen(point, state)
        send(Frame.Pen(point.x, point.y, state))
        invalidate()
    }

    private companion object {
        const val OPAQUE = 0xFF000000.toInt()
        const val OPAQUE_ALPHA = 255
    }
}
