package me.akshitbansal.edgepad.link

import android.content.ClipData
import android.content.ClipDescription
import android.content.ClipboardManager
import android.content.ContentResolver
import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.net.Uri
import android.util.Log
import me.akshitbansal.edgepad.link.blob.ClipboardContent
import me.akshitbansal.edgepad.link.blob.ClipboardSurface
import me.akshitbansal.edgepad.link.blob.ClipboardSync
import me.akshitbansal.edgepad.protocol.BlobKind
import java.io.ByteArrayOutputStream
import java.io.IOException
import java.io.InputStream

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
 * sensitive, and never more of an image than [ClipboardSync.MAX_IMAGE_BYTES].
 */
class AndroidClipboard(
    private val context: Context,
) : ClipboardDevice {
    private val manager = context.getSystemService(ClipboardManager::class.java)
    private val resolver = context.contentResolver
    private val listener = ClipboardManager.OnPrimaryClipChangedListener { callback?.invoke() }

    @Volatile private var callback: (() -> Unit)? = null

    /** The timestamp of the clip [settle] saw. */
    @Volatile private var settled = Long.MIN_VALUE

    /**
     * [settle] could not see the clipboard (Android hides it from an app without focus), so the first clip [read]
     * sees is taken as the settled one and not sent: what was there before sharing turned on never goes.
     */
    @Volatile private var settlePending = false

    override fun listen(onChanged: (() -> Unit)?) {
        callback = onChanged
        if (onChanged != null) {
            manager.addPrimaryClipChangedListener(listener)
        } else {
            manager.removePrimaryClipChangedListener(listener)
        }
    }

    override fun settle() {
        val description = manager.primaryClipDescription
        settlePending = description == null
        if (description != null) settled = description.timestamp
    }

    // The description first: it carries the timestamp and the sensitive flag, and reading it neither shows Android's
    // "pasted from your clipboard" notice nor opens the content, so a clip that will not be sent is never read.
    override fun read(): ClipboardContent? {
        val description = manager.primaryClipDescription ?: return null
        if (settlePending) {
            settlePending = false
            settled = description.timestamp
            return null
        }
        if (description.timestamp <= settled) return null
        // Before anything is opened: a password manager's copy is never touched, let alone sent.
        if (isSensitive(description)) return null
        val clip = manager.primaryClip ?: return null
        if (clip.itemCount == 0 || isSensitive(clip.description)) return null
        val item = clip.getItemAt(0)
        return when {
            description.hasMimeType(IMAGE_ANY) -> item.uri?.let(::readImage)
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

    // Another app's content URI is read through its own provider, up to the cap; a file or any other scheme is not.
    private fun readImage(uri: Uri): ClipboardContent? {
        if (uri.scheme != ContentResolver.SCHEME_CONTENT) return null
        return try {
            val bytes = resolver.openInputStream(uri)?.use { readCapped(it) } ?: return null
            val png = if (isPngSignature(bytes)) bytes else toPng(bytes)
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

    // The bounds are read first: a small file can decode to far more pixels than the far end will take.
    private fun toPng(bytes: ByteArray): ByteArray? {
        val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        BitmapFactory.decodeByteArray(bytes, 0, bytes.size, bounds)
        val pixels = bounds.outWidth.toLong() * bounds.outHeight
        if (bounds.outWidth <= 0 || bounds.outHeight <= 0 || pixels > ClipboardSync.MAX_IMAGE_PIXELS) return null
        val bitmap = BitmapFactory.decodeByteArray(bytes, 0, bytes.size) ?: return null
        return try {
            ByteArrayOutputStream().also { bitmap.compress(Bitmap.CompressFormat.PNG, PNG_QUALITY, it) }.toByteArray()
        } finally {
            bitmap.recycle()
        }
    }

    private companion object {
        const val TAG = "Edgepad"
        const val LABEL = "Edgepad"
        const val TEXT_ANY = "text/*"
        const val IMAGE_ANY = "image/*"
        const val BUFFER_BYTES = 8192

        /** PNG is lossless and takes no quality; the number is ignored. */
        const val PNG_QUALITY = 100

        /** [ClipDescription.EXTRA_IS_SENSITIVE] is API 33; Edgepad runs from 31, and the string is the same. */
        const val SENSITIVE_EXTRA = "android.content.extra.IS_SENSITIVE"
    }
}

private val PNG_SIGNATURE = byteArrayOf(-119, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)

/** True when [data] starts with the PNG signature, so it needs no re-encoding. */
internal fun isPngSignature(data: ByteArray): Boolean =
    data.size >= PNG_SIGNATURE.size && data.copyOf(PNG_SIGNATURE.size).contentEquals(PNG_SIGNATURE)
