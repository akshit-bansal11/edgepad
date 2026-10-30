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

/**
 * Where the one image the laptop last sent lives, and how another app is pointed at it. A clip can hold a picture only
 * as a URI, so the PNG goes in a file that [ClipProvider] serves; there is only ever the one, and the next image
 * replaces it.
 */
object ClipFile {
    const val DIR = "clip"
    const val NAME = "clip.png"
    const val MIME = "image/png"
    private const val TEMP = "clip.tmp"

    /** The provider's authority, which the manifest builds from the application id the same way. */
    fun authority(context: Context): String = context.packageName + ".clip"

    fun uri(context: Context): Uri =
        Uri
            .Builder()
            .scheme(ContentResolver.SCHEME_CONTENT)
            .authority(authority(context))
            .appendPath(NAME)
            .build()

    fun file(context: Context): File = File(File(context.cacheDir, DIR), NAME)

    /**
     * Writes [png] as the file and returns its URI. Written beside it and renamed over it, so an app reading the last
     * image never sees half of the next one.
     */
    @Synchronized
    fun store(
        context: Context,
        png: ByteArray,
    ): Uri {
        val target = file(context)
        val dir = target.parentFile ?: throw IOException("No cache folder")
        dir.mkdirs()
        val temp = File(dir, TEMP)
        temp.writeBytes(png)
        if (!temp.renameTo(target)) throw IOException("The clip could not be put in place")
        return uri(context)
    }
}

/**
 * True for the one path the provider serves, `/clip.png`, given as the segments Android has already decoded: a `..`, a
 * second segment, another name or no name at all is not it. Nothing else reaches the file system, so no path an app
 * sends can name a different file.
 */
internal fun isClipPath(segments: List<String>): Boolean = segments.size == 1 && segments[0] == ClipFile.NAME

/**
 * Hands the clipboard's image to the app the user pastes it into. Not exported: the system grants the paster read
 * access to this one URI when it takes the clip, and nothing else can open it. Read-only, one file.
 */
class ClipProvider : ContentProvider() {
    override fun onCreate(): Boolean = true

    override fun getType(uri: Uri): String? = if (isClipPath(uri.pathSegments)) ClipFile.MIME else null

    override fun openFile(
        uri: Uri,
        mode: String,
    ): ParcelFileDescriptor? {
        if (mode != "r" || !isClipPath(uri.pathSegments)) throw FileNotFoundException("Not a clip")
        return ParcelFileDescriptor.open(ClipFile.file(requireContext()), ParcelFileDescriptor.MODE_READ_ONLY)
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
        val size = ClipFile.file(requireContext()).length()
        val row = columns.map { if (it == OpenableColumns.DISPLAY_NAME) ClipFile.NAME else size }
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
