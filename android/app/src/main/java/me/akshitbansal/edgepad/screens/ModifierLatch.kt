package me.akshitbansal.edgepad.screens

/**
 * The on-screen keyboard's modifiers (Shift, Ctrl, Alt, Menu). Held under a finger, one is down for as
 * long as the finger is, like a real key. Tapped alone, it is pressed and released at once, and armed:
 * the next ordinary key goes out wrapped in it, and the wrapper stays down until that key is released or
 * cancelled. Tapping an armed modifier again disarms it. Pure, so it
 * is tested on the JVM; [send] takes a Windows virtual-key code and whether it went down.
 */
class ModifierLatch(
    private val send: (code: Int, down: Boolean) -> Unit,
) {
    private val held = LinkedHashSet<Int>()
    private val armed = LinkedHashSet<Int>()
    private val pressed = LinkedHashSet<Int>()
    private val usedWhileHeld = HashSet<Int>()
    private val fingers = HashMap<Int, Int>()

    fun isArmed(code: Int): Boolean = code in armed

    /** Held under a finger or armed for the next key. */
    fun isActive(code: Int): Boolean = code in held || code in armed

    // Two fingers on one code (the two Fn keys, Enter and numpad Enter) are one press: down on 0 to 1, up on 1 to 0.
    private fun fingerDown(code: Int): Boolean {
        val count = (fingers[code] ?: 0) + 1
        fingers[code] = count
        return count == 1
    }

    private fun fingerUp(code: Int): Boolean {
        val count = (fingers[code] ?: 0) - 1
        if (count > 0) {
            fingers[code] = count
            return false
        }
        fingers.remove(code)
        return true
    }

    fun modifierDown(code: Int) {
        if (!fingerDown(code)) return
        if (armed.remove(code)) return
        if (held.add(code)) {
            usedWhileHeld.remove(code)
            send(code, true)
        }
    }

    fun modifierUp(code: Int) {
        if (!fingerUp(code)) return
        if (!held.remove(code)) return
        send(code, false)
        if (!usedWhileHeld.remove(code)) armed.add(code)
    }

    fun keyDown(code: Int) {
        if (!fingerDown(code)) return
        usedWhileHeld.addAll(held)
        for (modifier in armed) if (pressed.add(modifier)) send(modifier, true)
        send(code, true)
    }

    fun keyUp(code: Int) {
        if (!fingerUp(code)) return
        send(code, false)
        releasePressed()
        armed.clear()
    }

    /** Lets go of everything: what a finger holds, and what a key pressed along with it. Nothing stays armed. */
    fun cancel() {
        for (modifier in held) send(modifier, false)
        held.clear()
        usedWhileHeld.clear()
        fingers.clear()
        releasePressed()
        armed.clear()
    }

    private fun releasePressed() {
        // asReversed, not reversed: on Java 21 the latter resolves to the new SequencedSet member, absent on 17.
        for (modifier in pressed.toList().asReversed()) send(modifier, false)
        pressed.clear()
    }
}
