package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.protocol.Pin
import me.akshitbansal.edgepad.protocol.TextKind
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/** What the notification that keeps the links alive names, and when there is nothing left to name. */
class LinkNoticeTest {
    private fun laptop(
        name: String,
        connected: Boolean = true,
    ): Laptop {
        val target = Target.Network(name.toByteArray().copyOf(16), name, emptyList(), 47321)
        return Laptop(target.key, name, Transport.WIFI, target).apply { link = FakeLink(connected) }
    }

    @Test
    fun noLaptopsMeansNoNotice() {
        assertNull(linkNotice(emptyList(), null))
    }

    @Test
    fun laptopsThatAreNotConnectedMeanNoNotice() {
        val trying = laptop("Desk", connected = false)
        val lost = Laptop(trying.id, "Desk", Transport.WIFI, trying.target)
        assertNull(linkNotice(listOf(trying, lost), trying))
    }

    @Test
    fun namesTheSelectedLaptopAndCountsTheOthers() {
        val desk = laptop("Desk")
        val travel = laptop("Travel")
        assertEquals(LinkNotice("Travel", 1), linkNotice(listOf(desk, travel), travel))
        assertEquals(LinkNotice("Desk", 1), linkNotice(listOf(desk, travel), desk))
    }

    @Test
    fun aSelectedLaptopWithNoLinkIsReplacedByAConnectedOne() {
        val lost = laptop("Desk", connected = false)
        val travel = laptop("Travel")
        assertEquals(LinkNotice("Travel", 0), linkNotice(listOf(lost, travel), lost))
        assertEquals(LinkNotice("Travel", 0), linkNotice(listOf(lost, travel), null))
    }

    @Test
    fun prefersTheNameTheLaptopGaveItself() {
        val desk = laptop("Desk")
        desk.state.take(Frame.Text(TextKind.DEVICE.id, "0123456789abcdef0123456789abcdef/windows/Office PC"))
        assertEquals(LinkNotice("Office PC", 0), linkNotice(listOf(desk), desk))
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
