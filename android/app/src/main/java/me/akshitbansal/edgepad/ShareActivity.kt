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
import me.akshitbansal.edgepad.link.xfer.Transfers
import me.akshitbansal.edgepad.link.xfer.XferPhase
import me.akshitbansal.edgepad.protocol.BlobKind
import me.akshitbansal.edgepad.screens.CardTitle
import me.akshitbansal.edgepad.screens.Ui
import me.akshitbansal.edgepad.screens.XferCardView
import me.akshitbansal.edgepad.screens.titleKind
import java.io.IOException
import kotlin.concurrent.thread

/**
 * Share > Edgepad: one text or one image another app hands over, sent to the connected laptop through the clipboard's
 * own path, or any other file, sent as a file transfer. Exported, so any app can start it, which is why it does nothing
 * without a tap: it reads only EXTRA_TEXT or EXTRA_STREAM of one ACTION_SEND or ACTION_SEND_MULTIPLE, shows what it
 * would send and to whom, and sends only after Send. The image is read on a worker, through the content resolver,
 * under the clipboard's size cap; a file is streamed on a worker. No URI permission is kept past this screen. Anything
 * else finishes at once.
 */
class ShareActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        // A rebuild (rotation) lost the dialog and nothing was sent: the share is simply over.
        val action = intent?.action
        val many = action == Intent.ACTION_SEND_MULTIPLE
        if (savedInstanceState != null || (action != Intent.ACTION_SEND && !many)) {
            finish()
            return
        }
        val type = intent.type.orEmpty()
        // Any app can start this. On API 31-32 the first extra read unparcels them all, so an extra of a class
        // Edgepad does not have throws (BadParcelableException) before anything is shown. That ends the share,
        // not the app.
        val text: CharSequence?
        val image: Uri?
        val file: Uri?
        val files: List<Uri>
        try {
            files = if (many) limitShared(streamsOf(intent)) else emptyList()
            text = if (!many && type == TEXT_TYPE) intent.getCharSequenceExtra(Intent.EXTRA_TEXT) else null
            image = if (!many && type.startsWith(IMAGE_PREFIX)) streamOf(intent) else null
            // Anything else with a stream: a document, a recording, a text file.
            file = if (!many && text == null && image == null) streamOf(intent) else null
        } catch (e: RuntimeException) {
            Log.w("Edgepad", "The share's extras could not be read", e)
            finish()
            return
        }
        val target = LinkHooks.shareTarget?.invoke()
        when {
            text == null && image == null && file == null && files.isEmpty() -> {
                notice(getString(R.string.share_nothing))
            }

            target == null -> {
                notice(getString(R.string.share_no_laptop))
            }

            files.isNotEmpty() -> {
                confirmFiles(files, target)
            }

            file != null -> {
                confirmFiles(listOf(file), target)
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
                val (shown, lines, notShown) = preview(text)
                // The line count always: whatever the preview cannot show, it says how much is really sent.
                val size = resources.getQuantityString(R.plurals.share_lines, lines, lines)
                val counted =
                    if (notShown == 0) {
                        size
                    } else {
                        getString(
                            R.string.sentences_joined,
                            size,
                            resources.getQuantityString(R.plurals.share_not_shown, notShown, notShown),
                        )
                    }
                confirm(shown + "\n\n" + counted, target) { deliver(target, BlobKind.CLIP_TEXT) { shared.bytes } }
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

    /**
     * The names are asked of the sharing app's provider on a worker, then the same question as for text: Send to whom.
     * Several files show their count and the first few names.
     */
    private fun confirmFiles(
        uris: List<Uri>,
        target: ShareTarget,
    ) {
        val sendFiles = target.sendFiles ?: return notice(getString(R.string.share_no_laptop))
        thread(name = "edgepad-share-name") {
            val names =
                try {
                    uris.take(NAMES_SHOWN).map { Transfers.displayName(applicationContext, it) }
                } catch (e: RuntimeException) {
                    // A revoked grant or a dead provider comes back across binder as any runtime exception.
                    Log.w("Edgepad", "Could not read the shared file's name", e)
                    null
                }
            runOnUiThread {
                if (isFinishing || isDestroyed) return@runOnUiThread
                if (names == null || names.any { it == null }) {
                    notice(getString(R.string.share_nothing))
                    return@runOnUiThread
                }
                val what =
                    if (uris.size == 1) {
                        names.first().orEmpty()
                    } else {
                        val count = resources.getQuantityString(R.plurals.xfer_share_count, uris.size, uris.size)
                        val more =
                            if (uris.size > NAMES_SHOWN) {
                                val extra = uris.size - NAMES_SHOWN
                                listOf(resources.getQuantityString(R.plurals.xfer_card_more, extra, extra))
                            } else {
                                emptyList()
                            }
                        (listOf(count) + names.filterNotNull() + more).joinToString("\n")
                    }
                confirm(what, target) { deliverFiles(target, uris, sendFiles, target.retryFailed) }
            }
        }
    }

    /**
     * Sends on a worker, counted as a share so the app leaving does not end the link under it. This activity stays
     * until the batch is over, since the sharing app's grants die with it, and shows the transfer card in a dialog.
     */
    private fun deliverFiles(
        target: ShareTarget,
        uris: List<Uri>,
        sendFiles: (List<Uri>) -> Boolean,
        retryFailed: (() -> Boolean)?,
    ) {
        val progress = Transfers.progress
        // A finished card left by an earlier batch must not be taken for this one.
        if (progress.snapshot.phase == XferPhase.FINISHED) progress.dismiss()
        Type.load(this)
        val ui = Ui(this)
        val send = { sendOnWorker { sendFiles(uris) } }
        val retry = { sendOnWorker { retryFailed?.invoke() ?: false } }
        val card = XferCardView(ui, onCancel = Transfers::cancel, onRetry = retry, onDone = progress::dismiss)
        val autoDone = Runnable { progress.dismiss() }
        var dialog: AlertDialog? = null
        progress.listener = { s ->
            when {
                isFinishing || isDestroyed -> {
                    Unit
                }

                s.phase == XferPhase.IDLE -> {
                    // Done, or auto-dismissed: the share is over.
                    dialog?.dismiss()
                    finish()
                }

                else -> {
                    card.bind(s, target.name)
                    if (dialog == null) {
                        dialog =
                            AlertDialog
                                .Builder(this)
                                .setView(card)
                                .setCancelable(false)
                                .show()
                    }
                    card.removeCallbacks(autoDone)
                    if (s.phase == XferPhase.FINISHED && titleKind(s) == CardTitle.DONE_ALL) {
                        card.postDelayed(autoDone, AUTO_DONE_MS)
                    }
                }
            }
        }
        send()
    }

    private fun sendOnWorker(work: () -> Boolean) {
        // Counted from before the thread starts, so a linger running out in between still sees it.
        LinkHooks.sharing.incrementAndGet()
        thread(name = "edgepad-share") {
            try {
                work()
            } catch (e: RuntimeException) {
                Log.w("Edgepad", "Could not send what was shared", e)
            } finally {
                LinkHooks.sharing.decrementAndGet()
            }
        }
    }

    override fun onDestroy() {
        super.onDestroy()
        Transfers.progress.listener = null
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
        // Counted from before the thread starts, so a linger running out in between still sees it.
        LinkHooks.sharing.incrementAndGet()
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
                } finally {
                    LinkHooks.sharing.decrementAndGet()
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
            .setPositiveButton(R.string.editor_done, null)
            .setOnDismissListener { finish() }
            .show()
    }

    private fun streamsOf(intent: Intent): List<Uri>? =
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            intent.getParcelableArrayListExtra(Intent.EXTRA_STREAM, Uri::class.java)
        } else {
            @Suppress("DEPRECATION")
            intent.getParcelableArrayListExtra(Intent.EXTRA_STREAM)
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
        const val NAMES_SHOWN = 3
        const val AUTO_DONE_MS = 5_000L
    }
}

/** The documents a multiple share carries: none for null, no repeats, at most [max], in the order given. */
internal fun <T> limitShared(
    items: List<T>?,
    max: Int = 100,
): List<T> = items.orEmpty().distinct().take(max)
