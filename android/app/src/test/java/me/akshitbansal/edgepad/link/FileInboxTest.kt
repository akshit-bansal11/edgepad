package me.akshitbansal.edgepad.link

import me.akshitbansal.edgepad.protocol.FileStatus
import me.akshitbansal.edgepad.protocol.FileType
import me.akshitbansal.edgepad.protocol.Frame
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.ByteArrayOutputStream
import java.io.IOException
import java.security.MessageDigest

/** The phone's side of a capture transfer: what it keeps, what it refuses, and that a refusal leaves nothing behind. */
class FileInboxTest {
    private class Memory(
        private val failWrites: Boolean = false,
    ) : FileInbox.Store {
        val opened = ArrayList<String>()
        val published = ArrayList<ByteArray>()
        var discarded = 0

        override fun open(
            name: String,
            type: FileType,
        ): FileInbox.Pending {
            opened.add(name)
            val sink = ByteArrayOutputStream()
            return object : FileInbox.Pending {
                override fun write(bytes: ByteArray) {
                    if (failWrites) throw IOException("disk full")
                    sink.write(bytes)
                }

                override fun publish(): String {
                    published.add(sink.toByteArray())
                    return "Pictures/Edgepad"
                }

                override fun discard() {
                    discarded++
                }
            }
        }
    }

    private val steps = ArrayList<FileInbox.Progress>()

    private fun sha(bytes: ByteArray): ByteArray = MessageDigest.getInstance("SHA-256").digest(bytes)

    private fun chunks(bytes: ByteArray): List<Frame> =
        bytes.toList().chunked(255).mapIndexed { i, piece -> Frame.FileChunk(i and 0xFF, piece.toByteArray()) }

    private fun file(
        begin: Frame.FileBegin,
        data: ByteArray,
        hash: ByteArray,
    ): List<Frame> = listOf<Frame>(begin) + chunks(data) + Frame.FileEnd(hash)

    /** Every answer the inbox gave, in order, for [frames]. */
    private fun run(
        inbox: FileInbox,
        frames: List<Frame>,
    ): List<Frame> = frames.mapNotNull(inbox::take)

    @Test
    fun aWholeFileWithTheRightHashIsSaved() {
        val store = Memory()
        val data = ByteArray(1000) { it.toByte() }
        val inbox = FileInbox(store) { steps.add(it) }

        val answers = run(inbox, file(Frame.FileBegin(0, 1000, "Screenshot.png"), data, sha(data)))

        assertEquals(listOf(FileStatus.SAVED.frame()), answers)
        assertArrayEquals(data, store.published.single())
        assertEquals(FileInbox.Progress.Saved("Pictures/Edgepad"), steps.last())
    }

    @Test
    fun aWrongHashIsThrownAwayAndSaid() {
        val store = Memory()
        val data = ByteArray(10) { 1 }
        val inbox = FileInbox(store) { steps.add(it) }

        val answers = run(inbox, file(Frame.FileBegin(0, 10, "a.png"), data, ByteArray(32)))

        assertEquals(listOf(FileStatus.BAD_HASH.frame()), answers)
        assertTrue(store.published.isEmpty())
        assertEquals(1, store.discarded)
    }

    @Test
    fun aFileOverItsLimitIsRefusedBeforeAnythingIsOpened() {
        val store = Memory()
        val inbox = FileInbox(store) { steps.add(it) }

        assertEquals(FileStatus.REJECTED.frame(), inbox.take(Frame.FileBegin(0, FileType.IMAGE_LIMIT + 1, "a.png")))
        assertEquals(FileStatus.REJECTED.frame(), inbox.take(Frame.FileBegin(2, FileType.VIDEO_LIMIT + 1, "a.mp4")))
        assertEquals(FileStatus.REJECTED.frame(), inbox.take(Frame.FileBegin(0, 0, "a.png")))
        assertNull(inbox.take(Frame.FileBegin(2, FileType.VIDEO_LIMIT, "a.mp4")))
        assertEquals(listOf("a.mp4"), store.opened)
    }

    @Test
    fun anUnknownTypeIsRefused() {
        val store = Memory()
        val inbox = FileInbox(store) { steps.add(it) }

        assertEquals(FileStatus.REJECTED.frame(), inbox.take(Frame.FileBegin(99, 10, "a.png")))
        assertTrue(store.opened.isEmpty())
    }

    @Test
    fun aChunkOutOfOrderOrPastTheSizeRefusesTheFile() {
        val store = Memory()
        val inbox = FileInbox(store) { steps.add(it) }

        inbox.take(Frame.FileBegin(0, 4, "a.png"))
        assertEquals(FileStatus.REJECTED.frame(), inbox.take(Frame.FileChunk(1, byteArrayOf(1))))
        // The rest of a refused file is ignored: the answer has gone once.
        assertNull(inbox.take(Frame.FileChunk(2, byteArrayOf(1))))
        assertNull(inbox.take(Frame.FileEnd(ByteArray(32))))

        inbox.take(Frame.FileBegin(0, 4, "b.png"))
        assertEquals(FileStatus.REJECTED.frame(), inbox.take(Frame.FileChunk(0, ByteArray(5))))
        assertEquals(2, store.discarded)
    }

    @Test
    fun aShortFileIsRefusedAtItsEnd() {
        val store = Memory()
        val inbox = FileInbox(store) { steps.add(it) }
        val data = byteArrayOf(1, 2, 3)

        val answers = run(inbox, file(Frame.FileBegin(0, 4, "a.png"), data, sha(data)))

        assertEquals(listOf(FileStatus.REJECTED.frame()), answers)
        assertTrue(store.published.isEmpty())
    }

    @Test
    fun aWriteThatFailsIsSaidAndTheFileThrownAway() {
        val store = Memory(failWrites = true)
        val inbox = FileInbox(store) { steps.add(it) }

        inbox.take(Frame.FileBegin(0, 1, "a.png"))

        assertEquals(FileStatus.WRITE_FAILED.frame(), inbox.take(Frame.FileChunk(0, byteArrayOf(1))))
        assertEquals(1, store.discarded)
    }

    @Test
    fun theSequenceWrapsAt256() {
        val store = Memory()
        val inbox = FileInbox(store) { steps.add(it) }
        val data = ByteArray(300) { it.toByte() }

        inbox.take(Frame.FileBegin(0, 300, "a.png"))
        repeat(300) { assertNull(inbox.take(Frame.FileChunk(it and 0xFF, byteArrayOf(data[it])))) }

        assertEquals(FileStatus.SAVED.frame(), inbox.take(Frame.FileEnd(sha(data))))
    }

    @Test
    fun progressIsReportedOncePerPercentNotOncePerChunk() {
        val inbox = FileInbox(Memory()) { steps.add(it) }
        val data = ByteArray(255 * 1000)

        run(inbox, listOf<Frame>(Frame.FileBegin(0, data.size.toLong(), "a.png")) + chunks(data))

        assertEquals(101, steps.count { it is FileInbox.Progress.Receiving })
    }

    @Test
    fun aLinkThatDropsMidFileLeavesNothing() {
        val store = Memory()
        val inbox = FileInbox(store) { steps.add(it) }

        inbox.take(Frame.FileBegin(0, 4, "a.png"))
        inbox.take(Frame.FileChunk(0, byteArrayOf(1, 2)))
        inbox.abort()

        assertEquals(1, store.discarded)
        assertNull(inbox.take(Frame.FileChunk(1, byteArrayOf(3, 4))))
    }

    @Test
    fun aPathLikeNameIsRefusedNotRepaired() {
        for (name in listOf("../evil.png", "a/b.png", "a\\b.png", ".hidden.png", "..", ".", "", "a\u0000.png")) {
            assertNull(name, safeName(name, FileType.PNG))
        }
    }

    @Test
    fun aNameIsCleanedAndGivenItsTypesExtension() {
        assertEquals("Screenshot 2026-10-25 103012.png", safeName("Screenshot 2026-10-25 103012.png", FileType.PNG))
        assertEquals("a_b_c.png", safeName("a:b\u0007c.png", FileType.PNG))
        assertEquals("run.png", safeName("run.apk", FileType.PNG))
        assertEquals("clip.mp4", safeName("clip", FileType.MP4))
        assertNull(safeName("   .png", FileType.PNG))
    }

    @Test
    fun aLongNameIsCutWithoutHalvingACharacter() {
        // 95 letters, then an emoji whose first half would be the 96th character kept.
        val name = safeName("a".repeat(95) + MUSICAL_NOTE + "b".repeat(50) + ".png", FileType.PNG)

        assertEquals("a".repeat(95) + ".png", name)
        assertTrue(name!!.length <= MAX_NAME_LENGTH)
    }

    private companion object {
        /** U+1F3B5, two UTF-16 units. */
        const val MUSICAL_NOTE = "🎵"
    }
}
