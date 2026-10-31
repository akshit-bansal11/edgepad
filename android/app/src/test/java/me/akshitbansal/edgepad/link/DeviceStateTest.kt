package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.protocol.Pin
import org.junit.Assert.assertEquals
import org.junit.Test

/** What each laptop's row in System settings says, and what tapping it does. */
class DeviceStateTest {
    private fun laptop(
        name: String,
        connected: Boolean? = null,
    ): Laptop {
        val target = Target.Network(name.toByteArray().copyOf(16), name, emptyList(), 47321)
        return Laptop(target.key, name, Transport.WIFI, target).apply {
            if (connected != null) link = FakeLink(connected)
        }
    }

    private fun rowOf(
        laptop: Laptop,
        saved: Boolean = false,
    ): LaptopRow {
        val remembered = if (saved) listOf(SavedLaptop(laptop.id, Transport.WIFI, laptop.name)) else emptyList()
        return laptopRows(remembered, listOf(laptop), emptyList(), emptyList()).single()
    }

    @Test
    fun theSelectedConnectedLaptopIsActive() {
        val desk = laptop("Desk", connected = true)
        assertEquals(DeviceState.ACTIVE, deviceState(rowOf(desk), desk))
    }

    @Test
    fun aConnectedLaptopThatIsNotSelectedIsOnlyConnected() {
        val desk = laptop("Desk", connected = true)
        val travel = laptop("Travel", connected = true)
        assertEquals(DeviceState.CONNECTED, deviceState(rowOf(travel), desk))
        assertEquals(DeviceState.CONNECTED, deviceState(rowOf(travel), null))
    }

    @Test
    fun aSelectedLaptopWithNoLinkIsNotConnected() {
        val desk = laptop("Desk")
        desk.failed = true
        assertEquals(DeviceState.NOT_CONNECTED, deviceState(rowOf(desk), desk))
    }

    @Test
    fun aRememberedLaptopWithNothingGoingOnIsNotConnected() {
        val desk = laptop("Desk")
        assertEquals(DeviceState.NOT_CONNECTED, deviceState(rowOf(desk, saved = true), null))
    }

    @Test
    fun anAttemptUnderWayIsConnecting() {
        val trying = laptop("Desk", connected = false)
        assertEquals(DeviceState.CONNECTING, deviceState(rowOf(trying), trying))
        trying.awaiting = true
        assertEquals(DeviceState.CONNECTING, deviceState(rowOf(trying), trying))
    }

    @Test
    fun aLostLaptopBeingRetriedIsConnecting() {
        val lost = laptop("Desk", connected = false)
        lost.reconnecting = true
        assertEquals(DeviceState.CONNECTING, deviceState(rowOf(lost), null))
    }

    @Test
    fun aLaptopWaitingOnAPinIsAsking() {
        val asking = laptop("Desk", connected = false)
        asking.pinAsked = true
        assertEquals(DeviceState.ASKING, deviceState(rowOf(asking), null))
    }

    @Test
    fun aTapDoesWhatTheStateCalls() {
        assertEquals(DeviceTap.NOTHING, DeviceState.ACTIVE.tap())
        assertEquals(DeviceTap.SELECT, DeviceState.CONNECTED.tap())
        assertEquals(DeviceTap.NOTHING, DeviceState.CONNECTING.tap())
        assertEquals(DeviceTap.ANSWER, DeviceState.ASKING.tap())
        assertEquals(DeviceTap.CONNECT, DeviceState.NOT_CONNECTED.tap())
    }

    private class FakeLink(
        override val connected: Boolean,
    ) : ManagedLink {
        override val id: Any = Any()
        override val wasConnected = true
        override var iconPx = 128
        override var blobs: ((Frame) -> Unit)? = null
        override val queued = 0
        override val lastInputMs = 0L

        override fun start() = Unit

        override fun send(frame: Frame) = Unit

        override fun answerPin(pin: Pin) = Unit

        override fun answerPairing(accepted: Boolean) = Unit

        override fun close(closure: LaptopLink.Closure) = Unit
    }
}
