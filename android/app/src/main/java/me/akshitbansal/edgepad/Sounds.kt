package me.akshitbansal.edgepad

import android.content.Context
import android.media.AudioAttributes
import android.media.AudioManager
import android.media.SoundPool
import android.os.SystemClock

/** The rules for when a sound may play, apart from the platform, so they can be tested alone. */
object SoundRules {
    /** One dial tick at most this often, so a fast spin is a steady patter and never a buzz. */
    const val MIN_TICK_GAP_MS = 45L

    /** Sounds play only with the switch on and the ringer in normal mode: silent and vibrate always win. */
    fun allowed(
        on: Boolean,
        ringerMode: Int,
    ): Boolean = on && ringerMode == AudioManager.RINGER_MODE_NORMAL

    /** Lets one tick through per [gapMs]; [accept] is handed the time of each step. */
    class TickGate(
        private val gapMs: Long = MIN_TICK_GAP_MS,
    ) {
        private var last = NONE

        fun accept(now: Long): Boolean {
            if (last != NONE && now - last in 0 until gapMs) return false
            last = now
            return true
        }

        private companion object {
            const val NONE = Long.MIN_VALUE
        }
    }
}

/**
 * The app's sound effects, off until the Sounds switch on the Accessibility page is turned on. Taps and buttons play
 * Android's own click. Dials play a small tick sample, `res/raw/dial_tick.wav`, through a [SoundPool] that is made on
 * the first tick and released when the switch goes off, so nothing is loaded or playing while sounds are off.
 */
object Sounds {
    /** The Sounds switch as stored; [Settings] sets it as it loads, and the Accessibility page when it changes. */
    @Volatile
    var on = false

    private val gate = SoundRules.TickGate()
    private var pool: SoundPool? = null
    private var sample = 0
    private var loaded = false

    /** The platform's click for a tap or a button. */
    fun click(context: Context) {
        val audio = audio(context) ?: return
        if (SoundRules.allowed(on, audio.ringerMode)) audio.playSoundEffect(AudioManager.FX_KEYPRESS_STANDARD)
    }

    /** One tick for a dial step, thinned to [SoundRules.MIN_TICK_GAP_MS]. */
    fun tick(context: Context) {
        val audio = audio(context) ?: return
        if (!SoundRules.allowed(on, audio.ringerMode) || !gate.accept(SystemClock.uptimeMillis())) return
        // The sample loads on the first tick, which is lost; the ones after it play.
        val ready = pool ?: load(context)
        if (loaded) ready.play(sample, VOLUME, VOLUME, 1, 0, 1f)
    }

    /** The switch went off: give the pool back. */
    fun release() {
        pool?.release()
        pool = null
        loaded = false
    }

    private fun audio(context: Context): AudioManager? =
        context.applicationContext.getSystemService(AudioManager::class.java)

    private fun load(context: Context): SoundPool {
        val attributes =
            AudioAttributes
                .Builder()
                .setUsage(AudioAttributes.USAGE_ASSISTANCE_SONIFICATION)
                .setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION)
                .build()
        val made =
            SoundPool
                .Builder()
                .setMaxStreams(STREAMS)
                .setAudioAttributes(attributes)
                .build()
        made.setOnLoadCompleteListener { _, _, status -> loaded = status == 0 }
        sample = made.load(context.applicationContext, R.raw.dial_tick, 1)
        pool = made
        return made
    }

    private const val VOLUME = 0.6f
    private const val STREAMS = 2
}
