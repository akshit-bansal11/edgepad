package me.akshitbansal.edgepad.link

import android.content.ContentProvider
import android.content.ContentResolver
import android.content.ContentValues
import android.content.Context
import android.database.Cursor
import android.database.MatrixCursor
import android.net.Uri
import android.os.ParcelFileDescriptor
import android.provider.OpenableColumns
import java.io.File
import java.io.FileNotFoundException
import java.io.IOException
import java.util.UUID

/**
 * Where the one image the laptop last sent lives, and how another app is pointed at it. A clip can hold a picture only
 * as a URI, so the PNG goes in a file that [ClipProvider] serves. Every image gets its own name and the next one
 * deletes it: an app that kept the URI then finds nothing, never a newer image nobody gave it.
 */
object ClipFile {
    const val DIR = "clip"

    /** What a paster is told the image is called, whatever it is stored as. */
    const val DISPLAY_NAME = "clip.png"
    const val MIME = "image/png"

    /** The provider's authority, which the manifest builds from the application id the same way. */
    fun authority(context: Context): String = context.packageName + ".clip"

    fun uri(
        context: Context,
        name: String,
    ): Uri =
        Uri
            .Builder()
            .scheme(ContentResolver.SCHEME_CONTENT)
            .authority(authority(context))
            .appendPath(name)
            .build()

    fun file(
        context: Context,
        name: String,
    ): File = File(File(context.cacheDir, DIR), name)

    internal fun newName(): String = "clip-" + UUID.randomUUID().toString().replace("-", "") + ".png"

    /**
     * Writes [png] under a new name, deletes every other file and returns the URI. Nothing can ask for the new name
     * until this returns, and an app reading the last image keeps the file it opened.
     */
    @Synchronized
    fun store(
        context: Context,
        png: ByteArray,
    ): Uri {
        val name = newName()
        val target = file(context, name)
        val dir = target.parentFile ?: throw IOException("No cache folder")
        dir.mkdirs()
        target.writeBytes(png)
        dir.listFiles()?.filter { it.name != name }?.forEach { it.delete() }
        return uri(context, name)
    }
}

private val CLIP_NAME = Regex("clip-[0-9a-f]{32}\\.png")

/**
 * True for a path the provider serves, one name of the shape [ClipFile] gives out, in the segments Android has already
 * decoded: a `..`, a second segment, another name or no name at all is not it. Nothing else reaches the file system,
 * so no path an app sends can name a different file.
 */
internal fun isClipPath(segments: List<String>): Boolean = segments.size == 1 && CLIP_NAME.matches(segments[0])

/**
 * Hands the clipboard's image to the app the user pastes it into. Not exported: the system grants the paster read
 * access to this one URI when it takes the clip, and nothing else can open it. Read-only, one image at a time.
 */
class ClipProvider : ContentProvider() {
    override fun onCreate(): Boolean = true

    override fun getType(uri: Uri): String? = if (isClipPath(uri.pathSegments)) ClipFile.MIME else null

    override fun openFile(
        uri: Uri,
        mode: String,
    ): ParcelFileDescriptor? {
        if (mode != "r" || !isClipPath(uri.pathSegments)) throw FileNotFoundException("Not a clip")
        val file = ClipFile.file(requireContext(), uri.pathSegments[0])
        return ParcelFileDescriptor.open(file, ParcelFileDescriptor.MODE_READ_ONLY)
    }

    // What a paster asks for to name and size what it opened.
    override fun query(
        uri: Uri,
        projection: Array<String>?,
        selection: String?,
        selectionArgs: Array<String>?,
        sortOrder: String?,
    ): Cursor? {
        if (!isClipPath(uri.pathSegments)) return null
        val all = arrayOf(OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE)
        val columns = projection?.filter { it in all }?.toTypedArray() ?: all
        val size = ClipFile.file(requireContext(), uri.pathSegments[0]).length()
        val row = columns.map { if (it == OpenableColumns.DISPLAY_NAME) ClipFile.DISPLAY_NAME else size }
        return MatrixCursor(columns).apply { addRow(row) }
    }

    override fun insert(
        uri: Uri,
        values: ContentValues?,
    ): Uri? = null

    override fun update(
        uri: Uri,
        values: ContentValues?,
        selection: String?,
        selectionArgs: Array<String>?,
    ): Int = 0

    override fun delete(
        uri: Uri,
        selection: String?,
        selectionArgs: Array<String>?,
    ): Int = 0
}
