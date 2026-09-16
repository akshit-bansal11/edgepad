package me.akshitbansal.edgepad.gamepad

/**
 * The gamepad's own shape rules, shared by the layout editor and the live surface so a control looks
 * and hit-tests the same in both: how tall a shoulder button is relative to its width, how big its
 * rounded corner is, and which cells of a d-pad's 3x3 grid are its arms.
 */
internal object ControlGeometry {
    const val SHOULDER_ASPECT = 0.42f
    const val CORNER_DP = 6f
    const val DPAD_CELLS = 3f

    /** A control's half-height given its half-width: shoulders are flattened, everything else is square. */
    fun halfHeight(
        kind: ControlKind,
        halfWidth: Float,
    ): Float = if (kind == ControlKind.SHOULDER) halfWidth * SHOULDER_ASPECT else halfWidth

    /** Whether ([row], [col]) of a d-pad's 3x3 grid is one of its four arms (the centre cross, not the corners). */
    fun isArmCell(
        row: Int,
        col: Int,
    ): Boolean = row == 1 || col == 1
}
