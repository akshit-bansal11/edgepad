package me.akshitbansal.edgepad.surface

import android.content.Context
import android.hardware.Sensor
import android.hardware.SensorEvent
import android.hardware.SensorEventListener
import android.hardware.SensorManager
import android.os.Handler
import android.os.Looper
import me.akshitbansal.edgepad.protocol.Frame
import kotlin.math.abs

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
}

/**
 * Moves the laptop's pointer by tilting the phone. The gyroscope is registered on the first finger down on
 * the trackpad and released one second after the last finger up (so a tap, tap, drag does not flap the
 * sensor), and at once by [release]. No thread: events arrive on the main looper.
 */
class AirMouse(
    context: Context,
    private val send: (Frame) -> Unit,
    private val sensitivity: () -> Float,
    private val deadZone: () -> Float,
    private val landscape: () -> Boolean = { false },
) : SensorEventListener {
    private val manager = context.getSystemService(Context.SENSOR_SERVICE) as SensorManager
    private val gyro: Sensor? = manager.getDefaultSensor(Sensor.TYPE_GYROSCOPE)
    private val handler = Handler(Looper.getMainLooper())
    private val unregister = Runnable { stop() }
    private var registered = false
    private var lastNanos = 0L
    private var carryX = 0f
    private var carryY = 0f

    /** The first finger went down on the trackpad. */
    fun clutchDown() {
        handler.removeCallbacks(unregister)
        val sensor = gyro ?: return
        if (registered) return
        lastNanos = 0L
        carryX = 0f
        carryY = 0f
        registered = manager.registerListener(this, sensor, SensorManager.SENSOR_DELAY_GAME, handler)
    }

    /** The last finger came up; the sensor is let go after one second unless a finger returns. */
    fun clutchUp() {
        if (!registered) return
        handler.removeCallbacks(unregister)
        handler.postDelayed(unregister, RELEASE_DELAY_MS)
    }

    /** Lets the sensor go now: the surface left the screen or lost focus. */
    fun release() {
        handler.removeCallbacks(unregister)
        stop()
    }

    private fun stop() {
        if (!registered) return
        manager.unregisterListener(this)
        registered = false
    }

    override fun onSensorChanged(event: SensorEvent) {
        val last = lastNanos
        lastNanos = event.timestamp
        if (last == 0L) return
        val dt = (event.timestamp - last) / NANOS_PER_SECOND
        if (dt > MAX_GAP_SECONDS) return
        val x = event.values[0]
        val z = event.values[2]

        // ponytail: axis signs follow a phone held flat with the screen up; landscape is untuned (HW check).
        val horizontal = if (landscape()) -x else -z
        val vertical = if (landscape()) -z else -x
        val s = sensitivity()
        val dead = deadZone()
        val totalX = AirMath.axis(horizontal, dt, s, dead, carryX)
        val totalY = AirMath.axis(vertical, dt, s, dead, carryY)
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
    }
}
