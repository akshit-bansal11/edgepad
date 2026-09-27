package me.akshitbansal.edgepad.screens

import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
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

/**
 * Drag the media pieces on a full-size canvas with a snapping grid; where they land is where the surface
 * draws them. A piece near the middle snaps to it, and the centre line it sits on turns accent to say so.
 */
object MediaLayoutScreen {
    private val scaleRange = StepRange(Settings.MIN_MEDIA_SCALE, Settings.MAX_MEDIA_SCALE, 0.1f)

    /** The hint's line height, as a multiple of its size, the same as a footnote's. */
    private const val HINT_LEADING = 1.4f

    fun build(
        ui: Ui,
        settings: Settings,
        onBack: () -> Unit,
    ): View {
        val canvas = Editor(ui.context, settings)
        return EditorFrame.build(
            ui,
            ui.string(R.string.media_layout_title),
            canvas,
            onBack,
            backLabel = ui.string(R.string.settings_title),
        ) { close ->
            LinearLayout(ui.context).apply {
                orientation = LinearLayout.VERTICAL
                addView(
                    ui.slider(
                        ui.string(R.string.media_scale),
                        scaleRange.steps,
                        scaleRange.stepOf(settings.mediaScale),
                        { step -> ui.string(R.string.multiplier_value, scaleRange.valueAt(step)) },
                    ) { step ->
                        settings.mediaScale = scaleRange.valueAt(step)
                        canvas.rescale()
                    },
                )
                addView(
                    ui
                        .mono(
                            ui.string(R.string.media_layout_hint),
                            Type.SMALL,
                            ui.palette.dim,
                        ).apply {
                            setLineSpacing(0f, HINT_LEADING)
                            setPadding(0, ui.dp(Space.XS), 0, ui.dp(Space.S))
                        },
                )
                addView(
                    EditorFrame.resetAndDone(ui, {
                        canvas.reset()
                        settings.mediaScale = Settings.DEFAULT_MEDIA_SCALE
                        canvas.rescale()
                        close()
                    }, close, Ui.Style.OUTLINED),
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
        private val corner = CORNER_DP * density
        private val drop = Space.HAIR * density
        private val fill = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.FILL }
        private val shadow =
            Paint(Paint.ANTI_ALIAS_FLAG).apply {
                style = Paint.Style.FILL
                color = palette.line
            }

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
                // A piece at rest is a card, with a soft line under it on the light ground; the one being
                // dragged turns accent and lifts off the grid on a shadow.
                val lifted = piece == dragging
                if (lifted) {
                    canvas.drawRoundRect(box, corner, corner, lift)
                } else if (!palette.dark) {
                    box.offset(0f, drop)
                    canvas.drawRoundRect(box, corner, corner, shadow)
                    box.offset(0f, -drop)
                }
                fill.color = if (lifted) palette.accent else palette.card
                canvas.drawRoundRect(box, corner, corner, fill)
                text.color = if (lifted) palette.onAccent else palette.dim
                canvas.drawText(
                    names.getValue(piece),
                    box.centerX(),
                    box.centerY() + text.textSize * Type.CAP_CENTRE,
                    text,
                )
            }
        }

        // Lint's ClickableViewAccessibility wants this on the same class that overrides onTouchEvent;
        // inheriting it from LayoutCanvas does not satisfy the rule, so both editors carry their own.
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
            const val CORNER_DP = 12f
        }
    }
}
