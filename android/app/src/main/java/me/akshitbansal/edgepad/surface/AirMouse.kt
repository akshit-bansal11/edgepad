package me.akshitbansal.edgepad.surface

import android.content.Context
import android.hardware.Sensor
import android.hardware.SensorEvent
import android.hardware.SensorEventListener
import android.hardware.SensorManager
import android.os.Handler
import android.os.Looper
import android.view.Surface
import me.akshitbansal.edgepad.protocol.Frame
import kotlin.math.abs
import kotlin.math.sqrt

/**
 * The air mouse's arithmetic, apart from the sensor. A gyroscope gives a rate, so there is no angle to
 * recentre: drift is handled by [deadZone] and by only reading while a finger rests on the trackpad.
 */
object AirMath {
    /** Pixels the pointer travels per radian of turn at sensitivity 1. A calibration knob, tune on a device. */
    const val PIXELS_PER_RADIAN = 600f

    /** The widest step one event may send, which is what the Move frame's signed 16-bit fields hold. */
    const val MAX_STEP = 32767

    /**
     * The travel in pixels for one axis, [carry] (the fraction left over from the last event) included. A rate
     * whose size is under [deadZone] counts as zero, so a phone lying still sends nothing.
     */
    fun axis(
        rate: Float,
        dtSeconds: Float,
        sensitivity: Float,
        deadZone: Float,
        carry: Float,
    ): Float {
        if (dtSeconds <= 0f || abs(rate) < deadZone) return 0f
        return rate * dtSeconds * sensitivity * PIXELS_PER_RADIAN + carry
    }

    /** The whole pixels of [total], clamped to the codec's range; the caller keeps `total - whole` as the carry. */
    fun whole(total: Float): Int = total.toInt().coerceIn(-MAX_STEP, MAX_STEP)

    /**
     * The pointer's two rates, in radians per second, written to [out] as (right, down), from the gyroscope's
     * rates ([gx], [gy], [gz], device axes) and the up direction ([ux], [uy], [uz], from gravity).
     *
     * Right is the turn about true vertical, so turning the phone left or right moves the pointer the same way
     * whether the phone lies flat, is held tilted like a remote, or stands upright (owner, 2026-11-18: only flat
     * worked). Down is the tilt of the screen's top edge about the screen's own left-right axis, which depends on
     * how the screen is turned: [rotation] is a [Surface] rotation.
     */
    fun rates(
        gx: Float,
        gy: Float,
        gz: Float,
        ux: Float,
        uy: Float,
        uz: Float,
        rotation: Int,
        out: FloatArray,
    ) {
        val length = sqrt(ux * ux + uy * uy + uz * uz)
        // Clockwise seen from above is a negative turn about up, and moves the pointer right.
        out[0] = if (length < MIN_GRAVITY) 0f else -(gx * ux + gy * uy + gz * uz) / length
        // The screen's top edge tipping up is a positive turn about the screen's right-hand axis: pointer up.
        out[1] =
            when (rotation) {
                Surface.ROTATION_90 -> gy
                Surface.ROTATION_180 -> gx
                Surface.ROTATION_270 -> -gy
                else -> -gx
            }
    }

    /** Below this (m/s²) the gravity reading is not trusted to say which way is up: free fall or no reading yet. */
    private const val MIN_GRAVITY = 1f
}

/**
 * Moves the laptop's pointer by turning and tilting the phone while a finger rests on the trackpad. The
 * gyroscope and gravity sensors are registered on the first finger down and released one second after the
 * last finger up (so a tap, tap, rest does not flap them), and at once by [release]. Only a resting finger
 * moves the pointer: after it lifts the sensors stay registered, but nothing is sent. No thread: events
 * arrive on the main looper.
 */
class AirMouse(
    context: Context,
    private val send: (Frame) -> Unit,
    private val sensitivity: () -> Float,
    private val deadZone: () -> Float,
    private val rotation: () -> Int = { Surface.ROTATION_0 },
) : SensorEventListener {
    private val manager = context.getSystemService(Context.SENSOR_SERVICE) as SensorManager
    private val gyro: Sensor? = manager.getDefaultSensor(Sensor.TYPE_GYROSCOPE)

    /** False on a phone with no gyroscope, where the air mouse can do nothing. */
    val works: Boolean get() = gyro != null

    // Fused gravity where the phone has it; the raw accelerometer, smoothed below, where it does not.
    private val gravity: Sensor? =
        manager.getDefaultSensor(Sensor.TYPE_GRAVITY) ?: manager.getDefaultSensor(Sensor.TYPE_ACCELEROMETER)
    private val handler = Handler(Looper.getMainLooper())
    private val unregister = Runnable { stop() }
    private var registered = false
    private var clutched = false
    private var lastNanos = 0L
    private var carryX = 0f
    private var carryY = 0f
    private val up = FloatArray(AXES)
    private val rate = FloatArray(2)

    /** The first finger went down on the trackpad. */
    fun clutchDown() {
        handler.removeCallbacks(unregister)
        clutched = true
        lastNanos = 0L
        carryX = 0f
        carryY = 0f
        val sensor = gyro ?: return
        if (registered) return
        // Flat, screen up, until the first gravity reading says otherwise.
        up[0] = 0f
        up[1] = 0f
        up[2] = SensorManager.GRAVITY_EARTH
        registered = manager.registerListener(this, sensor, SensorManager.SENSOR_DELAY_GAME, handler)
        gravity?.let { manager.registerListener(this, it, SensorManager.SENSOR_DELAY_GAME, handler) }
    }

    /** The last finger came up: the pointer stops now; the sensors are let go after one second. */
    fun clutchUp() {
        clutched = false
        if (!registered) return
        handler.removeCallbacks(unregister)
        handler.postDelayed(unregister, RELEASE_DELAY_MS)
    }

    /** Lets the sensors go now: the surface left the screen or lost focus. */
    fun release() {
        clutched = false
        handler.removeCallbacks(unregister)
        stop()
    }

    private fun stop() {
        if (!registered) return
        manager.unregisterListener(this)
        registered = false
    }

    override fun onSensorChanged(event: SensorEvent) {
        if (event.sensor.type != Sensor.TYPE_GYROSCOPE) {
            // The accelerometer carries the hand's own movement as well; a low-pass keeps mostly gravity.
            val keep = if (event.sensor.type == Sensor.TYPE_GRAVITY) 0f else SMOOTHING
            for (i in 0 until AXES) up[i] = up[i] * keep + event.values[i] * (1f - keep)
            return
        }
        val last = lastNanos
        lastNanos = event.timestamp
        if (!clutched || last == 0L) return
        val dt = (event.timestamp - last) / NANOS_PER_SECOND
        if (dt > MAX_GAP_SECONDS) return
        AirMath.rates(event.values[0], event.values[1], event.values[2], up[0], up[1], up[2], rotation(), rate)
        val s = sensitivity()
        val dead = deadZone()
        val totalX = AirMath.axis(rate[0], dt, s, dead, carryX)
        val totalY = AirMath.axis(rate[1], dt, s, dead, carryY)
        val dx = AirMath.whole(totalX)
        val dy = AirMath.whole(totalY)
        carryX = totalX - dx
        carryY = totalY - dy
        if (dx != 0 || dy != 0) send(Frame.Move(dx, dy))
    }

    override fun onAccuracyChanged(
        sensor: Sensor,
        accuracy: Int,
    ) = Unit

    private companion object {
        const val RELEASE_DELAY_MS = 1000L
        const val NANOS_PER_SECOND = 1_000_000_000f
        const val MAX_GAP_SECONDS = 0.25f
        const val AXES = 3
        const val SMOOTHING = 0.9f
    }
}
