package me.akshitbansal.edgepad.surface

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import kotlin.math.PI
import kotlin.math.cos
import kotlin.math.sin

/** The $1 recogniser and the text a set of shapes is stored as, at density 1 so dp and px are the same. */
class ShapesTest {
    private val play = ShapeTarget.Run(GestureAction.PLAY_PAUSE)

    private fun stroke(points: List<Pair<Float, Float>>): FloatArray? =
        Shapes.normalise(
            points.map { it.first }.toFloatArray(),
            points.map { it.second }.toFloatArray(),
            points.size,
            density = 1f,
        )

    private fun shape(points: List<Pair<Float, Float>>): Shape = Shape(stroke(points)!!, play)

    /** A straight run right, as [samples] evenly spaced points, climbing [rise] pixels over its length. */
    private fun line(
        length: Float,
        samples: Int,
        rise: Float = 0f,
    ): List<Pair<Float, Float>> =
        (0 until samples).map { i ->
            val t = i.toFloat() / (samples - 1)
            length * t to rise * t
        }

    private fun circle(radius: Float): List<Pair<Float, Float>> =
        (0 until SAMPLES).map { i ->
            val angle = 2 * PI * i / (SAMPLES - 1)
            (radius * cos(angle)).toFloat() to (radius * sin(angle)).toFloat()
        }

    @Test
    fun aStrokeIsResampledToThirtyTwoPointsWhateverNumberOfSamplesWentIntoIt() {
        assertEquals(Shapes.POINTS * 2, stroke(line(200f, 4))!!.size)
        assertEquals(Shapes.POINTS * 2, stroke(line(200f, 300))!!.size)
    }

    @Test
    fun theSamePathReportedInFourSamplesAndInThreeHundredIsTheSameStroke() {
        // Resampling is what makes how fast the finger moved stop mattering. If this does not hold, every
        // score after it is measuring the digitiser's reporting rate rather than the shape.
        val few = stroke(line(200f, 4))!!
        val many = stroke(line(200f, 300))!!
        assertTrue("same path, different sampling: ${Shapes.score(few, many)}", Shapes.score(few, many) > 0.99f)
    }

    @Test
    fun aShapeMatchesItselfAndScoresExactlyOne() {
        val drawn = stroke(circle(120f))!!
        val saved = shape(circle(120f))
        assertEquals(1f, Shapes.score(drawn, saved.points), TOLERANCE)
        assertEquals(saved, Shapes.match(drawn, listOf(saved)))
    }

    @Test
    fun aCircleDrawnOverASavedLineRunsNothing() {
        val drawn = stroke(circle(120f))!!
        val saved = shape(line(200f, 40))
        assertTrue("a circle should look nothing like a line", Shapes.score(drawn, saved.points) < Shapes.MIN_SCORE)
        assertNull(Shapes.match(drawn, listOf(saved)))
    }

    @Test
    fun aStrokeThatTwoSavedShapesBothFitLeavesTheMarginUnmetAndRunsNothing() {
        // Two lines a couple of degrees apart: either is a fine answer for a stroke drawn along either, and
        // the one that happens to score a hundredth higher must not win the toss on the user's behalf.
        val flat = shape(line(200f, 40))
        val tilted = shape(line(200f, 40, rise = 8f))
        val drawn = stroke(line(200f, 40))!!
        assertTrue("both clear the bar", Shapes.score(drawn, tilted.points) >= Shapes.MIN_SCORE)
        assertNull("too alike to choose between", Shapes.match(drawn, listOf(flat, tilted)))
        assertTrue("and too alike to be saved as a second shape", Shapes.tooClose(drawn, listOf(tilted)))
    }

    @Test
    fun aStrokeSmallerThanTheMinimumIsATapOrATwitchRatherThanAShape() {
        // The same diagonal drawn twice. Nine pixels of it is a finger settling into the glass; ninety is a
        // shape. Only the size differs between them, so only the size can be what refuses the first.
        assertNull(stroke((0 until 10).map { i -> i.toFloat() to i.toFloat() }))
        assertNotNull(stroke((0 until 10).map { i -> i * 10f to i * 10f }))
    }

    @Test
    fun aSetOfShapesSurvivesBeingWrittenOutAndReadBackIn() {
        val saved =
            listOf(
                Shape(stroke(circle(120f))!!, ShapeTarget.Run(GestureAction.PLAY_PAUSE)),
                Shape(stroke(line(200f, 40))!!, ShapeTarget.Macro(3)),
                Shape(stroke(line(200f, 40, rise = 400f))!!, ShapeTarget.Pad(PadMode.PAD_LOCKED)),
            )
        val read = Shapes.decode(Shapes.encode(saved))
        assertNotNull(read)
        assertEquals(saved.map { it.target }, read!!.map { it.target })
        for (i in saved.indices) {
            assertEquals("shape $i", 1f, Shapes.score(saved[i].points, read[i].points), TOLERANCE)
        }
    }

    @Test
    fun anEmptyStoreIsNoShapesRatherThanAFailureToReadOne() {
        assertEquals(emptyList<Shape>(), Shapes.decode(""))
    }

    @Test
    fun malformedTextIsNoShapesAtAllRatherThanTheOnesThatHappenedToParse() {
        val good = Shapes.encode(listOf(shape(circle(120f))))
        val points = good.substringAfterLast('|')
        val notNumbers = List(Shapes.POINTS * 2) { "x" }.joinToString(",")
        assertNull("a missing field", Shapes.decode("ACTION|PLAY_PAUSE"))
        assertNull("an unknown kind", Shapes.decode("WHATEVER|PLAY_PAUSE|$points"))
        assertNull("an unknown action", Shapes.decode("ACTION|EJECT_THE_DISC|$points"))
        assertNull("a continuous action has no meaning for one stroke", Shapes.decode("ACTION|VOLUME|$points"))
        assertNull("nor has NOTHING", Shapes.decode("ACTION|NOTHING|$points"))
        assertNull("a macro slot past the grid", Shapes.decode("MACRO|99|$points"))
        assertNull("a macro slot that is not a number", Shapes.decode("MACRO|three|$points"))
        assertNull("a pad mode that changes nothing", Shapes.decode("PAD|NORMAL|$points"))
        assertNull("too few points", Shapes.decode("ACTION|PLAY_PAUSE|1,2,3"))
        assertNull("points that are not numbers", Shapes.decode("ACTION|PLAY_PAUSE|$notNumbers"))
        // And one bad line takes the good one with it: the half that survives is the half nobody checked.
        assertNull("one bad line out of two", Shapes.decode("$good\nACTION|NOTHING|$points"))
    }

    private companion object {
        /** Round trips through Float.toString are exact; the slack is against the resampling arithmetic. */
        const val TOLERANCE = 1e-4f

        /** How many raw samples a test circle is drawn with, before anything resamples it. */
        const val SAMPLES = 40
    }
}
