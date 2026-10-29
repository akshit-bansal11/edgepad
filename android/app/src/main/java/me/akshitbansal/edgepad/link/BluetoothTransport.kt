package me.akshitbansal.edgepad.link

import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothSocket
import me.akshitbansal.edgepad.link.net.Sas
import me.akshitbansal.edgepad.protocol.ProtocolConstants
import java.io.IOException

/**
 * An RFCOMM socket to a bonded laptop. Bluetooth pairing already encrypts the link, so nothing runs in front of
 * the frames and [open] never shows a code. A missing Nearby devices permission comes out as a SecurityException.
 */
class BluetoothTransport(
    private val device: BluetoothDevice,
) : LinkTransport {
    @Volatile private var socket: BluetoothSocket? = null

    @Volatile private var closed = false

    override fun open(
        showCode: (Sas) -> Unit,
        confirmed: () -> Boolean,
    ): Streams {
        val s = device.createRfcommSocketToServiceRecord(ProtocolConstants.SERVICE_ID)
        socket = s
        // A close() that ran before the line above found no socket to close.
        if (closed) {
            s.close()
            throw IOException("Closed before connecting")
        }
        s.connect()
        return Streams(s.inputStream, s.outputStream)
    }

    override fun close() {
        closed = true
        try {
            socket?.close()
        } catch (e: IOException) {
            // Already broken; closing is all that was asked for.
        }
    }
}
