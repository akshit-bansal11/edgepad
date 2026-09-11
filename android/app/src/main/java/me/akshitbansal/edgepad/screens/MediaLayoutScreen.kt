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

/** Drag the three media pieces anywhere on a full-size canvas; where they land is where the surface draws them. */
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
                )
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
        private val box = RectF()
        private val stroke =
            Paint(Paint.ANTI_ALIAS_FLAG).apply {
                style = Paint.Style.STROKE
                strokeWidth = Space.HAIR * density
            }
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

                    MediaPiece.PLAY -> {
                        ControlSurface.PLAY_DP to ControlSurface.PLAY_DP
                    }

                    MediaPiece.SKIP -> {
                        ControlSurface.SKIP_GAP_DP * 2 + Space.TOUCH to Space.TOUCH
                    }
                }
            out.set(cx - w * density / 2, cy - h * density / 2, cx + w * density / 2, cy + h * density / 2)
        }

        override fun onDraw(canvas: Canvas) {
            super.onDraw(canvas)
            stroke.color = palette.line
            canvas.drawRect(
                Space.HAIR * density,
                Space.HAIR * density,
                width - Space.HAIR * density,
                height - Space.HAIR * density,
                stroke,
            )
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
                        }
                            ?: return false
                }

                MotionEvent.ACTION_MOVE -> {
                    val piece = dragging ?: return false
                    positions[piece] = (event.x / width).coerceIn(0f, 1f) to (event.y / height).coerceIn(0f, 1f)
                }

                MotionEvent.ACTION_UP -> {
                    val piece = dragging ?: return false
                    val (x, y) = positions.getValue(piece)
                    settings.setPiece(piece, x, y)
                    dragging = null
                    performClick()
                }

                MotionEvent.ACTION_CANCEL -> {
                    dragging = null
                }

                else -> {
                    return false
                }
            }
            invalidate()
            return true
        }

        private companion object {
            const val CORNER_DP = 6f
        }
    }
}
