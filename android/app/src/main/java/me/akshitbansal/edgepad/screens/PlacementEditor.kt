package me.akshitbansal.edgepad.screens

import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF
import android.os.Bundle
import android.text.TextPaint
import android.util.TypedValue
import android.view.MotionEvent
import android.view.View
import android.view.accessibility.AccessibilityNodeInfo
import me.akshitbansal.edgepad.Palette
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.surface.DialKind
import me.akshitbansal.edgepad.surface.Perimeter
import me.akshitbansal.edgepad.surface.Placement
import kotlin.math.abs

/** How a placement reads in Settings: "TOP-LEFT L", "RIGHT EDGE", "OFF". */
fun placementName(
    context: Context,
    placement: Placement?,
): String {
    val corner = placement?.corner
    val edge = placement?.edge
    val resId =
        when {
            placement == null -> R.string.place_off
            corner == 0 -> R.string.place_top_left
            corner == 1 -> R.string.place_top_right
            corner == 2 -> R.string.place_bottom_right
            corner != null -> R.string.place_bottom_left
            edge == 0 -> R.string.place_top
            edge == 1 -> R.string.place_right
            edge == 2 -> R.string.place_bottom
            else -> R.string.place_left
        }
    return context.getString(resId)
}

/**
 * A small phone outline with every placed dial on its edge. Drag a dial along the outline to move it; near
 * a corner it snaps on and wraps the bend as an L. The same moves are offered to accessibility services as
 * actions, so placing a dial never depends on dragging.
 */
class PlacementEditor(
    context: Context,
    private val settings: Settings,
    private val onMoved: (DialKind) -> Unit,
) : View(context) {
    /** For layout tools only. */
    constructor(context: Context) : this(context, Settings(context), {})

    private val density = resources.displayMetrics.density
    private val palette = Palette.of(context)
    private val names = DialKind.entries.associateWith { context.getString(it.nameRes) }
    private val shorts = DialKind.entries.associateWith { context.getString(it.shortRes) }
    private val moves =
        DialKind.entries.associateWith { kind ->
            val name = names.getValue(kind)
            AccessibilityNodeInfo.AccessibilityAction(
                View.generateViewId(),
                context.getString(R.string.editor_move_clockwise, name),
            ) to
                AccessibilityNodeInfo.AccessibilityAction(
                    View.generateViewId(),
                    context.getString(R.string.editor_move_anticlockwise, name),
                )
        }
    private val hint = context.getString(R.string.editor_hint).split('\n')

    private var perimeter = Perimeter(1f, 1f, 1f)
    private var inset = 0f
    private val outline = RectF()
    private val path = Path()
    private val hit = FloatArray(2)
    private val pt = FloatArray(4)
    private var dragging: DialKind? = null
    private var draft: Placement? = null

    private val edge =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            style = Paint.Style.STROKE
            strokeWidth = OUTLINE_DP * density
            color = palette.dim
        }
    private val segment =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            style = Paint.Style.STROKE
            strokeCap = Paint.Cap.ROUND
            strokeJoin = Paint.Join.ROUND
            color = palette.ink
        }
    private val text =
        TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
            typeface = Type.mono
            textAlign = Paint.Align.CENTER
            letterSpacing = LABEL_TRACKING
            textSize = TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_SP, LABEL_SP, resources.displayMetrics)
        }

    init {
        isFocusable = true
        describe()
    }

    /** Settings changed from outside, such as a dial switched on or off. */
    fun refresh() {
        describe()
        invalidate()
    }

    private fun placementOf(kind: DialKind): Placement? = if (kind == dragging) draft else settings.placement(kind)

    private fun describe() {
        val placed =
            DialKind.entries.mapNotNull { kind ->
                placementOf(
                    kind,
                )?.let { context.getString(R.string.editor_item, names.getValue(kind), placementName(context, it)) }
            }
        contentDescription =
            if (placed.isEmpty()) context.getString(R.string.editor_none) else placed.joinToString(", ")
    }

    override fun onSizeChanged(
        w: Int,
        h: Int,
        oldw: Int,
        oldh: Int,
    ) {
        super.onSizeChanged(w, h, oldw, oldh)
        inset = OUTLINE_DP * density
        perimeter = Perimeter(w - 2 * inset, h - 2 * inset, RADIUS_DP * density)
        outline.set(inset / 2, inset / 2, w - inset / 2, h - inset / 2)
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val radius = RADIUS_DP * density
        canvas.drawRoundRect(outline, radius, radius, edge)
        text.color = palette.dim
        val leading = text.textSize * HINT_LEADING
        hint.forEachIndexed { i, line ->
            val y = height / 2f + (i - (hint.size - 1) / 2f) * leading + text.textSize * CAP_CENTRE
            canvas.drawText(line, width / 2f, y, text)
        }
        canvas.save()
        canvas.translate(inset, inset)
        for (kind in DialKind.entries) {
            val placement = placementOf(kind) ?: continue
            val centre = perimeter.lengthAt(placement.at)
            val half = SEGMENT_HALF_DP * density
            val step = STEP_DP * density
            path.reset()
            perimeter.point(centre - half, pt)
            path.moveTo(pt[0], pt[1])
            var s = centre - half + step
            while (s <= centre + half) {
                perimeter.point(s, pt)
                path.lineTo(pt[0], pt[1])
                s += step
            }
            segment.strokeWidth = (if (kind == dragging) HELD_DP else SEGMENT_DP) * density
            canvas.drawPath(path, segment)
            perimeter.point(centre, pt)
            text.color = if (kind == dragging) palette.ink else palette.dim
            val depth = LABEL_DEPTH_DP * density
            canvas.drawText(
                shorts.getValue(kind),
                pt[0] + pt[2] * depth,
                pt[1] + pt[3] * depth + text.textSize * CAP_CENTRE,
                text,
            )
        }
        canvas.restore()
    }

    override fun performClick(): Boolean {
        super.performClick()
        return true
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        val x = event.x - inset
        val y = event.y - inset
        when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                val kind = nearest(x, y) ?: return false
                dragging = kind
                draft = settings.placement(kind)
                // Inside a scrolling page: a dial held is being dragged, not the page scrolled.
                parent?.requestDisallowInterceptTouchEvent(true)
            }

            MotionEvent.ACTION_MOVE -> {
                if (dragging == null) return false
                perimeter.project(x, y, hit)
                draft = Placement.of(perimeter.atOf(hit[0]))
            }

            MotionEvent.ACTION_UP -> {
                val kind = dragging ?: return false
                settings.place(kind, draft)
                dragging = null
                draft = null
                performClick()
                onMoved(kind)
                describe()
            }

            MotionEvent.ACTION_CANCEL -> {
                dragging = null
                draft = null
            }

            else -> {
                return false
            }
        }
        invalidate()
        return true
    }

    private fun nearest(
        x: Float,
        y: Float,
    ): DialKind? {
        perimeter.project(x, y, hit)
        if (hit[1] > GRAB_DEPTH_DP * density) return null
        var best: DialKind? = null
        var bestGap = (SEGMENT_HALF_DP + GRAB_SLACK_DP) * density
        for (kind in DialKind.entries) {
            val placement = settings.placement(kind) ?: continue
            val gap = abs(perimeter.delta(perimeter.lengthAt(placement.at), hit[0]))
            if (gap <= bestGap) {
                best = kind
                bestGap = gap
            }
        }
        return best
    }

    override fun onInitializeAccessibilityNodeInfo(info: AccessibilityNodeInfo) {
        super.onInitializeAccessibilityNodeInfo(info)
        for (kind in DialKind.entries) {
            if (settings.placement(kind) == null) continue
            val (clockwise, anticlockwise) = moves.getValue(kind)
            info.addAction(clockwise)
            info.addAction(anticlockwise)
        }
    }

    override fun performAccessibilityAction(
        action: Int,
        arguments: Bundle?,
    ): Boolean {
        for ((kind, pair) in moves) {
            val step =
                when (action) {
                    pair.first.id -> NUDGE
                    pair.second.id -> -NUDGE
                    else -> continue
                }
            val placement = settings.placement(kind) ?: return false
            settings.place(kind, Placement.of(placement.at + step))
            onMoved(kind)
            refresh()
            return true
        }
        return super.performAccessibilityAction(action, arguments)
    }

    private companion object {
        const val OUTLINE_DP = 1.5f
        const val RADIUS_DP = 20f
        const val SEGMENT_HALF_DP = 22f
        const val STEP_DP = 2f
        const val SEGMENT_DP = 2.5f
        const val HELD_DP = 4f
        const val LABEL_DEPTH_DP = 16f
        const val LABEL_SP = 8.5f
        const val LABEL_TRACKING = 0.12f
        const val GRAB_DEPTH_DP = 32f
        const val GRAB_SLACK_DP = 10f
        const val HINT_LEADING = 1.8f
        const val CAP_CENTRE = 0.35f

        /** An accessibility move: an eighth of an edge. */
        const val NUDGE = 0.125f
    }
}
