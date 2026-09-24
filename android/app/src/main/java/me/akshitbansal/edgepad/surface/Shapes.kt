package me.akshitbansal.edgepad.surface

import me.akshitbansal.edgepad.link.LaptopState
import kotlin.math.hypot

/**
 * What one drawn shape runs when the finger lifts.
 *
 * Deliberately wider than [GestureAction]. Two of the three targets never leave the phone, and the third
 * reaches the laptop's macro slots, which no gesture can: the owner's own example for this feature was an
 * "L" that locks the trackpad, and there is no laptop action that means that.
 */
sealed interface ShapeTarget {
    /**
     * A one-shot [GestureAction]. The four continuous ones and [GestureAction.NOTHING] are neither offered
     * nor accepted back: a drawn shape fires once, when the finger lifts, and an action whose whole meaning
     * is "one step per unit of travel" has nothing left to step along once the travel has been spent
     * drawing.
     */
    data class Run(
        val action: GestureAction,
    ) : ShapeTarget

    /**
     * The laptop's macro in [slot], sent as `ActionId.MACRO_BASE + slot`, which is exactly what the macro
     * grid sends. The phone never stores or sends what the slot launches; the laptop's own list decides.
     */
    data class Macro(
        val slot: Int,
    ) : ShapeTarget

    /**
     * The surface's own [PadMode]. Nothing crosses the link for this one — it is the phone rearranging
     * itself. Only [PadMode.FOCUS] and [PadMode.PAD_LOCKED] are ever stored: a shape bound to
     * [PadMode.NORMAL] would be a binding the list shows as working and that does nothing most of the time.
     */
    data class Pad(
        val mode: PadMode,
    ) : ShapeTarget
}

/**
 * One shape: the stroke as [Shapes] normalised it, and what it runs.
 *
 * Not a data class on purpose. [points] is a FloatArray, whose `equals` is identity, so a generated
 * `equals` would report two byte-identical shapes as different and a set as changed when it was not.
 * Nothing here needs value equality, so nothing here claims to have it.
 */
class Shape(
    val points: FloatArray,
    val target: ShapeTarget,
)

/**
 * Drawn gesture recognition — a simplified $1 unistroke recogniser — and the text the whole set is stored
 * as. The user presses a finger on the trackpad, holds it still until it ticks, draws without lifting, and
 * whatever the stroke matched runs on the lift.
 *
 * Pure: no Android type reaches this file, so the maths is tested on the JVM the way [TrackpadRecognizer]
 * and [Perimeter] are, on a machine with no emulator.
 *
 * The two numbers worth tuning are [MIN_SCORE] and [MIN_MARGIN], and they are the first thing in the file
 * for that reason. [MIN_SIZE_DP] is the third and is tuned with them.
 */
object Shapes {
    /**
     * How closely the drawn stroke has to match its template before anything runs, as a similarity in 0..1
     * where 1 is identical.
     *
     * The first of the two numbers to tune, and the one that decides how forgiving the pad feels. Set it
     * too strict and a shape has to be drawn three times before it takes, which is worse than not having
     * the feature; set it too loose and a sloppy flick that happens to lean the right way launches Chrome.
     * The default is about "drawn recognisably, not drawn carefully".
     */
    const val MIN_SCORE = 0.80f

    /**
     * How far ahead of the runner-up the best match has to be, in the same 0..1 similarity.
     *
     * The second number to tune. It is what makes an ambiguous scrawl do nothing rather than do something
     * random: with two shapes that resemble each other, a stroke sitting between them can clear [MIN_SCORE]
     * against both, and picking the higher of two near-equal numbers is a coin toss the user did not ask
     * for. Set it too high and two deliberately similar shapes can never both be used; too low and the coin
     * toss comes back.
     */
    const val MIN_MARGIN = 0.07f

    /**
     * The smallest bounding-box diagonal, in dp, that a stroke may have and still be read as a shape.
     *
     * Below it the "stroke" is a tap, a twitch, or the finger settling into the glass. Normalising one of
     * those blows a millimetre of jitter up to full size, where it matches whatever it happens to lean
     * towards, so the cheapest place to stop it is before any of the maths runs. Tuned with the two above.
     */
    const val MIN_SIZE_DP = 24f

    /** How many points every stroke is resampled to before anything compares it. */
    const val POINTS = 32

    /**
     * A raw stroke turned into the [POINTS] points everything else works on, or null when it is too small
     * to be a shape at all. [xs] and [ys] hold [count] samples in pixels; [density] is pixels per dp, and
     * is what turns [MIN_SIZE_DP] into this screen's pixels.
     *
     * Three steps of $1, and one it does that this deliberately does not:
     *
     * 1. Resample to [POINTS] points spaced equally along the path, so how fast the finger moved through
     *    each part of the stroke stops mattering and only the path is left.
     * 2. Translate so the centroid sits at the origin.
     * 3. Scale UNIFORMLY, so the longer side of the bounding box is 1. $1 itself fits the stroke into a
     *    square box, stretching each axis on its own. That is wrong here: it turns a tall thin "I" and a
     *    round "O" into the same square blob, and the aspect ratio is most of what tells one drawn letter
     *    from another. One divisor for both axes keeps it.
     *
     * What is skipped is $1's rotation to an indicative angle. Rotating both strokes to a common angle is
     * right for symbols that mean the same thing whichever way up they are, and wrong for letters: a "C"
     * turned around is not a "C", it is a backwards one, and a user who drew both meant two shapes.
     */
    fun normalise(
        xs: FloatArray,
        ys: FloatArray,
        count: Int,
        density: Float,
    ): FloatArray? {
        if (count < 2) return null
        var minX = Float.MAX_VALUE
        var maxX = -Float.MAX_VALUE
        var minY = Float.MAX_VALUE
        var maxY = -Float.MAX_VALUE
        for (i in 0 until count) {
            val x = xs[i]
            val y = ys[i]
            // A non-finite sample can only come from a corrupted store or a digitiser fault, and one of
            // them poisons every number downstream: the centroid, the box, and so every score.
            if (!x.isFinite() || !y.isFinite()) return null
            minX = minOf(minX, x)
            maxX = maxOf(maxX, x)
            minY = minOf(minY, y)
            maxY = maxOf(maxY, y)
        }
        if (hypot(maxX - minX, maxY - minY) < MIN_SIZE_DP * density) return null
        val points = resample(xs, ys, count) ?: return null
        centreAndScale(points)
        return points
    }

    /**
     * How alike two normalised strokes are, 0..1, as the mean distance between the points that face each
     * other, turned inside out. The divisor is the half-diagonal of the box both strokes were scaled into,
     * which is as far apart as two facing points can sensibly be, so a stroke scores exactly 1 against
     * itself and a stroke drawn across the box from another scores near 0.
     */
    fun score(
        a: FloatArray,
        b: FloatArray,
    ): Float {
        var total = 0f
        for (i in 0 until POINTS) {
            total += hypot(a[i * 2] - b[i * 2], a[i * 2 + 1] - b[i * 2 + 1])
        }
        return (1f - total / POINTS / HALF_DIAGONAL).coerceIn(0f, 1f)
    }

    /**
     * The shape [points] was drawn as, or null for "the user drew something, and it was not one of these".
     *
     * Two rules, and both have to hold. The best template must clear [MIN_SCORE], which is the ordinary
     * "is this even close" test. It must also beat the second best by [MIN_MARGIN], which is the rule that
     * matters: without it, a scrawl that is a mediocre match for two shapes still fires whichever of them
     * happened to score a thousandth higher, and the user cannot tell why. Silence is the right answer for
     * a stroke that was not clearly one thing.
     */
    fun match(
        points: FloatArray,
        shapes: List<Shape>,
    ): Shape? {
        var best: Shape? = null
        var bestScore = 0f
        var runnerUp = 0f
        for (shape in shapes) {
            val similarity = score(points, shape.points)
            if (similarity > bestScore) {
                runnerUp = bestScore
                bestScore = similarity
                best = shape
            } else if (similarity > runnerUp) {
                runnerUp = similarity
            }
        }
        if (bestScore < MIN_SCORE) return null
        if (bestScore - runnerUp < MIN_MARGIN) return null
        return best
    }

    /**
     * True when [points] is close enough to something already saved that the two could not be told apart.
     *
     * The editor refuses a new shape on this rather than on [match], because [match] answers null for an
     * ambiguous stroke as well — and a new shape that is ambiguous against two old ones is exactly the one
     * that must not be saved. The test here is the plain "is it close to any of them" half of the rule.
     */
    fun tooClose(
        points: FloatArray,
        shapes: List<Shape>,
    ): Boolean = shapes.any { score(points, it.points) >= MIN_SCORE }

    /**
     * One line per shape: the target's kind, the target's value, then the [POINTS] already-normalised
     * points as one comma-separated run. Normalised on the way in rather than on the way out, so opening
     * the app does no maths and a touch that becomes a stroke compares against numbers that are ready.
     */
    fun encode(shapes: List<Shape>): String =
        shapes.joinToString("\n") { shape ->
            val (kind, value) =
                when (val target = shape.target) {
                    is ShapeTarget.Run -> ACTION to target.action.name
                    is ShapeTarget.Macro -> MACRO to target.slot.toString()
                    is ShapeTarget.Pad -> PAD to target.mode.name
                }
            listOf(kind, value, shape.points.joinToString(",")).joinToString("|")
        }

    /**
     * Parses [encode]'s format. Malformed input of any kind yields null rather than the shapes that
     * happened to parse, the way the gamepad's layout does: a set that has gone bad is one the user draws
     * again, not one the trackpad silently runs half of, and the surviving half is the half nobody checked.
     */
    fun decode(text: String): List<Shape>? {
        val shapes = mutableListOf<Shape>()
        for (line in text.split("\n")) {
            if (line.isEmpty()) continue
            val fields = line.split("|")
            if (fields.size != FIELD_COUNT) return null
            val bound = target(fields[0], fields[1]) ?: return null
            val numbers = fields[2].split(",")
            if (numbers.size != POINTS * 2) return null
            val points = FloatArray(POINTS * 2)
            for (i in numbers.indices) {
                val value = numbers[i].toFloatOrNull() ?: return null
                if (!value.isFinite()) return null
                points[i] = value
            }
            shapes += Shape(points, bound)
        }
        return shapes
    }

    /** One stored target, or null for anything this vocabulary does not contain. */
    private fun target(
        kind: String,
        value: String,
    ): ShapeTarget? =
        when (kind) {
            ACTION -> {
                // The four continuous actions and NOTHING are not in the vocabulary, so a line naming one
                // is malformed rather than merely unusual, and the whole set goes with it.
                GestureAction.entries
                    .firstOrNull { it.name == value && !it.continuous && it != GestureAction.NOTHING }
                    ?.let(ShapeTarget::Run)
            }

            MACRO -> {
                // The grid the phone draws is the ceiling. A slot past it has no button to sit under and
                // would send an action id out of the reserved macro block, which means something else.
                val slot = value.toIntOrNull()
                if (slot != null && slot in 0 until LaptopState.MACRO_SLOTS) ShapeTarget.Macro(slot) else null
            }

            PAD -> {
                PadMode.entries
                    .firstOrNull { it.name == value && it != PadMode.NORMAL }
                    ?.let(ShapeTarget::Pad)
            }

            else -> {
                null
            }
        }

    /** [POINTS] points spaced equally along the path, or null when the path has no length to walk along. */
    private fun resample(
        xs: FloatArray,
        ys: FloatArray,
        count: Int,
    ): FloatArray? {
        var length = 0f
        for (i in 1 until count) length += hypot(xs[i] - xs[i - 1], ys[i] - ys[i - 1])
        if (length <= 0f) return null
        val step = length / (POINTS - 1)
        val out = FloatArray(POINTS * 2)
        out[0] = xs[0]
        out[1] = ys[0]
        var filled = 1
        var walked = 0f
        var prevX = xs[0]
        var prevY = ys[0]
        var i = 1
        // The last slot is never filled here: floating-point drift leaves the walk a hair short of the end
        // of the path often enough that the stroke's own final sample is the honest answer for it.
        while (i < count && filled < POINTS - 1) {
            val segment = hypot(xs[i] - prevX, ys[i] - prevY)
            if (segment <= 0f) {
                i++
            } else if (walked + segment >= step) {
                val t = (step - walked) / segment
                val x = prevX + t * (xs[i] - prevX)
                val y = prevY + t * (ys[i] - prevY)
                out[filled * 2] = x
                out[filled * 2 + 1] = y
                filled++
                prevX = x
                prevY = y
                walked = 0f
            } else {
                walked += segment
                prevX = xs[i]
                prevY = ys[i]
                i++
            }
        }
        while (filled < POINTS) {
            out[filled * 2] = xs[count - 1]
            out[filled * 2 + 1] = ys[count - 1]
            filled++
        }
        return out
    }

    /** Moves the centroid to the origin, then scales uniformly so the longer side of the box is 1. */
    private fun centreAndScale(points: FloatArray) {
        var sumX = 0f
        var sumY = 0f
        for (i in 0 until POINTS) {
            sumX += points[i * 2]
            sumY += points[i * 2 + 1]
        }
        val cx = sumX / POINTS
        val cy = sumY / POINTS
        var minX = Float.MAX_VALUE
        var maxX = -Float.MAX_VALUE
        var minY = Float.MAX_VALUE
        var maxY = -Float.MAX_VALUE
        for (i in 0 until POINTS) {
            val x = points[i * 2] - cx
            val y = points[i * 2 + 1] - cy
            points[i * 2] = x
            points[i * 2 + 1] = y
            minX = minOf(minX, x)
            maxX = maxOf(maxX, x)
            minY = minOf(minY, y)
            maxY = maxOf(maxY, y)
        }
        // One divisor for both axes: see [normalise] for why the aspect ratio is kept rather than fitted
        // away. A stroke with no extent in either direction was already refused by the size check.
        val side = maxOf(maxX - minX, maxY - minY)
        if (side <= 0f) return
        for (i in 0 until POINTS * 2) points[i] /= side
    }

    /**
     * Half the diagonal of the 1x1 box every stroke is scaled into, sqrt(2)/2, which is what turns a mean
     * distance into a 0..1 similarity. Written out rather than computed because a const cannot call sqrt.
     */
    private const val HALF_DIAGONAL = 0.70710678f

    /** The target kind, its value, and the points. */
    private const val FIELD_COUNT = 3
    private const val ACTION = "ACTION"
    private const val MACRO = "MACRO"
    private const val PAD = "PAD"
}
