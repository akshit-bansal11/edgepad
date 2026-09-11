package me.akshitbansal.edgepad.screens

import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import android.text.TextPaint
import android.util.TypedValue
import android.view.Gravity
import android.view.MotionEvent
import android.view.View
import android.view.ViewGroup
import android.widget.LinearLayout
import me.akshitbansal.edgepad.Palette
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.surface.ControlSurface
import me.akshitbansal.edgepad.surface.MediaPiece
import kotlin.math.abs
import kotlin.math.roundToInt

/**
 * Drag the media pieces on a full-size canvas with a snapping grid; where they land is where the surface
 * draws them. A piece near the middle snaps to it, and the centre lines light up to say so.
 */
object MediaLayoutScreen {
    fun build(
        ui: Ui,
        settings: Settings,
        onBack: () -> Unit,
    ): View {
        val canvas = Editor(ui.context, settings)
        val header =
            LinearLayout(ui.context).apply {
                gravity = Gravity.CENTER_VERTICAL
                setPadding(ui.dp(Space.L), ui.dp(Space.XL), ui.dp(Space.L), ui.dp(Space.S))
                val back =
                    Glyph(ui.context, Glyph.Shape.CHEVRON_LEFT, ui.palette.ink).apply {
                        contentDescription = ui.string(R.string.back)
                        ui.tappable(this, onBack)
                    }
                addView(back, LinearLayout.LayoutParams(ui.dp(Space.TOUCH), ui.dp(Space.TOUCH)))
                addView(
                    ui.text(
                        ui.string(R.string.media_layout_title),
                        Type.HEADING,
                        ui.palette.ink,
                        Type.sans,
                        Type.TRACKING_TIGHT,
                    ),
                    LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f),
                )
                addView(ui.chip(ui.string(R.string.media_layout_reset)) { canvas.reset() })
            }
        val hint =
            ui.text(ui.string(R.string.media_layout_hint), Type.CAPTION, ui.palette.dim, Type.plain).apply {
                setPadding(ui.dp(Space.L), 0, ui.dp(Space.L), ui.dp(Space.S))
            }
        return LinearLayout(ui.context).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(ui.palette.background)
            addView(header)
            addView(hint)
            addView(canvas, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f))
        }
    }

    /** The surface's own proportions: a piece is dragged by its centre and stored as fractions of the size. */
    private class Editor(
        context: Context,
        private val settings: Settings,
    ) : View(context) {
        private val density = resources.displayMetrics.density
        private val palette = Palette.of(context)
        private val names = MediaPiece.entries.associateWith { context.getString(it.nameRes) }
        private val positions = MediaPiece.entries.associateWith { settings.piece(it) }.toMutableMap()
        private var dragging: MediaPiece? = null
        private var onCentreX = false
        private var onCentreY = false
        private val box = RectF()
        private val stroke =
            Paint(Paint.ANTI_ALIAS_FLAG).apply {
                style = Paint.Style.STROKE
                strokeWidth = Space.HAIR * density
            }
        private val dots = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = palette.line }
        private val text =
            TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
                typeface = Type.mono
                textAlign = Paint.Align.CENTER
                letterSpacing = Type.TRACKING_WIDE
                textSize = TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_SP, Type.MICRO, resources.displayMetrics)
            }

        init {
            contentDescription = context.getString(R.string.media_layout_title)
        }

        fun reset() {
            settings.resetPieces()
            for (piece in MediaPiece.entries) positions[piece] = settings.piece(piece)
            invalidate()
        }

        private fun bounds(
            piece: MediaPiece,
            out: RectF,
        ) {
            val (fx, fy) = positions.getValue(piece)
            val cx = fx * width
            val cy = fy * height
            val (w, h) =
                when (piece) {
                    MediaPiece.NOW_PLAYING -> {
                        ControlSurface.NOW_PLAYING_WIDTH_DP to
                            ControlSurface.NOW_PLAYING_HEIGHT_DP
                    }

                    MediaPiece.TRANSPORT -> {
                        ControlSurface.SKIP_GAP_DP * 2 + Space.TOUCH to ControlSurface.PLAY_DP
                    }
                }
            out.set(cx - w * density / 2, cy - h * density / 2, cx + w * density / 2, cy + h * density / 2)
        }

        override fun onDraw(canvas: Canvas) {
            super.onDraw(canvas)
            val cell = GRID_DP * density
            val r = DOT_DP * density / 2
            var y = cell
            while (y < height) {
                var x = cell
                while (x < width) {
                    canvas.drawCircle(x, y, r, dots)
                    x += cell
                }
                y += cell
            }
            stroke.color = palette.line
            canvas.drawRect(
                stroke.strokeWidth / 2,
                stroke.strokeWidth / 2,
                width - stroke.strokeWidth / 2,
                height - stroke.strokeWidth / 2,
                stroke,
            )
            // The centre lines: faint always, ink while a piece sits on one.
            stroke.color = if (onCentreX) palette.ink else palette.line
            canvas.drawLine(width / 2f, 0f, width / 2f, height.toFloat(), stroke)
            stroke.color = if (onCentreY) palette.ink else palette.line
            canvas.drawLine(0f, height / 2f, width.toFloat(), height / 2f, stroke)
            for (piece in MediaPiece.entries) {
                bounds(piece, box)
                stroke.color = if (piece == dragging) palette.ink else palette.dim
                canvas.drawRoundRect(box, CORNER_DP * density, CORNER_DP * density, stroke)
                text.color = stroke.color
                canvas.drawText(
                    names.getValue(piece),
                    box.centerX(),
                    box.centerY() + text.textSize * Type.CAP_CENTRE,
                    text,
                )
            }
        }

        override fun performClick(): Boolean {
            super.performClick()
            return true
        }

        override fun onTouchEvent(event: MotionEvent): Boolean {
            when (event.actionMasked) {
                MotionEvent.ACTION_DOWN -> {
                    dragging =
                        MediaPiece.entries.lastOrNull {
                            bounds(it, box)
                            box.contains(event.x, event.y)
                        } ?: return false
                }

                MotionEvent.ACTION_MOVE -> {
                    val piece = dragging ?: return false
                    val x = snap(event.x, width)
                    val y = snap(event.y, height)
                    positions[piece] = x to y
                    onCentreX = abs(x - HALF) < EPSILON
                    onCentreY = abs(y - HALF) < EPSILON
                }

                MotionEvent.ACTION_UP -> {
                    val piece = dragging ?: return false
                    val (x, y) = positions.getValue(piece)
                    settings.setPiece(piece, x, y)
                    dragging = null
                    onCentreX = false
                    onCentreY = false
                    performClick()
                }

                MotionEvent.ACTION_CANCEL -> {
                    dragging = null
                    onCentreX = false
                    onCentreY = false
                }

                else -> {
                    return false
                }
            }
            invalidate()
            return true
        }

        /** The grid point nearest [px] along an axis of [extent] pixels, or the centre when close to it, as a fraction. */
        private fun snap(
            px: Float,
            extent: Int,
        ): Float {
            val centre = extent / 2f
            if (abs(px - centre) < CENTRE_SNAP_DP * density) return HALF
            val cell = GRID_DP * density
            val snapped = (px / cell).roundToInt() * cell
            return (snapped / extent).coerceIn(0f, 1f)
        }

        private companion object {
            const val CORNER_DP = 6f
            const val GRID_DP = 24f
            const val DOT_DP = 3f
            const val CENTRE_SNAP_DP = 16f
            const val HALF = 0.5f
            const val EPSILON = 1e-3f
        }
    }
}
