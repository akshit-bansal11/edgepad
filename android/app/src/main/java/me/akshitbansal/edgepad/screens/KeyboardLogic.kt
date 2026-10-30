package me.akshitbansal.edgepad.screens

import me.akshitbansal.edgepad.protocol.ActionId
import me.akshitbansal.edgepad.protocol.DeviceOs

/**
 * Wire values for the keyboard's key callback. A code is itself; an action is [ACTION_BASE] plus its id; Fn is
 * [FN_CODE]. All three of the latter sit above u16, so a bug that let one reach a key frame fails the codec
 * instead of pressing a real key.
 */
const val ACTION_BASE = 0x10000
const val FN_CODE = 0x1FFFF

private val REPEATING_ACTIONS =
    setOf(ActionId.VOLUME_UP, ActionId.VOLUME_DOWN, ActionId.BRIGHTNESS_UP, ActionId.BRIGHTNESS_DOWN)

private const val CODE_ALT_LEFT = 0xA4
private const val CODE_ALT_RIGHT = 0xA5
private const val CODE_WIN_LEFT = 0x5B
private const val CODE_WIN_RIGHT = 0x5C

/**
 * The whole of Fn. A [Cap.swappable] key is flipped by [FnMode.DIRECT], so Fn (or its absence) picks the side.
 * Plain keys use [Cap.fn] when Fn is active and ignore it when they have none.
 */
fun resolve(
    cap: Cap,
    fnActive: Boolean,
    mode: FnMode,
): KeyOut {
    val useFn = fnActive != (cap.swappable && mode == FnMode.DIRECT)
    if (!useFn) return cap.base
    return cap.fn ?: if (cap.swappable) KeyOut.None else cap.base
}

/** The value the screen hands its key callback for [out] on [cap]; null when pressing it does nothing. */
fun wireOf(
    cap: Cap,
    out: KeyOut,
): Int? =
    when {
        cap.role == KeyRole.FN -> FN_CODE
        out is KeyOut.Code -> out.code
        out is KeyOut.Act -> ACTION_BASE + out.action.id
        else -> null
    }

/** Fn is swallowed, an action goes out once on down, and anything else is a key. */
fun sendWire(
    code: Int,
    down: Boolean,
    onKey: (code: Int, down: Boolean) -> Unit,
    onAction: (ActionId) -> Unit,
) {
    when {
        code == FN_CODE -> Unit
        code >= ACTION_BASE -> if (down) ActionId.entries.firstOrNull { it.id == code - ACTION_BASE }?.let(onAction)
        else -> onKey(code, down)
    }
}

/** Whether a held key repeats: not Caps or Win, and of the actions only volume and brightness. */
fun repeats(
    cap: Cap,
    out: KeyOut,
): Boolean =
    !cap.oneShot &&
        when (out) {
            is KeyOut.Code -> true
            is KeyOut.Act -> out.action in REPEATING_ACTIONS
            KeyOut.None -> false
        }

/** The legend for [cap]: the Alt and Win positions are named for the connected laptop. Codes never change. */
fun labelFor(
    cap: Cap,
    os: DeviceOs?,
): String =
    when ((cap.base as? KeyOut.Code)?.code) {
        CODE_ALT_LEFT, CODE_ALT_RIGHT -> if (os == DeviceOs.MACOS) "Option" else "Alt"
        CODE_WIN_LEFT, CODE_WIN_RIGHT ->
            when (os) {
                DeviceOs.WINDOWS -> "Win"
                DeviceOs.LINUX -> "Super"
                DeviceOs.MACOS -> "Cmd"
                null -> "Meta"
            }
        else -> cap.label
    }
