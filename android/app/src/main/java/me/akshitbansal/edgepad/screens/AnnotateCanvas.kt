package me.akshitbansal.edgepad.screens

import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.Path
import android.graphics.Rect
import android.graphics.RectF
import android.view.MotionEvent
import android.view.View
import me.akshitbansal.edgepad.Palette
import me.akshitbansal.edgepad.protocol.Frame
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
    private val exclusion = Rect()
    private val exclusions = listOf(exclusion)

    // ANDU-5: one Path per finished PEN or HIGHLIGHTER stroke, keyed by identity (Stroke has no equals), valid for
    // one letterbox only: a new box (layout, laptop display) empties it. Undo and clear prune it in [refresh].
    // ponytail: 200 cached strokes, older ones are rebuilt each draw; raise if a drawing needs more.
    private val finished = LinkedHashMap<Stroke, Path>()
    private var boxLeft = Float.NaN
    private var boxTop = Float.NaN
    private var boxWidth = Float.NaN
    private var boxHeight = Float.NaN

    // The stroke under the finger: its path grows by the new samples only; a new stroke or a new box rebuilds it.
    private var activePath = Path()
    private var activeStroke: Stroke? = null
    private var activePoints = 0

    // The last point sent, so a finger that moved less than one wire step sends nothing.
    private var lastSent: WirePoint? = null
    private var fingerDown = false

    private fun box(): Letterbox? {
        val (w, h) = screenSize() ?: return null
        return Letterbox.fit(width.toFloat(), height.toFloat(), w, h)
    }

    /** Redraws after the strokes changed from outside the canvas (undo, clear, a new colour). */
    fun refresh() {
        // Undo and clear only shrink the list, so a cache larger than it holds a stroke that is gone.
        if (finished.size > strokes.strokes.size) finished.keys.retainAll(strokes.strokes.toSet())
        invalidate()
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val box = box() ?: return
        keepCacheFor(box)
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
        val points = stroke.points
        if (points.isEmpty()) return
        val penWidth = maxOf(1f, Strokes.penShare(stroke.width) * box.height)
        paint.style = Paint.Style.STROKE
        paint.strokeCap = Paint.Cap.ROUND
        paint.strokeJoin = Paint.Join.ROUND
        paint.strokeWidth = maxOf(1f, Strokes.widthShare(stroke.tool, stroke.width) * box.height)
        // The stroke's own colour, width and opacity, as the laptop draws it. The whole path is one drawPath call, so
        // an overlapping stretch is not darker than the rest, at any opacity.
        paint.color = stroke.rgb or OPAQUE
        paint.alpha = Strokes.alpha(stroke.tool, stroke.opacity)
        val (x0, y0) = box.toPixels(points.first().x, points.first().y)
        val (x1, y1) = box.toPixels(points.last().x, points.last().y)
        when (stroke.tool) {
            PenTool.PEN, PenTool.HIGHLIGHTER -> {
                canvas.drawPath(pathOf(box, stroke), paint)
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

    /** The caches hold pixels for one letterbox; any other box rebuilds every path once. */
    private fun keepCacheFor(box: Letterbox) {
        if (box.left == boxLeft && box.top == boxTop && box.width == boxWidth && box.height == boxHeight) return
        boxLeft = box.left
        boxTop = box.top
        boxWidth = box.width
        boxHeight = box.height
        finished.clear()
        activeStroke = null
    }

    /** A PEN or HIGHLIGHTER stroke's path: cached once finished, grown by its new samples while drawn. */
    private fun pathOf(
        box: Letterbox,
        stroke: Stroke,
    ): Path {
        finished[stroke]?.let { return it }
        if (stroke === strokes.active) return grow(box, stroke)
        if (stroke === activeStroke) {
            // It just finished: the path grown under the finger becomes its cached one, not a copy.
            val grown = grow(box, stroke)
            if (finished.size < MAX_CACHED) {
                finished[stroke] = grown
                activePath = Path()
                activeStroke = null
            }
            return grown
        }
        val built = if (finished.size < MAX_CACHED) Path().also { finished[stroke] = it } else path
        built.rewind()
        extend(built, box, stroke, 0)
        return built
    }

    private fun grow(
        box: Letterbox,
        stroke: Stroke,
    ): Path {
        if (stroke !== activeStroke || activePoints > stroke.points.size) {
            activePath.rewind()
            activeStroke = stroke
            activePoints = 0
        }
        activePoints = extend(activePath, box, stroke, activePoints)
        return activePath
    }

    /** Adds [stroke]'s points from [from] on to [target], which holds the ones before; returns the new count. */
    private fun extend(
        target: Path,
        box: Letterbox,
        stroke: Stroke,
        from: Int,
    ): Int {
        val points = stroke.points
        for (i in from until points.size) {
            val (x, y) = box.toPixels(points[i].x, points[i].y)
            if (i == 0) {
                target.moveTo(x, y)
                // A lone down point still shows as a dot, because a round cap draws a zero-length line.
                target.lineTo(x, y)
            } else {
                target.lineTo(x, y)
            }
        }
        return points.size
    }

    override fun performClick(): Boolean {
        super.performClick()
        return true
    }

    // AND-A-4: the pad is absolute, so a stroke near the screen edge must not become the system Back swipe,
    // which would leave Annotate and end the laptop session. Android keeps at most 200 dp of each edge.
    override fun onLayout(
        changed: Boolean,
        left: Int,
        top: Int,
        right: Int,
        bottom: Int,
    ) {
        super.onLayout(changed, left, top, right, bottom)
        exclusion.set(0, 0, right - left, bottom - top)
        systemGestureExclusionRects = exclusions
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
        const val MAX_CACHED = 200
    }
}
