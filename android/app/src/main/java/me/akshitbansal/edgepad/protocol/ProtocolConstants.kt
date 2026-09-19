package me.akshitbansal.edgepad.protocol

import java.util.UUID

object ProtocolConstants {
    /**
     * Bumped only when the wire format changes in a way an older build cannot survive. Both apps refuse
     * any other version at the handshake, so a phone and a laptop from different releases say so instead
     * of misreading each other. protocol/actions.txt holds the same number; a test checks it.
     *
     * Still 3 after the refresh-rate dial and the macro buttons, which is deliberate. Both are new ids in
     * tables that already existed: an unknown control, action or text kind is dropped and counted, never
     * an error, so an older build meets them by ignoring them rather than by breaking. Only a new frame
     * TYPE would need a bump, because an unknown type closes the connection. From 1.0 this number moves
     * only in a major release, and nothing here earned one.
     */
    const val VERSION = 3

    /** Sent in HELLO so the laptop refuses a stray connection before anything runs. */
    const val MAGIC = "EDGP"

    /** The RFCOMM service class id both apps agree on. Must match the Windows side. */
    val SERVICE_ID: UUID = UUID.fromString("758bb618-7b72-4cd3-9aa2-c9b88e54d555")
}
