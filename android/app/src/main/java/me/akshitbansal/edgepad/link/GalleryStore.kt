package me.akshitbansal.edgepad.link

import android.content.ContentResolver
import android.content.ContentValues
import android.net.Uri
import android.os.Environment
import android.provider.MediaStore
import android.util.Log
import me.akshitbansal.edgepad.protocol.FileType
import java.io.BufferedOutputStream
import java.io.IOException
import java.io.OutputStream

/**
 * Captures from the laptop, into the phone's gallery: Pictures/Edgepad for an image and Movies/Edgepad for a video.
 * Through MediaStore, which needs no storage permission for files this app creates (minSdk is 31). Each file is
 * inserted as pending, so no other app sees it until [FileInbox] has checked its hash, and a file that fails is
 * deleted rather than left half-written in the gallery.
 */
class GalleryStore(
    private val resolver: ContentResolver,
) : FileInbox.Store {
    override fun open(
        name: String,
        type: FileType,
    ): FileInbox.Pending? {
        val (collection, folder) =
            if (type.video) {
                MediaStore.Video.Media.getContentUri(MediaStore.VOLUME_EXTERNAL_PRIMARY) to MOVIES
            } else {
                MediaStore.Images.Media.getContentUri(MediaStore.VOLUME_EXTERNAL_PRIMARY) to PICTURES
            }
        val values =
            ContentValues().apply {
                put(MediaStore.MediaColumns.DISPLAY_NAME, name)
                put(MediaStore.MediaColumns.MIME_TYPE, type.mime)
                put(MediaStore.MediaColumns.RELATIVE_PATH, folder)
                put(MediaStore.MediaColumns.IS_PENDING, 1)
            }
        var uri: Uri? = null
        return try {
            val created = resolver.insert(collection, values) ?: return null
            uri = created
            val out = resolver.openOutputStream(created) ?: throw IOException("The gallery gave no stream for $created")
            Entry(created, BufferedOutputStream(out, BUFFER_BYTES), folder)
        } catch (e: IOException) {
            Log.w(TAG, "Could not create a gallery file", e)
            // The row was made and cannot be written: it goes, rather than sitting pending in the gallery.
            uri?.let { resolver.delete(it, null, null) }
            null
        } catch (e: IllegalStateException) {
            // Shared storage is not mounted.
            Log.w(TAG, "Could not create a gallery file", e)
            null
        } catch (e: IllegalArgumentException) {
            // The gallery refused the type or the folder.
            Log.w(TAG, "Could not create a gallery file", e)
            null
        }
    }

    private inner class Entry(
        private val uri: Uri,
        private val out: OutputStream,
        private val folder: String,
    ) : FileInbox.Pending {
        override fun write(bytes: ByteArray) = out.write(bytes)

        override fun publish(): String {
            out.close()
            val values = ContentValues().apply { put(MediaStore.MediaColumns.IS_PENDING, 0) }
            if (resolver.update(uri, values, null, null) != 1) throw IOException("The gallery did not publish $uri")
            return folder
        }

        override fun discard() {
            try {
                out.close()
            } catch (e: IOException) {
                // Thrown away either way.
            }
            try {
                resolver.delete(uri, null, null)
            } catch (e: IllegalStateException) {
                Log.w(TAG, "Could not delete a refused capture", e)
            }
        }
    }

    private companion object {
        const val TAG = "Edgepad"
        const val BUFFER_BYTES = 64 * 1024
        val PICTURES = "${Environment.DIRECTORY_PICTURES}/Edgepad"
        val MOVIES = "${Environment.DIRECTORY_MOVIES}/Edgepad"
    }
}
