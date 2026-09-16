package me.akshitbansal.edgepad.screens

import android.content.Context
import android.graphics.Canvas
import android.graphics.DashPathEffect
import android.graphics.RectF
import android.view.MotionEvent
import android.view.View
import android.widget.LinearLayout
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.surface.ControlSurface
import me.akshitbansal.edgepad.surface.MediaPiece
import kotlin.math.roundToInt

/**
 * Drag the media pieces on a full-size canvas with a snapping grid; where they land is where the surface
 * draws them. A piece near the middle snaps to it, and the centre lines light up to say so.
 */
object MediaLayoutScreen {
    private const val SCALE_STEP = 0.1f

    private fun scaleOf(step: Int): Float = Settings.MIN_MEDIA_SCALE + step * SCALE_STEP

    private fun toStep(scale: Float): Int = ((scale - Settings.MIN_MEDIA_SCALE) / SCALE_STEP).roundToInt()

    private val SCALE_STEPS = ((Settings.MAX_MEDIA_SCALE - Settings.MIN_MEDIA_SCALE) / SCALE_STEP).roundToInt()

    fun build(
        ui: Ui,
        settings: Settings,
        onBack: () -> Unit,
    ): View {
        val canvas = Editor(ui.context, settings)
        return EditorFrame.build(ui, ui.string(R.string.media_layout_title).uppercase(), canvas, onBack) { close ->
            LinearLayout(ui.context).apply {
                orientation = LinearLayout.VERTICAL
                addView(
                    ui.slider(
                        ui.string(R.string.media_scale),
                        SCALE_STEPS,
                        toStep(settings.mediaScale),
                        { step -> ui.string(R.string.multiplier_value, scaleOf(step)) },
                    ) { step ->
                        settings.mediaScale = scaleOf(step)
                        canvas.rescale()
                    },
                )
                addView(
                    ui
                        .mono(
                            ui.string(R.string.media_layout_hint),
                            Type.MICRO,
                            ui.palette.dim,
                            Type.TRACKING_ROW,
                        ).apply {
                            setPadding(0, ui.dp(Space.S), 0, ui.dp(Space.S))
                        },
                )
                addView(
                    EditorFrame.resetAndDone(ui, {
                        canvas.reset()
                        settings.mediaScale = Settings.DEFAULT_MEDIA_SCALE
                        canvas.rescale()
                        close()
                    }, close),
                )
            }
        }
    }

    /** The surface's own proportions: a piece is dragged by its centre and stored as fractions of the size. */
    private class Editor(
        context: Context,
        private val settings: Settings,
    ) : LayoutCanvas(context) {
        private val names = MediaPiece.entries.associateWith { context.getString(it.nameRes) }
        private val positions = MediaPiece.entries.associateWith { settings.piece(it) }.toMutableMap()
        private var dragging: MediaPiece? = null
        private val dash = DashPathEffect(floatArrayOf(DASH_DP * density, GAP_DP * density), 0f)

        init {
            contentDescription = context.getString(R.string.media_layout_title)
        }

        private var scale = settings.mediaScale

        fun rescale() {
            scale = settings.mediaScale
            invalidate()
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
            val px = density * scale
            out.set(cx - w * px / 2, cy - h * px / 2, cx + w * px / 2, cy + h * px / 2)
        }

        override fun onDraw(canvas: Canvas) {
            super.onDraw(canvas)
            for (piece in MediaPiece.entries) {
                bounds(piece, box)
                // A piece at rest is dashed; the one being dragged lifts off the grid on a shadow.
                val lifted = piece == dragging
                if (lifted) canvas.drawRoundRect(box, CORNER_DP * density, CORNER_DP * density, lift)
                stroke.pathEffect = if (lifted) null else dash
                stroke.color = if (lifted) palette.ink else palette.dim
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
                    setOnCentre(x, y)
                }

                MotionEvent.ACTION_UP -> {
                    val piece = dragging ?: return false
                    val (x, y) = positions.getValue(piece)
                    settings.setPiece(piece, x, y)
                    dragging = null
                    clearOnCentre()
                    performClick()
                }

                MotionEvent.ACTION_CANCEL -> {
                    dragging = null
                    clearOnCentre()
                }

                else -> {
                    return false
                }
            }
            invalidate()
            return true
        }

        private companion object {
            const val DASH_DP = 4f
            const val GAP_DP = 3f
        }
    }
}
