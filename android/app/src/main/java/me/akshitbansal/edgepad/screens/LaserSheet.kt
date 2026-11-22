package me.akshitbansal.edgepad.screens

import android.app.AlertDialog
import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.LinearLayout
import me.akshitbansal.edgepad.LaserSizes
import me.akshitbansal.edgepad.Palette
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type

private const val SHEET_WIDTH_DP = 440f
private const val SHEET_RADIUS_DP = 16f
private const val SHEET_GAP_DP = 14f
private const val PREVIEW_WIDTH_DP = 150f
private const val PREVIEW_HEIGHT_DP = 84f
private const val PREVIEW_RADIUS_DP = 8f
private const val SCREEN_FILL = 0xFF0D1117.toInt()
private const val SLIDE_TEXT = 0xFFE6EDF3.toInt()
private const val SLIDE_BAR = 0xFF3A424D.toInt()
private const val LASER_RED = 0xFFFF1A1A.toInt()
private const val LASER_HOT = 0xFFFFD0D0.toInt()
private const val GLOW_SCALE = 1.8f
private const val HOT_SCALE = 0.35f
private const val GLOW_ALPHA = 0x55
private const val TRAIL_STEPS = 6
private const val TRAIL_LENGTH = 0.16f
private const val OPAQUE = 0xFF

/** The Presenter's laser settings: a preview at the laptop's scale, the three sizes and the trail switch. */
class LaserSheet(
    private val ui: Ui,
    private val settings: Settings,
    private val screenSize: () -> Pair<Int, Int>?,
    private val onChange: () -> Unit,
) {
    /** Opens the sheet over [owner], at the bottom of the screen. */
    fun show(owner: View) {
        val shape = screenSize()
        val boxHeight =
            if (shape != null && shape.first > 0) PREVIEW_WIDTH_DP * shape.second / shape.first else PREVIEW_HEIGHT_DP
        val preview =
            LaserPreview(ui.context).apply {
                size = settings.laserSize
                trail = settings.laserTrail
                sample = ui.string(R.string.presenter_preview_sample)
            }
        val sizes =
            ui.segmented(
                listOf(
                    ui.string(R.string.presenter_size_small),
                    ui.string(R.string.presenter_size_medium),
                    ui.string(R.string.presenter_size_large),
                ),
                LaserSizes.ALL.indexOf(settings.laserSize),
            ) { i ->
                settings.laserSize = LaserSizes.ALL[i]
                preview.size = settings.laserSize
                onChange()
            }
        val trail =
            ui.toggle(ui.string(R.string.presenter_laser_trail), settings.laserTrail) { on: Boolean ->
                settings.laserTrail = on
                preview.trail = on
                onChange()
            }
        val wrap = ViewGroup.LayoutParams.WRAP_CONTENT
        val match = ViewGroup.LayoutParams.MATCH_PARENT
        val gap = ui.dp(SHEET_GAP_DP)
        val head =
            LinearLayout(ui.context).apply {
                gravity = Gravity.CENTER_VERTICAL
                val title = ui.string(R.string.presenter_laser_title)
                addView(ui.text(title, Type.LABEL, ui.palette.ink, face = Type.bold))
                val note = ui.secondary(ui.string(R.string.presenter_laser_zoomed))
                note.gravity = Gravity.END
                addView(note, LinearLayout.LayoutParams(0, wrap, 1f).apply { marginStart = ui.dp(Space.M) })
            }
        val pick =
            LinearLayout(ui.context).apply {
                orientation = LinearLayout.VERTICAL
                addView(ui.secondary(ui.string(R.string.presenter_size_label)))
                addView(sizes, LinearLayout.LayoutParams(wrap, wrap).apply { topMargin = ui.dp(Space.S) })
            }
        val middle =
            LinearLayout(ui.context).apply {
                gravity = Gravity.CENTER_VERTICAL
                addView(preview, LinearLayout.LayoutParams(ui.dp(PREVIEW_WIDTH_DP), ui.dp(boxHeight)))
                addView(pick, LinearLayout.LayoutParams(0, wrap, 1f).apply { marginStart = gap })
            }
        val body =
            LinearLayout(ui.context).apply {
                orientation = LinearLayout.VERTICAL
                val pad = ui.dp(Space.L)
                setPadding(pad, pad, pad, pad)
                addView(head, LinearLayout.LayoutParams(match, wrap))
                addView(middle, LinearLayout.LayoutParams(match, wrap).apply { topMargin = gap })
                addView(ui.hairline(), LinearLayout.LayoutParams(match, ui.dp(1f)).apply { setMargins(0, gap, 0, gap) })
                addView(trail, LinearLayout.LayoutParams(match, wrap))
            }
        val dialog = AlertDialog.Builder(ui.context).setView(body).openOver(owner)
        dialog.window?.apply {
            setBackgroundDrawable(ui.rounded(ui.palette.popover, SHEET_RADIUS_DP))
            setGravity(Gravity.BOTTOM)
            val width = minOf(owner.width - 2 * ui.dp(Space.PAGE), ui.dp(SHEET_WIDTH_DP))
            setLayout(width, wrap)
            attributes = attributes.apply { y = ui.dp(Space.L) }
        }
    }
}

/**
 * A laptop screen in miniature: a slide line and the laser dot at the same share of the box's height as the
 * laptop draws it of its screen's, with the same white-hot core, red ring and short glow.
 */
private class LaserPreview(
    context: Context,
) : View(context) {
    var size = LaserSizes.DEFAULT
        set(value) {
            field = value
            invalidate()
        }

    var trail = false
        set(value) {
            field = value
            invalidate()
        }

    var sample: CharSequence = ""

    private val palette = Palette.of(context)
    private val paint = Paint(Paint.ANTI_ALIAS_FLAG)
    private val box = RectF()
    private val density = resources.displayMetrics.density

    init {
        importantForAccessibility = IMPORTANT_FOR_ACCESSIBILITY_NO
        paint.typeface = Type.bold
        paint.textSize =
            TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_SP, SAMPLE_SP, resources.displayMetrics)
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val w = width.toFloat()
        val h = height.toFloat()
        val corner = PREVIEW_RADIUS_DP * density
        box.set(0f, 0f, w, h)
        paint.style = Paint.Style.FILL
        paint.color = SCREEN_FILL
        canvas.drawRoundRect(box, corner, corner, paint)
        paint.color = SLIDE_TEXT
        val left = w * TEXT_LEFT
        canvas.drawText(sample, 0, sample.length, left, h * TEXT_TOP, paint)
        paint.color = SLIDE_BAR
        val bar = BAR_DP * density
        box.set(left, h * BAR_TOP, w * BAR_LONG, h * BAR_TOP + bar)
        canvas.drawRoundRect(box, bar / 2f, bar / 2f, paint)
        box.set(left, h * BAR_TOP + bar * 2f, w * BAR_SHORT, h * BAR_TOP + bar * 3f)
        canvas.drawRoundRect(box, bar / 2f, bar / 2f, paint)
        val radius = LaserSizes.radius(h, size) * LaserSizes.PREVIEW_ZOOM
        val x = w * DOT_X
        val y = h * DOT_Y
        if (trail) drawTrail(canvas, x, y, radius)
        paint.style = Paint.Style.FILL
        paint.color = LASER_RED
        paint.alpha = GLOW_ALPHA
        canvas.drawCircle(x, y, radius * GLOW_SCALE, paint)
        paint.alpha = OPAQUE
        canvas.drawCircle(x, y, radius, paint)
        paint.color = LASER_HOT
        canvas.drawCircle(x, y, radius * HOT_SCALE, paint)
        paint.style = Paint.Style.STROKE
        paint.strokeWidth = density
        paint.color = palette.line
        box.set(0.5f * density, 0.5f * density, w - 0.5f * density, h - 0.5f * density)
        canvas.drawRoundRect(box, corner, corner, paint)
    }

    /** The recent path as short segments thickening and strengthening towards the dot, which is the head. */
    private fun drawTrail(
        canvas: Canvas,
        x: Float,
        y: Float,
        radius: Float,
    ) {
        paint.style = Paint.Style.STROKE
        paint.strokeCap = Paint.Cap.ROUND
        paint.color = LASER_RED
        val dx = -width * TRAIL_LENGTH
        val dy = height * TRAIL_LENGTH
        for (i in 0 until TRAIL_STEPS) {
            val tail = (TRAIL_STEPS - i).toFloat() / TRAIL_STEPS
            val head = (TRAIL_STEPS - i - 1).toFloat() / TRAIL_STEPS
            val age = (i + 1).toFloat() / TRAIL_STEPS
            paint.strokeWidth = 2f * radius * age
            paint.alpha = (OPAQUE * 0.9f * age).toInt()
            canvas.drawLine(x + dx * tail, y + dy * tail, x + dx * head, y + dy * head, paint)
        }
        paint.strokeCap = Paint.Cap.BUTT
    }

    private companion object {
        const val SAMPLE_SP = 7.5f
        const val BAR_DP = 4f
        const val TEXT_LEFT = 0.08f
        const val TEXT_TOP = 0.3f
        const val BAR_TOP = 0.4f
        const val BAR_LONG = 0.82f
        const val BAR_SHORT = 0.6f
        const val DOT_X = 0.7f
        const val DOT_Y = 0.55f
    }
}
