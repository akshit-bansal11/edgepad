package me.akshitbansal.edgepad.screens

import android.content.Context
import android.graphics.Canvas
import android.graphics.drawable.Drawable
import android.view.View
import me.akshitbansal.edgepad.R

/** A Lucide icon for the plain screens, tinted and centred in its view. */
class Glyph(
    context: Context,
    shape: Shape,
    color: Int,
) : View(context) {
    /** Android lint requires a (Context) constructor on every custom View; nothing inflates this one. */
    constructor(context: Context) : this(context, Shape.CHEVRON_RIGHT, 0)

    enum class Shape(
        val icon: Int,
    ) {
        CHEVRON_RIGHT(R.drawable.ic_chevron_right),
        CHEVRON_LEFT(R.drawable.ic_chevron_left),
        GEAR(R.drawable.ic_settings),
        REFRESH(R.drawable.ic_refresh_cw),
        BLUETOOTH(R.drawable.ic_bluetooth),
    }

    private val density = resources.displayMetrics.density
    private val icon: Drawable? = context.getDrawable(shape.icon)?.mutate()?.apply { setTint(color) }

    override fun onSizeChanged(
        w: Int,
        h: Int,
        oldw: Int,
        oldh: Int,
    ) {
        super.onSizeChanged(w, h, oldw, oldh)
        val half = (ICON_DP * density / 2).toInt()
        icon?.setBounds(w / 2 - half, h / 2 - half, w / 2 + half, h / 2 + half)
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        icon?.draw(canvas)
    }

    private companion object {
        const val ICON_DP = 20f
    }
}
