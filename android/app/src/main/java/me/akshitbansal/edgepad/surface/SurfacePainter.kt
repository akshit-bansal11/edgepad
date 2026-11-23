package me.akshitbansal.edgepad.surface

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import android.graphics.drawable.Drawable
import android.text.TextPaint
import android.util.TypedValue
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.link.blob.MediaIconStore
import me.akshitbansal.edgepad.protocol.ActionId
import kotlin.math.roundToInt

/**
 * Where the transport's three hit rects fall, as pure arithmetic so a layout editor can place the same pieces the
 * surface does and a test can pin them. [out] takes play, previous and next, each as left, top, right, bottom.
 */
object PieceGeometry {
    const val TRANSPORT_RECTS = 12

    fun transport(
        w: Float,
        h: Float,
        tx: Float,
        ty: Float,
        density: Float,
        mediaScale: Float,
        touchDp: Float,
        out: FloatArray,
    ) {
        val touch = touchDp * density / 2
        val play = maxOf(ControlSurface.PLAY_DP * density * mediaScale / 2, touch)
        val gap = maxOf(ControlSurface.SKIP_GAP_DP * density * mediaScale, play + touch)
        val cx = tx * w
        val cy = ty * h
        rect(out, 0, cx - play, cy - play, cx + play, cy + play)
        rect(out, 4, cx - gap - touch, cy - touch, cx - gap + touch, cy + touch)
        rect(out, 8, cx + gap - touch, cy - touch, cx + gap + touch, cy + touch)
    }

    /** The now-playing piece's nominal box centred at fractions [nx], [ny], as left, top, right, bottom in [out]. */
    fun nowPlaying(
        w: Float,
        h: Float,
        nx: Float,
        ny: Float,
        density: Float,
        mediaScale: Float,
        out: FloatArray,
    ) {
        val px = density * mediaScale
        val halfW = ControlSurface.NOW_PLAYING_WIDTH_DP * px / 2
        val halfH = ControlSurface.NOW_PLAYING_HEIGHT_DP * px / 2
        rect(out, 0, nx * w - halfW, ny * h - halfH, nx * w + halfW, ny * h + halfH)
    }

    private fun rect(
        out: FloatArray,
        at: Int,
        left: Float,
        top: Float,
        right: Float,
        bottom: Float,
    ) {
        out[at] = left
        out[at + 1] = top
        out[at + 2] = right
        out[at + 3] = bottom
    }
}

/**
 * Everything the surface paints that is not the finger, the trackpad's own hints or the widgets: the now-playing
 * piece, the media transport, the dials' rulers, the top buttons and the battery strip. [ControlSurface] and the
 * layout editors call the same methods with a Canvas and what to show, so a preview is the real drawing.
 *
 * Every Paint, Drawable and tint is made here once; the draw methods allocate nothing. Touch handling is not here.
 */
class SurfacePainter(
    context: Context,
    private val ink: Int,
    private val accent: Int,
    private val dim: Int,
    private val faint: Int,
    onAccent: Int,
    dark: Boolean,
    private val mediaScale: Float,
    private val ruler: RulerPainter,
) {
    private val density = context.resources.displayMetrics.density
    private val metrics = context.resources.displayMetrics
    private val logo = AppLogo(context, dark)
    private val stroke =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            style = Paint.Style.STROKE
            strokeWidth = dp(Space.HAIR)
        }
    private val fill = Paint(Paint.ANTI_ALIAS_FLAG)

    // The play disc casts a soft accent glow downward, the one control on the surface that is lifted.
    private val playPaint =
        Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = accent
            setShadowLayer(media(PLAY_GLOW_DP), 0f, media(PLAY_GLOW_DROP_DP), withAlpha(accent, PLAY_GLOW_ALPHA))
        }

    /** Public so the surface can measure and cut its lines with the paints they are drawn in. */
    val titlePaint =
        TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
            typeface = Type.bold
            textSize = sp(TITLE_SP) * mediaScale
        }
    val subPaint =
        TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
            typeface = Type.face
            textSize = sp(Type.MICRO) * mediaScale
            color = dim
        }
    private val lockTint = withAlpha(accent, LOCK_TINT_ALPHA)
    private val lockIconOn = icon(context, R.drawable.ic_lock, accent)
    private val skipBackIcon = icon(context, R.drawable.ic_skip_back, ink)
    private val skipForwardIcon = icon(context, R.drawable.ic_skip_forward, ink)
    private val playIcon = icon(context, R.drawable.ic_play, onAccent)
    private val pauseIcon = icon(context, R.drawable.ic_pause, onAccent)

    /**
     * The piece that says what is playing: logo (or an empty frame), the two text lines and, unless a dial scrubs,
     * the progress line with its thumb at [fraction].
     */
    fun drawNowPlaying(
        canvas: Canvas,
        box: RectF,
        app: String,
        titleLine: String,
        subLine: String,
        scrubOnADial: Boolean,
        scrubbing: Boolean,
        fraction: Float,
        icons: MediaIconStore<Bitmap>,
    ) {
        val size = media(ControlSurface.LOGO_DP)
        val cx = box.left + size / 2
        val cy = box.centerY() - (if (scrubOnADial) 0f else dp(PROGRESS_BELOW_DP) / 2)
        if (app.isEmpty()) {
            stroke.color = faint
            val corner = size * LOGO_ROUNDING
            canvas.drawRoundRect(cx - size / 2, cy - size / 2, cx + size / 2, cy + size / 2, corner, corner, stroke)
        } else {
            logo.draw(canvas, app, cx, cy, size, ink, icons.get(app), icons.get(MediaIconStore.ARTWORK))
        }
        val textX = box.left + size + dp(Space.M)
        canvas.drawText(titleLine, textX, cy - dp(Space.XS), titlePaint)
        canvas.drawText(subLine, textX, cy + subPaint.textSize + dp(Space.S), subPaint)
        // The progress line only when no dial scrubs; then it can be slid itself.
        if (scrubOnADial) return
        val progressY = box.bottom
        stroke.color = faint
        stroke.strokeWidth = dp(PROGRESS_STROKE_DP)
        canvas.drawLine(box.left, progressY, box.right, progressY, stroke)
        // What has played is live, so it takes the accent, as a level dial's held notches do.
        stroke.color = accent
        canvas.drawLine(box.left, progressY, box.left + box.width() * fraction, progressY, stroke)
        stroke.strokeWidth = dp(Space.HAIR)
        fill.color = accent
        canvas.drawCircle(
            box.left + box.width() * fraction,
            progressY,
            dp(if (scrubbing) THUMB_HELD_DP else THUMB_DP) / 2,
            fill,
        )
    }

    /** Previous, play or pause, next; [pressed] is the one whose button is held, if any. */
    fun drawTransport(
        canvas: Canvas,
        prevHit: RectF,
        playHit: RectF,
        nextHit: RectF,
        playing: Boolean,
        pressed: ActionId?,
    ) {
        val skipRadius = dp(Space.TOUCH) / 2
        if (pressed == ActionId.PREVIOUS_TRACK) pressDisc(canvas, prevHit.centerX(), prevHit.centerY(), skipRadius)
        if (pressed == ActionId.NEXT_TRACK) pressDisc(canvas, nextHit.centerX(), nextHit.centerY(), skipRadius)
        drawIcon(canvas, skipBackIcon, prevHit.centerX(), prevHit.centerY(), media(SKIP_DP))
        drawIcon(canvas, skipForwardIcon, nextHit.centerX(), nextHit.centerY(), media(SKIP_DP))
        val cx = playHit.centerX()
        val cy = playHit.centerY()
        canvas.drawCircle(cx, cy, media(ControlSurface.PLAY_DP) / 2, playPaint)
        if (pressed == ActionId.PLAY_PAUSE) pressDisc(canvas, cx, cy, media(ControlSurface.PLAY_DP) / 2)
        // On the accent disc in its own on-accent colour: a pause while playing, else a play.
        drawIcon(canvas, if (playing) pauseIcon else playIcon, cx, cy, media(PLAY_ICON_DP))
    }

    /** One top button: its press disc, the lit lock's tint with the accent copy of the lock icon, or [icon]. */
    fun drawTopButton(
        canvas: Canvas,
        hit: RectF,
        icon: Drawable,
        pressed: Boolean,
        lockLit: Boolean,
    ) {
        val cx = hit.centerX()
        val cy = hit.centerY()
        if (pressed) pressDisc(canvas, cx, cy, dp(LOCK_TINT_DP) / 2)
        if (lockLit) {
            fill.color = lockTint
            canvas.drawCircle(cx, cy, dp(LOCK_TINT_DP) / 2, fill)
        }
        drawIcon(canvas, if (lockLit) lockIconOn else icon, cx, cy, dp(ICON_DP))
    }

    /** The battery strip, and a round-rect press fill over it while a finger is on it. */
    fun drawBatteryStrip(
        canvas: Canvas,
        strip: BatteryStrip,
        pressed: Boolean,
    ) {
        strip.draw(canvas)
        if (pressed) {
            val radius = strip.hit.height() / 2
            fill.color = faint
            canvas.drawRoundRect(strip.hit, radius, radius, fill)
        }
    }

    /** One dial's ruler, label and value, in the painter's colours. [scratch] is the caller's four-float buffer. */
    fun drawDial(
        canvas: Canvas,
        perimeter: Perimeter,
        dial: Dial,
        centre: Float,
        after: Float,
        before: Float,
        valueText: String,
        scratch: FloatArray,
        outputName: String?,
        outputIcon: Drawable?,
    ) {
        ruler.draw(
            canvas,
            perimeter,
            centre,
            after,
            before,
            dial.rulerDp,
            if (dial.control != null) dial.rulerLengthDp else null,
            dial.armed,
            ink,
            accent,
            dim,
            dial.label,
            valueText,
            scratch,
            if (dial.kind == DialKind.VOLUME) outputName else null,
            outputIcon,
        )
    }

    fun drawIcon(
        canvas: Canvas,
        icon: Drawable,
        cx: Float,
        cy: Float,
        size: Float,
    ) {
        val half = (size / 2).toInt()
        icon.setBounds(cx.toInt() - half, cy.toInt() - half, cx.toInt() + half, cy.toInt() + half)
        icon.draw(canvas)
    }

    /** The pressed state of a button: a fill step drawn while the finger is down, no animation (UX-28). */
    private fun pressDisc(
        canvas: Canvas,
        cx: Float,
        cy: Float,
        radius: Float,
    ) {
        fill.color = faint
        canvas.drawCircle(cx, cy, radius, fill)
    }

    private fun dp(value: Float): Float = value * density

    /** A media piece's dp, at the user's media size. */
    private fun media(value: Float): Float = value * density * mediaScale

    private fun sp(value: Float): Float = TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_SP, value, metrics)

    companion object {
        private const val RGB_MASK = 0x00FFFFFF
        private const val MAX_ALPHA = 255
        private const val ALPHA_SHIFT = 24

        /** The lit lock's round tint. */
        private const val LOCK_TINT_DP = 44f
        private const val LOCK_TINT_ALPHA = 0.16f

        /** The play disc's glow: how soft, how far down it falls, and how strong. */
        private const val PLAY_GLOW_DP = 14f
        private const val PLAY_GLOW_DROP_DP = 6f
        private const val PLAY_GLOW_ALPHA = 0.35f
        private const val ICON_DP = 24f
        private const val SKIP_DP = 24f
        private const val PLAY_ICON_DP = 24f
        private const val TITLE_SP = 15f

        /** An empty logo slot's corners, as a fraction of its size: an app icon's rounding. */
        private const val LOGO_ROUNDING = 0.22f
        private const val PROGRESS_BELOW_DP = 16f
        private const val PROGRESS_STROKE_DP = 2f
        private const val THUMB_DP = 8f
        private const val THUMB_HELD_DP = 14f

        /** [color] with its alpha replaced by [fraction] of opaque. */
        fun withAlpha(
            color: Int,
            fraction: Float,
        ): Int = color and RGB_MASK or ((fraction * MAX_ALPHA).roundToInt() shl ALPHA_SHIFT)

        private fun icon(
            context: Context,
            id: Int,
            color: Int,
        ): Drawable = checkNotNull(context.getDrawable(id)).mutate().apply { setTint(color) }
    }
}
