package me.akshitbansal.edgepad.link.blob

import android.graphics.Bitmap
import android.graphics.BitmapFactory

/**
 * The media icons of one laptop, decoded once on arrival: at most [CAPACITY] by app name, least recently used out
 * first, and one [ARTWORK] slot of its own. Nothing is decoded when something draws, and there is no timer.
 *
 * [put] runs on the link's reading thread and [get] on the UI thread, hence the lock.
 *
 * @param decode The PNG's picture, or null for bytes that are not an icon after all. A seam for tests.
 */
class MediaIconStore<T : Any>(
    private val decode: (ByteArray) -> T?,
) {
    private val icons =
        object : LinkedHashMap<String, T>(CAPACITY, 1f, true) {
            override fun removeEldestEntry(eldest: MutableMap.MutableEntry<String, T>): Boolean = size > CAPACITY
        }
    private var artwork: T? = null

    /** How many blobs were dropped for not being a PNG icon of a sane size. */
    @get:Synchronized
    var dropped = 0
        private set

    /** Decodes and keeps the icon under [label]. A bad one is dropped and counted, and the old icon stays. */
    fun put(
        label: String,
        png: ByteArray,
    ) {
        val picture = if (isPng(png)) decode(png) else null
        synchronized(this) {
            if (picture == null) {
                dropped++
            } else if (label == ARTWORK) {
                artwork = picture
            } else {
                icons[label] = picture
            }
        }
    }

    /** The icon of [label], or null. Reading it makes it the most recently used. */
    @Synchronized
    fun get(label: String): T? = if (label == ARTWORK) artwork else icons[label]

    /** Forgets every icon: the laptop is gone. */
    @Synchronized
    fun clear() {
        icons.clear()
        artwork = null
    }

    companion object {
        const val CAPACITY = 8

        /** The label of a browser's media artwork. */
        const val ARTWORK = "artwork"

        /** The most a decoded side may be: the laptop sends 64 px, and a claim past this is not an icon. */
        const val MAX_SIDE_PX = 128

        private val PNG_SIGNATURE = byteArrayOf(0x89.toByte(), 0x50, 0x4E, 0x47)

        private fun isPng(bytes: ByteArray): Boolean =
            bytes.size > PNG_SIGNATURE.size && PNG_SIGNATURE.indices.all { bytes[it] == PNG_SIGNATURE[it] }

        /** The production decoder: the header first, so a few bytes claiming a huge size are never decoded. */
        fun decodeBitmap(png: ByteArray): Bitmap? {
            val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
            BitmapFactory.decodeByteArray(png, 0, png.size, bounds)
            if (bounds.outWidth !in 1..MAX_SIDE_PX || bounds.outHeight !in 1..MAX_SIDE_PX) return null
            return BitmapFactory.decodeByteArray(png, 0, png.size)
        }
    }
}
