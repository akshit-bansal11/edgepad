package me.akshitbansal.edgepad.protocol

import java.util.UUID

object ProtocolConstants {
    /**
     * Bumped with every change to the wire format (3 since 0.6.0, which added typed text). Both apps
     * refuse any other version at the handshake, so a phone and a laptop from different releases say so
     * instead of misreading each other. protocol/actions.txt holds the same number; a test checks it.
     */
    const val VERSION = 3

    /** Sent in HELLO so the laptop refuses a stray connection before anything runs. */
    const val MAGIC = "EDGP"

    /** The RFCOMM service class id both apps agree on. Must match the Windows side. */
    val SERVICE_ID: UUID = UUID.fromString("758bb618-7b72-4cd3-9aa2-c9b88e54d555")
}
