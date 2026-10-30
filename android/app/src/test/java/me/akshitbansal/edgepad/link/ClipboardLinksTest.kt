package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.link.blob.ClipboardContent
import me.akshitbansal.edgepad.link.blob.ClipboardSurface
import me.akshitbansal.edgepad.link.blob.ClipboardSync
import me.akshitbansal.edgepad.link.blob.sha256
import me.akshitbansal.edgepad.protocol.BlobKind
import me.akshitbansal.edgepad.protocol.ControlId
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.protocol.Pin
import me.akshitbansal.edgepad.protocol.TextKind
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * Which laptop shares the clipboard, over fake links and a fake clipboard, with the sync's work run by hand so nothing
 * here sleeps. Only the selected laptop is told 1, every other one that offers it is told 0, and a copy goes to the
 * selected one alone.
 */
class ClipboardLinksTest {
    private val log = ArrayList<String>()
    private val surface = FakeSurface()
    private val pending = ArrayList<() -> Unit>()
    private var allow = true
    private var settled = 0
    private val links =
        ClipboardLinks(surface, { allow }, { settled++ }) { device, sender, allowed, ended ->
            ClipboardSync(device, sender, allowed, ended, { 1_000L }, { _, work -> pending.add(work) }) { }
        }

    private val text = "copied on the phone".toByteArray()

    private fun laptop(
        name: String,
        features: String = "clipboard",
    ): Laptop {
        val target = Target.Network(name.toByteArray().copyOf(16), name, emptyList(), 47321)
        return Laptop(target.key, name, Transport.WIFI, target).apply {
            link = FakeLink(name, log)
            state.take(Frame.Text(TextKind.FEATURES.id, features))
        }
    }

    private fun Laptop.fake() = link as FakeLink

    private fun runPending() {
        while (pending.isNotEmpty()) pending.removeAt(0)()
    }

    private fun blobsSentTo(laptop: Laptop) = laptop.fake().sent.filterIsInstance<Frame.BlobBegin>()

    @Test
    fun onlyTheSelectedLaptopIsTurnedOn() {
        val a = laptop("a")
        val b = laptop("b")

        links.refresh(listOf(a, b), a)

        assertEquals(listOf("b:0", "a:1"), log)
        assertEquals(listOf<Frame>(ControlId.CLIPBOARD.set(1)), a.fake().sent)
        assertEquals(listOf<Frame>(ControlId.CLIPBOARD.set(0)), b.fake().sent)
    }

    @Test
    fun aSelectionChangeTurnsTheOldOneOffBeforeTheNewOneOn() {
        val a = laptop("a")
        val b = laptop("b")
        links.refresh(listOf(a, b), a)
        log.clear()

        links.refresh(listOf(a, b), b)

        assertEquals(listOf("a:0", "b:1"), log)
    }

    @Test
    fun aRefreshThatChangesNothingSendsNothing() {
        val a = laptop("a")
        val b = laptop("b")
        links.refresh(listOf(a, b), a)
        log.clear()

        links.refresh(listOf(a, b), a)

        assertTrue(log.isEmpty())
    }

    @Test
    fun aLaptopThatDidNotOfferTheClipboardIsLeftAlone() {
        val plain = laptop("plain", features = "capture")

        links.refresh(listOf(plain), plain)

        assertTrue(plain.fake().sent.isEmpty())
        assertNull(plain.fake().blobs)
    }

    @Test
    fun theOwnersToggleOffTurnsEveryLaptopOff() {
        val a = laptop("a")
        links.refresh(listOf(a), a)
        log.clear()
        allow = false

        links.refresh(listOf(a), a)

        assertEquals(listOf("a:0"), log)
    }

    @Test
    fun theToggleOffNeverTurnsAnyoneOn() {
        val a = laptop("a")
        allow = false

        links.refresh(listOf(a), a)

        assertEquals(listOf("a:0"), log)
    }

    @Test
    fun aCopyGoesToTheSelectedLaptopAlone() {
        val a = laptop("a")
        val b = laptop("b")
        links.refresh(listOf(a, b), a)
        surface.current = ClipboardContent(BlobKind.CLIP_TEXT, text)

        links.localChanged()
        runPending()

        assertEquals(1, blobsSentTo(a).size)
        assertTrue(blobsSentTo(b).isEmpty())
    }

    @Test
    fun aSelectionChangePushesNothingAndTheNextCopyGoesToTheNewLaptop() {
        val a = laptop("a")
        val b = laptop("b")
        surface.current = ClipboardContent(BlobKind.CLIP_TEXT, text)
        links.refresh(listOf(a, b), a)
        val before = settled

        links.refresh(listOf(a, b), b)
        runPending()

        assertEquals(before + 1, settled)
        assertTrue(blobsSentTo(a).isEmpty())
        assertTrue(blobsSentTo(b).isEmpty())
        surface.current = ClipboardContent(BlobKind.CLIP_TEXT, "later".toByteArray())
        links.localChanged()
        runPending()
        assertEquals(1, blobsSentTo(b).size)
        assertTrue(blobsSentTo(a).isEmpty())
    }

    @Test
    fun aSensitiveCopyNeverLeaves() {
        val a = laptop("a")
        links.refresh(listOf(a), a)
        surface.current = ClipboardContent(BlobKind.CLIP_TEXT, "hunter2".toByteArray(), sensitive = true)

        links.localChanged()
        runPending()

        assertTrue(a.fake().sent.none { it is Frame.BlobBegin || it is Frame.BlobChunk || it is Frame.BlobEnd })
    }

    @Test
    fun aBlobFromTheSelectedLaptopIsWritten() {
        val a = laptop("a")
        links.refresh(listOf(a), a)

        deliver(a, text)
        runPending()

        assertArrayEquals(text, surface.writes.single().data)
    }

    @Test
    fun aBlobFromALaptopThatIsNotSelectedIsRefused() {
        val a = laptop("a")
        val b = laptop("b")
        links.refresh(listOf(a, b), a)

        deliver(b, text)
        runPending()

        assertTrue(surface.writes.isEmpty())
    }

    @Test
    fun aBlobAfterTheSelectionMovedIsRefusedFromTheOldLaptop() {
        val a = laptop("a")
        val b = laptop("b")
        links.refresh(listOf(a, b), a)
        links.refresh(listOf(a, b), b)

        deliver(a, text)
        runPending()

        assertTrue(surface.writes.isEmpty())
        deliver(b, text)
        runPending()
        assertEquals(1, surface.writes.size)
    }

    @Test
    fun aLinkThatClosedIsLetGo() {
        val a = laptop("a")
        links.refresh(listOf(a), a)
        assertNotNull(a.fake().blobs)
        val old = a.fake()

        old.connected = false
        links.refresh(listOf(a), a)

        assertNull(old.blobs)
    }

    @Test
    fun aReconnectedLaptopIsTurnedOnAgain() {
        val a = laptop("a")
        links.refresh(listOf(a), a)
        a.fake().connected = false
        links.refresh(listOf(a), a)
        log.clear()

        a.link = FakeLink("a", log)
        a.state.take(Frame.Text(TextKind.FEATURES.id, "clipboard"))
        links.refresh(listOf(a), a)

        assertEquals(listOf("a:1"), log)
        assertNotNull(a.fake().blobs)
    }

    // What a laptop sends to put text on the phone's clipboard, through the link's blob sink.
    private fun deliver(
        laptop: Laptop,
        data: ByteArray,
    ) {
        val sink = laptop.fake().blobs ?: return
        sink(Frame.BlobBegin(BlobKind.CLIP_TEXT.id, data.size.toLong(), ""))
        sink(Frame.BlobChunk(0, data))
        sink(Frame.BlobEnd(sha256(data)))
    }

    private class FakeSurface : ClipboardSurface {
        var current: ClipboardContent? = null
        val writes = ArrayList<ClipboardContent>()

        override fun read(): ClipboardContent? = current

        override fun write(content: ClipboardContent) {
            writes.add(content)
        }
    }

    private class FakeLink(
        private val name: String,
        private val log: MutableList<String>,
    ) : ManagedLink {
        val sent = ArrayList<Frame>()
        override val id: Any = Any()
        override var connected = true
        override val wasConnected = true
        override var iconPx = 128
        override var blobs: ((Frame) -> Unit)? = null
        override val queued = 0
        override val lastInputMs = 0L

        override fun start() = Unit

        override fun send(frame: Frame) {
            sent.add(frame)
            if (frame is Frame.SetValue) log.add("$name:${frame.value}")
        }

        override fun answerPin(pin: Pin) = Unit

        override fun answerPairing(accepted: Boolean) = Unit

        override fun close(closure: LaptopLink.Closure) = Unit
    }
}
