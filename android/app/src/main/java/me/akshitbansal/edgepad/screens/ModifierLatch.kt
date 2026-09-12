package me.akshitbansal.edgepad.screens

/**
 * The on-screen keyboard's modifiers (Shift, Ctrl, Alt, Menu). Held under a finger, one is down for as
 * long as the finger is, like a real key. Tapped alone, it is pressed and released at once, and armed:
 * the next ordinary key goes out wrapped in it. Tapping an armed modifier again disarms it. Pure, so it
 * is tested on the JVM; [send] takes a Windows virtual-key code and whether it went down.
 */
class ModifierLatch(
    private val send: (code: Int, down: Boolean) -> Unit,
) {
    private val held = LinkedHashSet<Int>()
    private val armed = LinkedHashSet<Int>()
    private var usedWhileHeld = false

    fun isArmed(code: Int): Boolean = code in armed

    fun modifierDown(code: Int) {
        if (armed.remove(code)) return
        if (held.add(code)) {
            usedWhileHeld = false
            send(code, true)
        }
    }

    fun modifierUp(code: Int) {
        if (!held.remove(code)) return
        send(code, false)
        if (!usedWhileHeld) armed.add(code)
    }

    fun keyDown(code: Int) {
        usedWhileHeld = true
        for (modifier in armed) send(modifier, true)
        send(code, true)
    }

    fun keyUp(code: Int) {
        send(code, false)
        for (modifier in armed.reversed()) send(modifier, false)
        armed.clear()
    }

    /** Lets go of everything: what a finger holds is released, and nothing stays armed. */
    fun cancel() {
        for (modifier in held) send(modifier, false)
        held.clear()
        armed.clear()
    }
}
