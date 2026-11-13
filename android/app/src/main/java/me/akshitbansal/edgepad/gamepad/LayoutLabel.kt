package me.akshitbansal.edgepad.gamepad

import android.content.Context
import me.akshitbansal.edgepad.R

/**
 * The name to show for a layout. A built-in layout's English name is its id, so it is mapped to its string here;
 * a layout of the user's own shows as they typed it. "Xbox" is a brand and stays as it is.
 */
fun layoutLabel(
    context: Context,
    name: String,
): String =
    when (name) {
        GamepadLayout.XBOX_KEYS -> context.getString(R.string.gamepad_preset_xbox_keys)
        GamepadLayout.PLATFORMER -> context.getString(R.string.gamepad_preset_platformer)
        GamepadLayout.RACING -> context.getString(R.string.gamepad_preset_racing)
        GamepadLayout.SHOOTER -> context.getString(R.string.gamepad_preset_shooter)
        else -> name
    }

/** [layoutLabel] for a whole list, in the same order, for a picker that hands back the index. */
fun layoutLabels(
    context: Context,
    names: List<String>,
): List<String> = names.map { layoutLabel(context, it) }
