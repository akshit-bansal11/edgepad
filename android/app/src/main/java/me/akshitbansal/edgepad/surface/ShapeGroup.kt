package me.akshitbansal.edgepad.surface

import me.akshitbansal.edgepad.R

/**
 * The sub-menu a [ShapeTarget] is filed under when a shape is given something to run. Declared in the order the
 * menu lists them. Pure, so the one place that says which target lives where is tested on the JVM.
 */
enum class ShapeGroup(
    val nameRes: Int,
) {
    MOUSE(R.string.shape_group_mouse),
    MEDIA(R.string.auto_kind_media),
    SYSTEM(R.string.widget_system),
    SCREENS(R.string.shape_group_screens),
    MACROS(R.string.macros_title),
    ;

    companion object {
        private val clicks =
            setOf(GestureAction.LEFT_CLICK, GestureAction.RIGHT_CLICK, GestureAction.MIDDLE_CLICK)
        private val transport =
            setOf(
                GestureAction.PLAY_PAUSE,
                GestureAction.NEXT_TRACK,
                GestureAction.PREVIOUS_TRACK,
                GestureAction.MUTE,
                GestureAction.MIC_MUTE,
            )

        /**
         * The group of [target]. Clicks are Mouse and the transport keys are Media. Every other one-shot is System,
         * which is also where Reset zoom goes, as it has no group of its own. The two pad modes are Screens, and
         * so is nothing else: there is no action that opens an Edgepad screen.
         */
        fun of(target: ShapeTarget): ShapeGroup =
            when (target) {
                is ShapeTarget.Macro -> {
                    MACROS
                }

                is ShapeTarget.Pad -> {
                    SCREENS
                }

                is ShapeTarget.Run -> {
                    when (target.action) {
                        in clicks -> {
                            MOUSE
                        }

                        in transport -> {
                            MEDIA
                        }

                        else -> {
                            SYSTEM
                        }
                    }
                }
            }
    }
}
