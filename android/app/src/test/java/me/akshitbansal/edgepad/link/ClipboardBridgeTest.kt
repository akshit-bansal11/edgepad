package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.link.blob.ClipboardContent
import me.akshitbansal.edgepad.protocol.BlobKind
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/** When the app may touch the clipboard, over a fake one: nothing is read or listened for while stopped. */
class ClipboardBridgeTest {
    private val device = FakeDevice()
    private val bridge = ClipboardBridge(device)
    private var changes = 0

    init {
        bridge.onChanged = { changes++ }
    }

    @Test
    fun nothingIsReadWhileStopped() {
        device.current = ClipboardContent(BlobKind.CLIP_TEXT, "x".toByteArray())

        assertNull(bridge.read())
        assertEquals(0, device.reads)
    }

    @Test
    fun readsPassThroughWhileStarted() {
        device.current = ClipboardContent(BlobKind.CLIP_TEXT, "x".toByteArray())
        bridge.start()

        assertNotNull(bridge.read())
        assertEquals(1, device.reads)
    }

    @Test
    fun stoppingStopsReading() {
        device.current = ClipboardContent(BlobKind.CLIP_TEXT, "x".toByteArray())
        bridge.start()
        bridge.stop()

        assertNull(bridge.read())
        assertEquals(0, device.reads)
    }

    @Test
    fun aClipFromTheLaptopIsWrittenWhileStoppedAndNeverRead() {
        val content = ClipboardContent(BlobKind.CLIP_TEXT, "from the laptop".toByteArray())

        bridge.write(content)

        assertEquals(listOf(content), device.writes)
        assertEquals(0, device.reads)
    }

    @Test
    fun listeningIsOnlyBetweenStartAndStop() {
        assertFalse(device.listening)
        bridge.start()
        assertTrue(device.listening)
        bridge.start()
        assertEquals(1, device.listenCalls)
        bridge.stop()
        assertFalse(device.listening)
    }

    @Test
    fun aChangeWhileStartedReachesTheSync() {
        bridge.start()

        device.change()

        assertEquals(1, changes)
    }

    @Test
    fun aChangeAfterStopReachesNothing() {
        bridge.start()
        bridge.stop()

        device.change()

        assertEquals(0, changes)
    }

    @Test
    fun theFocusReadIsOnlyAskedForWhileStarted() {
        bridge.focused()
        assertEquals(0, changes)

        bridge.start()
        bridge.focused()
        assertEquals(1, changes)
    }

    @Test
    fun settlingGoesToTheDevice() {
        bridge.settle()

        assertEquals(1, device.settles)
    }

    @Test
    fun aPngIsToldFromAnythingElse() {
        val png = byteArrayOf(-119, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0)

        assertTrue(isPngSignature(png))
        assertFalse(isPngSignature(byteArrayOf(-1, -40, -1, -32, 0, 16, 74, 70, 73, 70)))
        assertFalse(isPngSignature(png.copyOf(4)))
        assertFalse(isPngSignature(ByteArray(0)))
    }

    private class FakeDevice : ClipboardDevice {
        var current: ClipboardContent? = null
        val writes = ArrayList<ClipboardContent>()
        var reads = 0
        var settles = 0
        var listenCalls = 0
        private var callback: (() -> Unit)? = null

        val listening: Boolean get() = callback != null

        override fun read(): ClipboardContent? {
            reads++
            return current
        }

        override fun write(content: ClipboardContent) {
            writes.add(content)
        }

        override fun listen(onChanged: (() -> Unit)?) {
            listenCalls++
            callback = onChanged
        }

        override fun settle() {
            settles++
        }

        /** The platform's change notification, which only arrives while something is listening. */
        fun change() {
            callback?.invoke()
        }
    }
}
