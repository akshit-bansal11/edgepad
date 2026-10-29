package me.akshitbansal.edgepad.link

import android.bluetooth.BluetoothDevice
import me.akshitbansal.edgepad.link.net.Sas
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream
import java.net.InetAddress

/** The byte pipe a [LaptopLink] runs its frames over, once a [LinkTransport] has opened it. */
class Streams(
    val input: InputStream,
    val output: OutputStream,
)

/**
 * How a [LaptopLink] reaches its laptop: Bluetooth RFCOMM, or TCP over Wi-Fi or USB tethering with the EDGL1
 * handshake in front. [open] blocks on the link's thread until the frames can flow. An [IOException] from it that
 * is not a [LinkFailure] means the laptop was never reached. [close] may run on any thread at any time, before or
 * during [open], and ends it; it is also how a pairing in progress is cancelled.
 */
interface LinkTransport {
    /**
     * [showCode] is called on this thread when a pairing needs the owner to type a code on the laptop, and
     * [confirmed] once the laptop has answered, blocking until the owner says on the phone whether the laptop
     * accepted the code. Nothing is kept unless it returns true.
     */
    fun open(
        showCode: (Sas) -> Unit,
        confirmed: () -> Boolean,
    ): Streams

    fun close()
}

/** A transport that reached the laptop and was turned away, with the reason the user is shown. */
class LinkFailure(
    val closure: LaptopLink.Closure,
) : IOException(closure.cause.name)

/** What a connect is aimed at. [key] is what the picker marks as connecting and the activity compares laptops by. */
sealed interface Target {
    val key: String

    class Bluetooth(
        val device: BluetoothDevice,
    ) : Target {
        override val key: String get() = device.address
    }

    /**
     * [pairAgain] pairs even when a key for [laptopId] is stored, for the user's "Pair again": a stored key is only
     * ever replaced by a pairing its owner confirmed, never dropped because the network said so.
     */
    class Network(
        val laptopId: ByteArray,
        val name: String,
        val hosts: List<InetAddress>,
        val port: Int,
        val pairAgain: Boolean = false,
    ) : Target {
        override val key: String = "net:" + laptopId.joinToString("") { "%02x".format(it) }
    }
}
