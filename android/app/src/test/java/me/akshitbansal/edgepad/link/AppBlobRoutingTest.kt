package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.link.blob.ClipboardContent
import me.akshitbansal.edgepad.link.blob.ClipboardSurface
import me.akshitbansal.edgepad.link.blob.ClipboardSync
import me.akshitbansal.edgepad.link.blob.sha256
import me.akshitbansal.edgepad.protocol.BlobKind
import me.akshitbansal.edgepad.protocol.Frame
import me.akshitbansal.edgepad.protocol.FrameCodec
import me.akshitbansal.edgepad.protocol.Pin
import me.akshitbansal.edgepad.protocol.TextKind
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * A link has one blob sink, and the clipboard and the app list both take blobs through it: a blob goes to the lane its
 * kind belongs to, and everything else is dropped. Over fake links, with the main thread's work run by hand, so nothing
 * here sleeps.
 */
class AppBlobRoutingTest {
    private val routes = BlobRoutes()
    private val posted = ArrayList<() -> Unit>()
    private var postNow = true
    private val apps =
        AppLinks(routes) { work -> if (postNow) work() else posted.add(work) }
    private val told = ArrayList<Pair<Laptop, String?>>()

    init {
        apps.events = AppLinks.Events { laptop, token -> told.add(laptop to token) }
    }

    private fun laptop(
        name: String = "a",
        features: String = "apps",
    ): Laptop {
        val target = Target.Network(name.toByteArray().copyOf(16), name, emptyList(), 47321)
        return Laptop(target.key, name, Transport.WIFI, target).apply {
            link = FakeLink()
            state.take(Frame.Text(TextKind.FEATURES.id, features))
        }
    }

    private fun Laptop.fake() = link as FakeLink

    private fun names(laptop: Laptop): List<String> {
        val catalog = laptop.state.apps
        return catalog.apps.map { it.name }
    }

    private fun list(vararg rows: String): ByteArray = rows.joinToString("\n", postfix = "\n").toByteArray()

    private fun send(
        sink: ((Frame) -> Unit)?,
        frames: List<Frame>,
    ) {
        val take = sink ?: return
        frames.forEach(take)
    }

    private fun framesOf(
        kind: Int,
        label: String,
        data: ByteArray,
    ): List<Frame> =
        listOf<Frame>(Frame.BlobBegin(kind, data.size.toLong(), label)) +
            data.asList().chunked(FrameCodec.MAX_CHUNK_BYTES).mapIndexed { at, piece ->
                Frame.BlobChunk(at and SEQ_MASK, piece.toByteArray())
            } +
            Frame.BlobEnd(sha256(data))

    private fun blob(
        kind: BlobKind,
        label: String,
        data: ByteArray,
    ): List<Frame> = framesOf(kind.id, label, data)

    @Test
    fun eachLaneGetsOnlyTheBlobsOfItsOwnKinds() {
        val link = FakeLink()
        val clip = ArrayList<Frame>()
        val app = ArrayList<Frame>()
        routes.attach(link, BlobRoutes.Lane.CLIPBOARD) { clip.add(it) }
        routes.attach(link, BlobRoutes.Lane.APPS) { app.add(it) }
        val text = blob(BlobKind.CLIP_TEXT, "", "copied".toByteArray())
        val image = blob(BlobKind.CLIP_IMAGE, "", ByteArray(300) { 1 })
        val names = blob(BlobKind.APP_LIST, "", list("1.0\tAlpha"))
        val icon = blob(BlobKind.APP_ICON, "1.0", ByteArray(40) { 2 })

        send(link.blobs, text + names + image + icon)

        assertEquals(text + image, clip)
        assertEquals(names + icon, app)
    }

    @Test
    fun aKindNoLaneTakesIsDroppedAndTheNextBlobStillArrives() {
        val link = FakeLink()
        val clip = ArrayList<Frame>()
        val app = ArrayList<Frame>()
        routes.attach(link, BlobRoutes.Lane.CLIPBOARD) { clip.add(it) }
        routes.attach(link, BlobRoutes.Lane.APPS) { app.add(it) }
        val text = blob(BlobKind.CLIP_TEXT, "", "copied".toByteArray())

        send(link.blobs, framesOf(UNKNOWN_KIND, "", ByteArray(600) { 3 }) + text)

        assertEquals(text, clip)
        assertTrue(app.isEmpty())
    }

    @Test
    fun aLaneWithNoSinkDropsItsBlobsAndLeavesTheOthers() {
        val link = FakeLink()
        val clip = ArrayList<Frame>()
        routes.attach(link, BlobRoutes.Lane.CLIPBOARD) { clip.add(it) }
        val text = blob(BlobKind.CLIP_TEXT, "", "copied".toByteArray())

        send(link.blobs, blob(BlobKind.APP_LIST, "", list("1.0\tAlpha")) + text)

        assertEquals(text, clip)
    }

    @Test
    fun aChunkWithNoBeginBeforeItGoesNowhere() {
        val link = FakeLink()
        val clip = ArrayList<Frame>()
        routes.attach(link, BlobRoutes.Lane.CLIPBOARD) { clip.add(it) }

        send(link.blobs, listOf(Frame.BlobChunk(0, byteArrayOf(1)), Frame.BlobEnd(sha256(byteArrayOf(1)))))

        assertTrue(clip.isEmpty())
    }

    @Test
    fun theLastLaneToLeaveTakesTheLinksSinkWithIt() {
        val link = FakeLink()
        routes.attach(link, BlobRoutes.Lane.CLIPBOARD) {}
        routes.attach(link, BlobRoutes.Lane.APPS) {}

        routes.attach(link, BlobRoutes.Lane.CLIPBOARD, null)
        assertNotNull(link.blobs)

        routes.attach(link, BlobRoutes.Lane.APPS, null)
        assertNull(link.blobs)
    }

    @Test
    fun aLaneThatLeftGetsNothingMoreAndTheOtherCarriesOn() {
        val link = FakeLink()
        val clip = ArrayList<Frame>()
        val app = ArrayList<Frame>()
        routes.attach(link, BlobRoutes.Lane.CLIPBOARD) { clip.add(it) }
        routes.attach(link, BlobRoutes.Lane.APPS) { app.add(it) }

        routes.attach(link, BlobRoutes.Lane.APPS, null)
        val names = blob(BlobKind.APP_LIST, "", list("1.0\tAlpha"))
        send(link.blobs, names + blob(BlobKind.CLIP_TEXT, "", byteArrayOf(5)))

        assertTrue(app.isEmpty())
        assertEquals(3, clip.size)
    }

    @Test
    fun detachingFromALinkNeverAttachedChangesNothing() {
        val link = FakeLink()

        routes.attach(link, BlobRoutes.Lane.APPS, null)

        assertNull(link.blobs)
    }

    @Test
    fun eachLinkHasARouterOfItsOwn() {
        val one = FakeLink()
        val two = FakeLink()
        val heard = ArrayList<String>()
        routes.attach(one, BlobRoutes.Lane.APPS) { heard.add("one") }
        routes.attach(two, BlobRoutes.Lane.APPS) { heard.add("two") }

        send(two.blobs, blob(BlobKind.APP_LIST, "", list("1.0\tAlpha")))

        assertEquals(3, heard.size)
        assertTrue(heard.all { it == "two" })
    }

    @Test
    fun aListFillsTheCatalogueOfTheLaptopItCameFrom() {
        val a = laptop("a")
        val b = laptop("b")
        apps.refresh(listOf(a, b))

        send(a.fake().blobs, blob(BlobKind.APP_LIST, "", list("5.0\tAlpha", "5.1\tBeta")))

        assertEquals(listOf("Alpha", "Beta"), names(a))
        assertFalse(b.state.apps.loaded)
        assertEquals(listOf<Pair<Laptop, String?>>(a to null), told)
    }

    @Test
    fun anIconFollowsItsListAndNamesItsRowByLabel() {
        val a = laptop()
        apps.refresh(listOf(a))
        val picture = ByteArray(100) { 7 }

        send(a.fake().blobs, blob(BlobKind.APP_LIST, "", list("5.0\tAlpha", "5.1\tBeta")))
        send(a.fake().blobs, blob(BlobKind.APP_ICON, "5.1", picture))

        assertArrayEquals(picture, a.state.apps.icon("5.1"))
        assertNull(a.state.apps.icon("5.0"))
        assertEquals(listOf<Pair<Laptop, String?>>(a to null, a to "5.1"), told)
    }

    @Test
    fun anIconForAnUnknownTokenChangesNothing() {
        val a = laptop()
        apps.refresh(listOf(a))
        send(a.fake().blobs, blob(BlobKind.APP_LIST, "", list("5.0\tAlpha")))
        told.clear()

        send(a.fake().blobs, blob(BlobKind.APP_ICON, "4.0", ByteArray(100) { 7 }))

        assertNull(a.state.apps.icon("4.0"))
        assertTrue(told.isEmpty())
    }

    @Test
    fun anIconOverTheCapIsRefusedAndTheNextOneStillArrives() {
        val a = laptop()
        apps.refresh(listOf(a))
        send(a.fake().blobs, blob(BlobKind.APP_LIST, "", list("5.0\tAlpha", "5.1\tBeta")))
        val small = ByteArray(100) { 7 }

        send(a.fake().blobs, blob(BlobKind.APP_ICON, "5.0", ByteArray(AppCatalogState.MAX_ICON_BYTES + 1)))
        send(a.fake().blobs, blob(BlobKind.APP_ICON, "5.1", small))

        assertNull(a.state.apps.icon("5.0"))
        assertArrayEquals(small, a.state.apps.icon("5.1"))
    }

    @Test
    fun aBlobWithAWrongHashIsNotKept() {
        val a = laptop()
        apps.refresh(listOf(a))
        val data = list("5.0\tAlpha")
        val frames = blob(BlobKind.APP_LIST, "", data).dropLast(1) + Frame.BlobEnd(ByteArray(SHA256_BYTES))

        send(a.fake().blobs, frames)

        assertFalse(a.state.apps.loaded)
        assertTrue(told.isEmpty())
    }

    @Test
    fun aLaptopThatDidNotNameAppsIsLeftAlone() {
        val plain = laptop(features = "clipboard")

        apps.refresh(listOf(plain))

        assertNull(plain.fake().blobs)
    }

    @Test
    fun aLaptopNotConnectedIsLeftAlone() {
        val a = laptop()
        a.fake().connected = false

        apps.refresh(listOf(a))

        assertNull(a.fake().blobs)
    }

    @Test
    fun aRefreshThatChangesNothingKeepsTheSinkAndTakesNoBlobTwice() {
        val a = laptop()
        apps.refresh(listOf(a))
        apps.refresh(listOf(a))

        send(a.fake().blobs, blob(BlobKind.APP_LIST, "", list("5.0\tAlpha")))

        assertEquals(1, told.size)
    }

    @Test
    fun aLostLinkIsLetGoAndItsListIsForgotten() {
        val a = laptop()
        apps.refresh(listOf(a))
        send(a.fake().blobs, blob(BlobKind.APP_LIST, "", list("5.0\tAlpha")))
        val old = a.fake()

        old.connected = false
        apps.refresh(listOf(a))

        assertNull(old.blobs)
        assertFalse(a.state.apps.loaded)
        assertTrue(
            a.state.apps.apps
                .isEmpty(),
        )
    }

    @Test
    fun aReconnectedLaptopIsTakenAgainOnItsNewLink() {
        val a = laptop()
        apps.refresh(listOf(a))
        a.fake().connected = false
        apps.refresh(listOf(a))

        a.link = FakeLink()
        apps.refresh(listOf(a))
        send(a.fake().blobs, blob(BlobKind.APP_LIST, "", list("6.0\tAlpha")))

        assertEquals(listOf("Alpha"), names(a))
    }

    @Test
    fun aBlobPostedBeforeItsLinkWasLetGoChangesNothing() {
        val a = laptop()
        apps.refresh(listOf(a))
        postNow = false
        send(a.fake().blobs, blob(BlobKind.APP_LIST, "", list("5.0\tAlpha")))

        a.fake().connected = false
        apps.refresh(listOf(a))
        posted.forEach { it() }

        assertFalse(a.state.apps.loaded)
        assertTrue(told.isEmpty())
    }

    @Test
    fun theClipboardAndTheAppListShareOneLink() {
        val a = laptop(features = "clipboard/apps")
        val surface = FakeSurface()
        val pending = ArrayList<() -> Unit>()
        val clipboard =
            ClipboardLinks(surface, { true }, {}, routes) { device, sender, allowed, ended ->
                ClipboardSync(device, sender, allowed, ended, { 1_000L }, { _, work -> pending.add(work) }) { }
            }
        clipboard.refresh(listOf(a), a)
        apps.refresh(listOf(a))
        val copied = "copied on the laptop".toByteArray()

        send(a.fake().blobs, blob(BlobKind.CLIP_TEXT, "", copied) + blob(BlobKind.APP_LIST, "", list("5.0\tAlpha")))
        pending.forEach { it() }

        assertEquals(1, surface.writes.size)
        assertArrayEquals(copied, surface.writes.single().data)
        assertEquals(listOf("Alpha"), names(a))
    }

    @Test
    fun theClipboardLettingGoLeavesTheAppListItsSink() {
        val a = laptop(features = "clipboard/apps")
        val clipboard = ClipboardLinks(FakeSurface(), { true }, {}, routes)
        clipboard.refresh(listOf(a), a)
        apps.refresh(listOf(a))

        a.state.take(Frame.Text(TextKind.FEATURES.id, "apps"))
        clipboard.refresh(listOf(a), a)

        assertNotNull(a.fake().blobs)
        send(a.fake().blobs, blob(BlobKind.APP_LIST, "", list("5.0\tAlpha")))
        assertEquals(listOf("Alpha"), names(a))
    }

    private class FakeSurface : ClipboardSurface {
        val writes = ArrayList<ClipboardContent>()

        override fun read(): ClipboardContent? = null

        override fun write(content: ClipboardContent) {
            writes.add(content)
        }
    }

    private class FakeLink : ManagedLink {
        override val id: Any = Any()
        override var connected = true
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

    private companion object {
        const val SEQ_MASK = 0xFF
        const val SHA256_BYTES = 32

        /** Not one of the four kinds a laptop sends this build. */
        const val UNKNOWN_KIND = 9
    }
}
