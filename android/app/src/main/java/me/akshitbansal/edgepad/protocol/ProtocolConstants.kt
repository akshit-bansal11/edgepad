package me.akshitbansal.edgepad.protocol

import java.util.UUID

object ProtocolConstants {
    const val VERSION = 1

    /** Sent in HELLO so the laptop refuses a stray connection before anything runs. */
    const val MAGIC = "EDGP"

    /** The RFCOMM service class id both apps agree on. Must match the Windows side. */
    val SERVICE_ID: UUID = UUID.fromString("758bb618-7b72-4cd3-9aa2-c9b88e54d555")
}
