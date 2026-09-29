package me.akshitbansal.edgepad.gamepad

/**
 * The gamepad's own shape rules, shared by the layout editor and the live surface so a control looks
 * and hit-tests the same in both: how tall a shoulder button is relative to its width, how big its
 * rounded corner is (also the most a d-pad arm's outer corners are rounded), and how many cells a d-pad's
 * grid has on a side.
 */
internal object ControlGeometry {
    const val SHOULDER_ASPECT = 0.42f
    const val CORNER_DP = 10f
    const val DPAD_CELLS = 3f

    /** A control's half-height given its half-width: shoulders are flattened, everything else is square. */
    fun halfHeight(
        kind: ControlKind,
        halfWidth: Float,
    ): Float = if (kind == ControlKind.SHOULDER) halfWidth * SHOULDER_ASPECT else halfWidth
}
