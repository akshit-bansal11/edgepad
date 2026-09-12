package me.akshitbansal.edgepad.screens

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Paint
import android.view.View
import com.caverock.androidsvg.SVG
import com.caverock.androidsvg.SVGParseException
import me.akshitbansal.edgepad.Palette
import me.akshitbansal.edgepad.R

/**
 * The Edgepad mark, from the SVG made for the current theme (a dark tile on the light theme, a light
 * tile on the dark one). Rendered once per size into a bitmap, so drawing allocates nothing.
 */
class MarkView(
    context: Context,
) : View(context) {
    private val resource = if (Palette.of(context).dark) R.raw.mark_dark else R.raw.mark_light
    private val smooth = Paint(Paint.FILTER_BITMAP_FLAG or Paint.ANTI_ALIAS_FLAG)
    private var bitmap: Bitmap? = null

    init {
        contentDescription = context.getString(R.string.app_name)
    }

    override fun onSizeChanged(
        w: Int,
        h: Int,
        oldw: Int,
        oldh: Int,
    ) {
        super.onSizeChanged(w, h, oldw, oldh)
        bitmap =
            try {
                val svg = SVG.getFromResource(resources, resource)
                svg.setDocumentWidth("100%")
                svg.setDocumentHeight("100%")
                Bitmap.createBitmap(w, h, Bitmap.Config.ARGB_8888).also { svg.renderToCanvas(Canvas(it)) }
            } catch (e: SVGParseException) {
                null
            }
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        bitmap?.let { canvas.drawBitmap(it, 0f, 0f, smooth) }
    }
}
