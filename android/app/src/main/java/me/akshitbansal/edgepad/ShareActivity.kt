package me.akshitbansal.edgepad

import android.app.Activity
import android.app.AlertDialog
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.util.Log
import android.widget.Toast
import me.akshitbansal.edgepad.link.AndroidClipboard
import me.akshitbansal.edgepad.link.LinkHooks
import me.akshitbansal.edgepad.link.ShareTarget
import me.akshitbansal.edgepad.link.SharedText
import me.akshitbansal.edgepad.link.preview
import me.akshitbansal.edgepad.link.sharedText
import me.akshitbansal.edgepad.protocol.BlobKind
import java.io.IOException
import kotlin.concurrent.thread

/**
 * Share > Edgepad: one text or one image another app hands over, sent to the connected laptop through the clipboard's
 * own path. Exported, so any app can start it, which is why it does nothing without a tap: it reads only
 * EXTRA_TEXT or EXTRA_STREAM of a single ACTION_SEND, shows what it would send and to whom, and sends only after Send.
 * The image is read on a worker, through the content resolver, under the clipboard's size cap; no URI permission is
 * kept past this screen. Anything else finishes at once.
 */
class ShareActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        // A rebuild (rotation) lost the dialog and nothing was sent: the share is simply over.
        if (savedInstanceState != null || intent?.action != Intent.ACTION_SEND) {
            finish()
            return
        }
        val type = intent.type.orEmpty()
        // Any app can start this. On API 31-32 the first extra read unparcels them all, so an extra of a class
        // Edgepad does not have throws (BadParcelableException) before anything is shown. That ends the share,
        // not the app.
        val text: CharSequence?
        val image: Uri?
        try {
            text = if (type == TEXT_TYPE) intent.getCharSequenceExtra(Intent.EXTRA_TEXT) else null
            image = if (type.startsWith(IMAGE_PREFIX)) streamOf(intent) else null
        } catch (e: RuntimeException) {
            Log.w("Edgepad", "The share's extras could not be read", e)
            finish()
            return
        }
        val target = LinkHooks.shareTarget?.invoke()
        when {
            text == null && image == null -> {
                notice(getString(R.string.share_nothing))
            }

            target == null -> {
                notice(getString(R.string.share_no_laptop))
            }

            target.send == null -> {
                notice(getString(R.string.share_clipboard_off, target.name))
            }

            text != null -> {
                confirmText(text, target)
            }

            else -> {
                confirmImage(image, target)
            }
        }
    }

    private fun confirmText(
        text: CharSequence,
        target: ShareTarget,
    ) {
        when (val shared = sharedText(text)) {
            SharedText.Blank -> {
                notice(getString(R.string.share_nothing))
            }

            SharedText.TooLong -> {
                notice(getString(R.string.share_text_too_long))
            }

            is SharedText.Ok -> {
                confirm(preview(text), target) { deliver(target, BlobKind.CLIP_TEXT) { shared.bytes } }
            }
        }
    }

    private fun confirmImage(
        uri: Uri?,
        target: ShareTarget,
    ) {
        if (uri == null) return notice(getString(R.string.share_nothing))
        confirm(getString(R.string.share_image), target) {
            deliver(target, BlobKind.CLIP_IMAGE) { AndroidClipboard(applicationContext).readImage(uri)?.data }
        }
    }

    private fun confirm(
        what: String,
        target: ShareTarget,
        send: () -> Unit,
    ) {
        var tapped = false
        AlertDialog
            .Builder(this)
            .setTitle(getString(R.string.share_title, target.name))
            .setMessage(what)
            .setPositiveButton(R.string.share_send) { _, _ ->
                tapped = true
                send()
            }.setNegativeButton(android.R.string.cancel, null)
            .setOnDismissListener { if (!tapped) finish() }
            .show()
    }

    /** Reads on a worker, sends there too (a transfer is paced), and answers on the main thread. */
    private fun deliver(
        target: ShareTarget,
        kind: BlobKind,
        read: () -> ByteArray?,
    ) {
        val send = target.send ?: return finish()
        thread(name = "edgepad-share") {
            val message =
                try {
                    val data = read()
                    when {
                        data == null -> getString(R.string.share_image_failed)
                        send(kind, data) -> getString(R.string.share_sent, target.name)
                        else -> getString(R.string.share_send_failed, target.name)
                    }
                } catch (e: IOException) {
                    Log.w("Edgepad", "Could not read what was shared", e)
                    getString(R.string.share_image_failed)
                } catch (e: RuntimeException) {
                    // The sharing app's provider runs across binder: a revoked grant (SecurityException), a deleted
                    // file or a bad image can come back as any runtime exception, and uncaught here it would end the
                    // whole process, every laptop link with it.
                    Log.w("Edgepad", "Could not read what was shared", e)
                    getString(R.string.share_image_failed)
                }
            runOnUiThread {
                Toast.makeText(applicationContext, message, Toast.LENGTH_SHORT).show()
                finish()
            }
        }
    }

    private fun notice(message: String) {
        AlertDialog
            .Builder(this)
            .setMessage(message)
            .setPositiveButton(android.R.string.ok, null)
            .setOnDismissListener { finish() }
            .show()
    }

    private fun streamOf(intent: Intent): Uri? =
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            intent.getParcelableExtra(Intent.EXTRA_STREAM, Uri::class.java)
        } else {
            @Suppress("DEPRECATION")
            intent.getParcelableExtra(Intent.EXTRA_STREAM)
        }

    private companion object {
        const val TEXT_TYPE = "text/plain"
        const val IMAGE_PREFIX = "image/"
    }
}
