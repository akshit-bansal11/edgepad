package me.akshitbansal.edgepad

import android.graphics.Canvas
import android.graphics.Paint

/** What [fitText] decided: the size to draw at, and the text (cut with an ellipsis only below the floor). */
data class Fitted(
    val size: Float,
    val text: String,
)

/** The smallest a fitted label shrinks to, as a share of its normal size, before it is cut short. */
const val FIT_FLOOR = 0.7f

private const val ELLIPSIS = "…"

/**
 * Fits [text] into [room]: at [size] if it does, else shrunk toward [floor] times [size], else cut with an ellipsis
 * at that floor. [measure] gives a width at a size, so the decision is pure and a test can stand in for a paint.
 */
fun fitText(
    text: String,
    room: Float,
    size: Float,
    floor: Float,
    measure: (String, Float) -> Float,
): Fitted {
    val width = measure(text, size)
    if (width <= room) return Fitted(size, text)
    val shrunk = maxOf(size * floor, size * room / width)
    if (measure(text, shrunk) <= room) return Fitted(shrunk, text)
    var keep = text.length
    while (keep > 0 && measure(text.take(keep).trimEnd() + ELLIPSIS, shrunk) > room) keep--
    return Fitted(shrunk, if (keep == 0) "" else text.take(keep).trimEnd() + ELLIPSIS)
}

/** Draws [text] with this paint inside [room] as [fitText] decides, then puts the paint's size back. */
fun Paint.drawFitted(
    canvas: Canvas,
    text: String,
    x: Float,
    y: Float,
    room: Float,
    floor: Float = FIT_FLOOR,
) {
    val normal = textSize
    val fitted =
        fitText(text, room, normal, floor) { s, size ->
            textSize = size
            measureText(s)
        }
    textSize = fitted.size
    canvas.drawText(fitted.text, x, y, this)
    textSize = normal
}
