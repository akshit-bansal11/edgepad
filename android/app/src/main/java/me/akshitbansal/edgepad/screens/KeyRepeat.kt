package me.akshitbansal.edgepad.screens

/** How long a key is held before it starts repeating, as on a desktop keyboard. */
const val REPEAT_DELAY_MS = 400L

/** The gap between repeats once it has started: about thirty a second. */
const val REPEAT_EVERY_MS = 33L

/**
 * Typematic repeat for the on-screen keyboard. The laptops inject a key down and a key up and get no
 * repeat from the OS (SendInput and posted CGEvents do not repeat), so the phone supplies it: [fire] is
 * called with the held key's code once the delay has passed and then at every interval.
 *
 * Like a real keyboard, only the newest held key repeats. Releasing an older one changes nothing;
 * releasing the newest stops the repeat even if an older key is still down. Pure, with the scheduler
 * passed in ([post] runs a Runnable after a delay, [cancel] withdraws it), so it is tested on the JVM.
 */
class KeyRepeat(
    private val post: (Runnable, Long) -> Unit,
    private val cancel: (Runnable) -> Unit,
    private val fire: (Int) -> Unit,
) {
    private var current: Int? = null

    private val tick =
        object : Runnable {
            override fun run() {
                val code = current ?: return
                fire(code)
                post(this, REPEAT_EVERY_MS)
            }
        }

    fun press(code: Int) {
        cancel(tick)
        current = code
        post(tick, REPEAT_DELAY_MS)
    }

    fun release(code: Int) {
        if (current != code) return
        stop()
    }

    fun stop() {
        cancel(tick)
        current = null
    }
}
