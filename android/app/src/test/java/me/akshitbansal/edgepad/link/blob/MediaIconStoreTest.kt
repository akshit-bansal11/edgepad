package me.akshitbansal.edgepad.link.blob

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Test

class MediaIconStoreTest {
    private val png = byteArrayOf(0x89.toByte(), 0x50, 0x4E, 0x47, 1)
    private val store = MediaIconStore { bytes -> if (bytes.last().toInt() == 1) "pic" else null }

    @Test
    fun ninthIconEvictsTheLeastRecentlyUsed() {
        repeat(8) { store.put("app$it", png) }
        assertNotNull(store.get("app0"))
        store.put("app8", png)
        assertNotNull(store.get("app0"))
        assertNull(store.get("app1"))
    }

    @Test
    fun artworkHasItsOwnSlot() {
        repeat(9) { store.put("app$it", png) }
        store.put(MediaIconStore.ARTWORK, png)
        assertNotNull(store.get(MediaIconStore.ARTWORK))
        assertNotNull(store.get("app8"))
    }

    @Test
    fun nonPngAndUndecodableAreDroppedAndCounted() {
        store.put("a", byteArrayOf(1, 2, 3, 4, 5, 6))
        store.put("b", byteArrayOf(0x89.toByte(), 0x50, 0x4E, 0x47, 0))
        assertNull(store.get("a"))
        assertNull(store.get("b"))
        assertEquals(2, store.dropped)
    }
}
