package me.akshitbansal.edgepad.link

import android.content.ClipData
import android.content.ClipDescription
import android.content.ClipboardManager
import android.content.ContentResolver
import android.content.Context
import android.graphics.Bitmap
import android.graphics.ImageDecoder
import android.net.Uri
import android.util.Log
import me.akshitbansal.edgepad.link.blob.ClipboardContent
import me.akshitbansal.edgepad.link.blob.ClipboardSurface
import me.akshitbansal.edgepad.link.blob.ClipboardSync
import me.akshitbansal.edgepad.protocol.BlobKind
import java.io.ByteArrayOutputStream
import java.io.IOException
import java.io.InputStream
import kotlin.math.min
import kotlin.math.sqrt

/**
 * What the platform's clipboard can do, apart from when the app may use it. [ClipboardBridge] is the rules; the real
 * one is [AndroidClipboard], and a test stands another in.
 */
interface ClipboardDevice {
    /** The clipboard now, or null for nothing that can be shared. May block. */
    fun read(): ClipboardContent?

    fun write(content: ClipboardContent)

    /** Starts telling [onChanged] about every change, or stops when it is null. */
    fun listen(onChanged: (() -> Unit)?)

    /** Remembers the clip that is on the clipboard now, so [read] gives nothing until a newer one is copied. */
    fun settle()
}

/**
 * This phone's clipboard, for [ClipboardSync], and when the app may touch it. From Android 10 only the app in front
 * can read the clipboard, and from Android 12 the system tells the user each time another app's clip is read, so the
 * phone side works only while Edgepad is open: changes are listened for between [start] and [stop], and one more read
 * is made when the window gains focus, which is when a copy made in another app first becomes readable. A clip from the
 * laptop is written whenever it arrives, started or not: writing is allowed from the background.
 *
 * Nothing here is read while stopped, and [onChanged] is not called, so a stopped app never asks the system for
 * anything it would refuse.
 */
class ClipboardBridge(
    private val device: ClipboardDevice,
) : ClipboardSurface {
    /** Told of every change while started and of the focus read. Any thread; [ClipboardSync] returns at once. */
    @Volatile var onChanged: () -> Unit = {}

    @Volatile private var started = false

    /** The app is in front: listen. */
    fun start() {
        if (started) return
        started = true
        device.listen { onChanged() }
    }

    /** The app is behind: no listening and no reading. */
    fun stop() {
        if (!started) return
        started = false
        device.listen(null)
    }

    /** The window gained focus: a copy made in another app is readable now, so ask for one read. */
    fun focused() {
        if (started) onChanged()
    }

    /** What is on the clipboard now will not be sent. Best effort: a clipboard that cannot be asked keeps its last. */
    fun settle() = device.settle()

    override fun read(): ClipboardContent? = if (started) device.read() else null

    override fun write(content: ClipboardContent) = device.write(content)
}

/**
 * The real clipboard. Built on the application context, since it outlives the activity. Never reads a clip flagged
 * sensitive. An image travels as a PNG of at most [ClipboardSync.MAX_IMAGE_BYTES], so a larger one is sent smaller.
 */
class AndroidClipboard(
    private val context: Context,
) : ClipboardDevice {
    private val manager = context.getSystemService(ClipboardManager::class.java)
    private val resolver = context.contentResolver
    private val listener = ClipboardManager.OnPrimaryClipChangedListener { callback?.invoke() }

    @Volatile private var callback: (() -> Unit)? = null

    /** Clips copied at or before this are not sent, in [System.currentTimeMillis], the time base Android stamps a clip in. */
    @Volatile private var settled = Long.MIN_VALUE

    override fun listen(onChanged: (() -> Unit)?) {
        callback = onChanged
        if (onChanged != null) {
            manager.addPrimaryClipChangedListener(listener)
        } else {
            manager.removePrimaryClipChangedListener(listener)
        }
    }

    // Android hides the clipboard from an app without focus, as when a laptop connects while Edgepad is still opening.
    // Then "now" stands in for the clip's own stamp: whatever is there was copied before it, and a copy made after it,
    // in another app, is still sent when Edgepad comes back.
    override fun settle() {
        settled = manager.primaryClipDescription?.timestamp?.takeIf { it > 0 } ?: System.currentTimeMillis()
    }

    // The description first: it carries the timestamp and the sensitive flag, and reading it neither shows Android's
    // "pasted from your clipboard" notice nor opens the content, so a clip that will not be sent is never read.
    override fun read(): ClipboardContent? {
        val description = manager.primaryClipDescription ?: return null
        if (description.timestamp <= settled) return null
        // Before anything is opened: a password manager's copy is never touched, let alone sent.
        if (isSensitive(description)) return null
        val clip = manager.primaryClip ?: return null
        if (clip.itemCount == 0 || isSensitive(clip.description)) return null
        val item = clip.getItemAt(0)
        val uri = item.uri
        return when {
            uri != null && (description.hasMimeType(IMAGE_ANY) || isImage(uri)) -> readImage(uri)
            description.hasMimeType(TEXT_ANY) -> textOf(item.coerceToText(context).toString())
            else -> null
        }
    }

    override fun write(content: ClipboardContent) {
        when (content.kind) {
            BlobKind.CLIP_TEXT -> {
                manager.setPrimaryClip(ClipData.newPlainText(LABEL, content.data.decodeToString()))
            }

            BlobKind.CLIP_IMAGE -> {
                val uri = ClipFile.store(context, content.data)
                manager.setPrimaryClip(ClipData.newUri(resolver, LABEL, uri))
            }

            else -> {
                Unit
            }
        }
    }

    // A flag that is there but is not a boolean counts as set, as an unreadable one does everywhere.
    private fun isSensitive(description: ClipDescription): Boolean {
        val extras = description.extras ?: return false
        return extras.containsKey(SENSITIVE_EXTRA) && extras.getBoolean(SENSITIVE_EXTRA, true)
    }

    private fun textOf(text: String): ClipboardContent? =
        if (text.isEmpty()) null else ClipboardContent(BlobKind.CLIP_TEXT, text.toByteArray())

    // Some apps put an image on the clipboard as a bare URI, which describes itself as text/uri-list.
    private fun isImage(uri: Uri): Boolean = resolver.getType(uri)?.startsWith(IMAGE_PREFIX) == true

    // Another app's content URI is read through its own provider; a file or any other scheme is not. A PNG within the
    // cap goes as it is, and anything else is decoded and sent as the largest PNG that fits.
    private fun readImage(uri: Uri): ClipboardContent? {
        if (uri.scheme != ContentResolver.SCHEME_CONTENT) return null
        return try {
            val original = resolver.openInputStream(uri)?.use { readCapped(it) }
            val png = original?.takeIf(::isPngSignature) ?: shrunkPng(uri)
            png?.let { ClipboardContent(BlobKind.CLIP_IMAGE, it) }
        } catch (e: IOException) {
            Log.w(TAG, "Clipboard: the image could not be read: ${e.javaClass.simpleName}")
            null
        } catch (e: SecurityException) {
            Log.w(TAG, "Clipboard: the image is not ours to read")
            null
        } catch (e: OutOfMemoryError) {
            // A photo within the pixel cap can still be too big to decode on a small phone; losing one copy beats
            // the whole app, and every link with it.
            Log.w(TAG, "Clipboard: the image was too large to convert")
            null
        }
    }

    // One byte past the cap says it is over, and no more than that is held.
    private fun readCapped(input: InputStream): ByteArray? {
        val out = ByteArrayOutputStream()
        val buffer = ByteArray(BUFFER_BYTES)
        var total = 0L
        while (true) {
            val n = input.read(buffer)
            if (n < 0) break
            total += n
            if (total > ClipboardSync.MAX_IMAGE_BYTES) return null
            out.write(buffer, 0, n)
        }
        return out.toByteArray()
    }

    // ponytail: a photo as a 4 MiB PNG is far smaller than the photo. JPEG on the wire would keep more, and needs a
    // protocol change on all three laptops.
    // Decoded to size from the header, so a huge image never fills the memory; this also applies its EXIF rotation.
    private fun shrunkPng(uri: Uri): ByteArray? {
        val decoded =
            ImageDecoder.decodeBitmap(ImageDecoder.createSource(resolver, uri)) { decoder, info, _ ->
                val scale = decodeScale(info.size.width, info.size.height)
                decoder.allocator = ImageDecoder.ALLOCATOR_SOFTWARE
                decoder.setTargetSize(scaled(info.size.width, scale), scaled(info.size.height, scale))
            }
        return try {
            fittedEncoding(ClipboardSync.MAX_IMAGE_BYTES) { scale -> png(decoded, scale) }
        } finally {
            decoded.recycle()
        }
    }

    private fun png(
        bitmap: Bitmap,
        scale: Double,
    ): ByteArray {
        val width = scaled(bitmap.width, scale)
        val height = scaled(bitmap.height, scale)
        val sized = if (scale == 1.0) bitmap else Bitmap.createScaledBitmap(bitmap, width, height, true)
        return try {
            ByteArrayOutputStream().also { sized.compress(Bitmap.CompressFormat.PNG, PNG_QUALITY, it) }.toByteArray()
        } finally {
            if (sized !== bitmap) sized.recycle()
        }
    }

    private companion object {
        const val TAG = "Edgepad"
        const val LABEL = "Edgepad"
        const val TEXT_ANY = "text/*"
        const val IMAGE_ANY = "image/*"
        const val IMAGE_PREFIX = "image/"
        const val BUFFER_BYTES = 8192

        /** PNG is lossless and takes no quality; the number is ignored. */
        const val PNG_QUALITY = 100

        /** [ClipDescription.EXTRA_IS_SENSITIVE] is API 33; Edgepad runs from 31, and the string is the same. */
        const val SENSITIVE_EXTRA = "android.content.extra.IS_SENSITIVE"
    }
}

private val PNG_SIGNATURE = byteArrayOf(-119, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)

/** Well past what fits in the cap as a PNG unless the picture is mostly flat colour, and far under what fills memory. */
private const val MAX_DECODED_PIXELS = 4_000_000.0
private const val MAX_SHRINKS = 5

/** A try lands a little under the size it aims for, so the next one rarely misses by a few bytes. */
private const val SHRINK_MARGIN = 0.9

/** The scale that brings a picture down to [MAX_DECODED_PIXELS], and never above its own size. */
internal fun decodeScale(
    width: Int,
    height: Int,
): Double = min(1.0, sqrt(MAX_DECODED_PIXELS / (width.toDouble() * height)))

internal fun scaled(
    side: Int,
    scale: Double,
): Int = (side * scale).toInt().coerceAtLeast(1)

/**
 * The first of [encode]'s results within [cap], or null if none is. A PNG's size goes with its pixels, the square of
 * the scale, so each try shrinks by the square root of how far over the last one was.
 */
internal fun fittedEncoding(
    cap: Long,
    encode: (Double) -> ByteArray,
): ByteArray? {
    var scale = 1.0
    repeat(MAX_SHRINKS) {
        val bytes = encode(scale)
        if (bytes.size <= cap) return bytes
        scale *= sqrt(cap.toDouble() / bytes.size) * SHRINK_MARGIN
    }
    return null
}

/** True when [data] starts with the PNG signature, so it needs no re-encoding. */
internal fun isPngSignature(data: ByteArray): Boolean =
    data.size >= PNG_SIGNATURE.size && data.copyOf(PNG_SIGNATURE.size).contentEquals(PNG_SIGNATURE)
