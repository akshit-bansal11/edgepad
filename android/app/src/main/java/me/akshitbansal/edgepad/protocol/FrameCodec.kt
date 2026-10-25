package me.akshitbansal.edgepad.protocol

import java.io.StreamCorruptedException
import java.nio.ByteBuffer
import java.nio.ByteOrder

/**
 * Frame layout: one type byte, then a payload whose length is fixed by the type, except TEXT, whose two
 * header bytes (kind, length) say how much UTF-8 follows. Little-endian. protocol/frames.txt holds the
 * golden bytes both apps are tested against.
 */
object FrameCodec {
    const val MAX_TEXT_BYTES = 255
    const val TEXT_HEADER_LENGTH = 2
    const val MAX_FRAME_LENGTH = 1 + TEXT_HEADER_LENGTH + MAX_TEXT_BYTES

    /** What [payloadLength] returns for TEXT: read the header, then as many bytes as it says. */
    const val LENGTH_PREFIXED = -2

    private const val HELLO = 0x01
    private const val HELLO_ACK = 0x02
    private const val PIN_REQUEST = 0x03
    private const val PIN_ANSWER = 0x04
    private const val MOVE = 0x10
    private const val BUTTON = 0x11
    private const val SCROLL = 0x12
    private const val ZOOM = 0x13
    private const val RUN_ACTION = 0x20
    private const val SET_VALUE = 0x21
    private const val PING = 0x30
    private const val PONG = 0x31
    private const val STATE = 0x40
    private const val TEXT = 0x41
    private const val KEY = 0x22
    private const val PAD_STATE = 0x23
    private const val PEN = 0x24
    private const val FILE_BEGIN = 0x50
    private const val FILE_CHUNK = 0x51
    private const val FILE_END = 0x52
    private const val FILE_RESULT = 0x53

    /** FILE_BEGIN's size field, ahead of the name in the length-prefixed part. */
    private const val FILE_SIZE_BYTES = 4

    /** The most of a name one FILE_BEGIN carries: what is left of the 255 bytes after the size. */
    const val MAX_FILE_NAME_BYTES = MAX_TEXT_BYTES - FILE_SIZE_BYTES

    /** The most one FILE_CHUNK carries, the same 255 bytes a TEXT does, so no frame grows past [MAX_FRAME_LENGTH]. */
    const val MAX_CHUNK_BYTES = MAX_TEXT_BYTES

    const val SHA256_BYTES = 32

    private const val MAX_U32 = 0xFFFF_FFFFL

    /** The first code points that take two and three bytes of UTF-8; four starts at the supplementary planes. */
    private const val UTF8_ONE_BYTE_LIMIT = 0x80
    private const val UTF8_TWO_BYTE_LIMIT = 0x800

    /**
     * Payload length for a type byte, [LENGTH_PREFIXED] for TEXT, FILE_BEGIN and FILE_CHUNK, or -1 when the type
     * is unknown. The two file frames borrow TEXT's header shape, an id and then a length, so a reader frames them
     * without knowing what they are.
     *
     * PAD_STATE is the longest fixed payload at 12 bytes, so 13 on the wire — well inside [MAX_FRAME_LENGTH],
     * which a 255-byte TEXT still sets at 258. Nothing about the buffers had to move to let the pad in.
     */
    fun payloadLength(type: Int): Int =
        when (type) {
            HELLO -> 5
            HELLO_ACK, RUN_ACTION, FILE_RESULT -> 1
            PIN_REQUEST -> 3
            PEN -> 5
            PIN_ANSWER -> Pin.LENGTH
            BUTTON, ZOOM, SET_VALUE -> 2
            STATE, KEY -> 3
            MOVE, SCROLL -> 4
            PING, PONG -> 8
            PAD_STATE -> 12
            FILE_END -> SHA256_BYTES
            TEXT, FILE_BEGIN, FILE_CHUNK -> LENGTH_PREFIXED
            else -> -1
        }

    fun encode(frame: Frame): ByteArray {
        val out = ByteArray(MAX_FRAME_LENGTH)
        return out.copyOf(encode(frame, out, 0))
    }

    /** Writes [frame] into [out] at [offset] and returns the number of bytes written. */
    fun encode(
        frame: Frame,
        out: ByteArray,
        offset: Int,
    ): Int {
        val b = ByteBuffer.wrap(out, offset, minOf(MAX_FRAME_LENGTH, out.size - offset)).order(ByteOrder.LITTLE_ENDIAN)
        when (frame) {
            is Frame.Hello -> {
                b.type(HELLO).put(magicBytes).u8(frame.version)
            }

            is Frame.HelloAck -> {
                b.type(HELLO_ACK).u8(frame.version)
            }

            is Frame.Move -> {
                b.type(MOVE).i16(frame.dx).i16(frame.dy)
            }

            is Frame.PointerButton -> {
                b.type(BUTTON).u8(frame.id).u8(if (frame.down) 1 else 0)
            }

            is Frame.Scroll -> {
                b.type(SCROLL).i16(frame.dx).i16(frame.dy)
            }

            is Frame.Zoom -> {
                b.type(ZOOM).i16(frame.delta)
            }

            is Frame.RunAction -> {
                b.type(RUN_ACTION).u8(frame.id)
            }

            is Frame.SetValue -> {
                b.type(SET_VALUE).u8(frame.control).u8(frame.value)
            }

            is Frame.Ping -> {
                b.type(PING).putLong(frame.time)
            }

            is Frame.Pong -> {
                b.type(PONG).putLong(frame.time)
            }

            is Frame.StateReport -> {
                b
                    .type(STATE)
                    .u8(frame.control)
                    .u8(frame.value)
                    .u8(frame.flags)
            }

            is Frame.Text -> {
                val bytes = truncate(frame.text).toByteArray(Charsets.UTF_8)
                b
                    .type(TEXT)
                    .u8(frame.kind)
                    .u8(bytes.size)
                    .put(bytes)
            }

            is Frame.Key -> {
                b.type(KEY).u16(frame.code).u8(if (frame.down) 1 else 0)
            }

            // The XINPUT_GAMEPAD field order exactly: wButtons, bLeftTrigger, bRightTrigger, then the four
            // thumb axes. Little-endian like everything else, which is also the order the struct is in
            // memory on the laptop, so the payload lands in it whole.
            is Frame.PadState -> {
                b
                    .type(PAD_STATE)
                    .u16(frame.buttons)
                    .u8(frame.lt)
                    .u8(frame.rt)
                    .i16(frame.lx)
                    .i16(frame.ly)
                    .i16(frame.rx)
                    .i16(frame.ry)
            }

            is Frame.PinRequest -> {
                b.type(PIN_REQUEST).u8(frame.attemptsLeft).u16(frame.lockedSeconds)
            }

            is Frame.Pen -> {
                b
                    .type(PEN)
                    .u16(frame.x)
                    .u16(frame.y)
                    .u8(frame.state)
            }

            // TEXT's shape: the type id, then one length byte counting the size and the name together.
            is Frame.FileBegin -> {
                require(frame.size in 0..MAX_U32) { "u32 out of range: ${frame.size}" }
                val name = truncate(frame.name, MAX_FILE_NAME_BYTES).toByteArray(Charsets.UTF_8)
                b
                    .type(FILE_BEGIN)
                    .u8(frame.type)
                    .u8(FILE_SIZE_BYTES + name.size)
                    .putInt(frame.size.toInt())
                    .put(name)
            }

            is Frame.FileChunk -> {
                require(frame.data.size <= MAX_CHUNK_BYTES) { "A chunk is at most $MAX_CHUNK_BYTES bytes" }
                b
                    .type(FILE_CHUNK)
                    .u8(frame.seq)
                    .u8(frame.data.size)
                    .put(frame.data)
            }

            is Frame.FileEnd -> {
                require(frame.sha256.size == SHA256_BYTES) { "A SHA-256 is $SHA256_BYTES bytes" }
                b.type(FILE_END).put(frame.sha256)
            }

            is Frame.FileResult -> {
                b.type(FILE_RESULT).u8(frame.status)
            }

            // Each digit goes out as its value 0..9, not its ASCII code, so these bytes can never be read as text.
            is Frame.PinAnswer -> {
                b.type(PIN_ANSWER)
                for (i in 0 until Pin.LENGTH) b.u8(frame.pin.digitAt(i))
            }
        }
        return b.position() - offset
    }

    /** Decodes one frame. Anything malformed throws, and the caller drops the connection. */
    fun decode(
        type: Int,
        payload: ByteArray,
    ): Frame {
        val expected = payloadLength(type)
        if (expected == LENGTH_PREFIXED) return decodePrefixed(type, payload)
        if (expected < 0) throw StreamCorruptedException("Unknown frame type 0x%02x".format(type))
        if (payload.size != expected) {
            throw StreamCorruptedException(
                "Frame 0x%02x needs $expected payload bytes, got ${payload.size}".format(type),
            )
        }
        val b = ByteBuffer.wrap(payload).order(ByteOrder.LITTLE_ENDIAN)
        return when (type) {
            HELLO -> decodeHello(b)
            HELLO_ACK -> Frame.HelloAck(b.u8())
            MOVE -> Frame.Move(b.short.toInt(), b.short.toInt())
            BUTTON -> Frame.PointerButton(b.u8(), flag(b.u8()))
            SCROLL -> Frame.Scroll(b.short.toInt(), b.short.toInt())
            ZOOM -> Frame.Zoom(b.short.toInt())
            RUN_ACTION -> Frame.RunAction(b.u8())
            SET_VALUE -> Frame.SetValue(b.u8(), b.u8())
            PING -> Frame.Ping(b.long)
            PONG -> Frame.Pong(b.long)
            STATE -> Frame.StateReport(b.u8(), b.u8(), b.u8())
            KEY -> Frame.Key(b.u16(), flag(b.u8()))
            PAD_STATE -> decodePadState(b)
            PIN_REQUEST -> Frame.PinRequest(b.u8(), b.u16())
            PEN -> Frame.Pen(b.u16(), b.u16(), b.u8())
            PIN_ANSWER -> decodePinAnswer(b)
            FILE_END -> Frame.FileEnd(payload.copyOf())
            FILE_RESULT -> Frame.FileResult(b.u8())
            else -> throw StreamCorruptedException("Unknown frame type 0x%02x".format(type))
        }
    }

    /**
     * The longest prefix of [text] made of whole code points whose UTF-8 is at most [maxBytes] bytes.
     * The Windows codec runs the same walk, so both apps cut a long name at the same byte.
     *
     * One pass over the code points, adding up what each would cost in UTF-8. It used to drop one UTF-16
     * unit at a time and re-encode the lot, which was quadratic and, when an emoji straddled the limit, left
     * half a surrogate pair at the end: the encoder wrote that out as '?', a character nobody typed. Walking
     * whole code points means a pair is kept or dropped together. A lone surrogate already in [text] is
     * counted as the three bytes of the U+FFFD the Windows encoder writes for it, so the two agree on where
     * to cut even then.
     */
    fun truncate(
        text: String,
        maxBytes: Int = MAX_TEXT_BYTES,
    ): String {
        var bytes = 0
        var end = 0
        while (end < text.length) {
            val point = text.codePointAt(end)
            bytes += utf8Length(point)
            if (bytes > maxBytes) return text.substring(0, end)
            end += Character.charCount(point)
        }
        return text
    }

    private fun utf8Length(point: Int): Int =
        when {
            point < UTF8_ONE_BYTE_LIMIT -> 1
            point < UTF8_TWO_BYTE_LIMIT -> 2
            point < Character.MIN_SUPPLEMENTARY_CODE_POINT -> 3
            else -> 4
        }

    private val magicBytes = ProtocolConstants.MAGIC.toByteArray(Charsets.US_ASCII)

    /**
     * TEXT, FILE_BEGIN and FILE_CHUNK: an id byte, a length byte, then that many bytes. The length is checked once
     * here for all three, so a header that lies closes the link whatever frame it heads.
     */
    private fun decodePrefixed(
        type: Int,
        payload: ByteArray,
    ): Frame {
        if (payload.size < TEXT_HEADER_LENGTH || payload.size != TEXT_HEADER_LENGTH + (payload[1].toInt() and 0xFF)) {
            throw StreamCorruptedException("Frame 0x%02x length does not match its header".format(type))
        }
        val id = payload[0].toInt() and 0xFF
        return when (type) {
            FILE_BEGIN -> decodeFileBegin(id, payload)
            FILE_CHUNK -> Frame.FileChunk(id, payload.copyOfRange(TEXT_HEADER_LENGTH, payload.size))
            else -> Frame.Text(id, utf8(payload, TEXT_HEADER_LENGTH))
        }
    }

    /** The size, then the name. Shorter than the size field is malformed; an empty name is the phone's to refuse. */
    private fun decodeFileBegin(
        type: Int,
        payload: ByteArray,
    ): Frame.FileBegin {
        if (payload.size < TEXT_HEADER_LENGTH + FILE_SIZE_BYTES) {
            throw StreamCorruptedException("FILE_BEGIN is too short to hold a size")
        }
        val size = ByteBuffer.wrap(payload, TEXT_HEADER_LENGTH, FILE_SIZE_BYTES).order(ByteOrder.LITTLE_ENDIAN).int
        return Frame.FileBegin(type, size.toLong() and MAX_U32, utf8(payload, TEXT_HEADER_LENGTH + FILE_SIZE_BYTES))
    }

    private fun utf8(
        bytes: ByteArray,
        from: Int,
    ): String = String(bytes, from, bytes.size - from, Charsets.UTF_8)

    /**
     * Nothing here can be out of range, so nothing here throws. Every field spans its whole width — the
     * triggers are 0..255 and the four axes -32768..32767 — and an undefined button bit is an unknown id,
     * which this protocol drops and counts rather than treating as an error. That is the same line BUTTON
     * draws: its id may be anything and is dropped downstream, while its `down` byte throws, because a flag
     * with two legal values out of 256 means a corrupt stream and not a newer app. A wrong payload length
     * still closes the connection, as it does for every type.
     */
    private fun decodePadState(b: ByteBuffer): Frame.PadState =
        Frame.PadState(b.u16(), b.u8(), b.u8(), b.short.toInt(), b.short.toInt(), b.short.toInt(), b.short.toInt())

    /**
     * A byte above 9 is not a digit. It closes the link like a bad flag byte, and the laptop does not count it as a
     * failed attempt, since it tests no PIN. The message names no value: a rejected byte is still part of somebody's
     * PIN.
     */
    private fun decodePinAnswer(b: ByteBuffer): Frame.PinAnswer {
        val digits = StringBuilder()
        repeat(Pin.LENGTH) {
            val digit = b.u8()
            if (digit > 9) throw StreamCorruptedException("PIN_ANSWER carries a byte that is not a digit")
            digits.append('0' + digit)
        }
        return Frame.PinAnswer(Pin(digits.toString()))
    }

    private fun decodeHello(b: ByteBuffer): Frame.Hello {
        val magic = ByteArray(magicBytes.size).also { b.get(it) }
        if (!magic.contentEquals(magicBytes)) {
            throw StreamCorruptedException("HELLO without the Edgepad magic")
        }
        return Frame.Hello(b.u8())
    }

    private fun flag(value: Int): Boolean =
        when (value) {
            0 -> false
            1 -> true
            else -> throw StreamCorruptedException("Flag byte must be 0 or 1, got $value")
        }

    private fun ByteBuffer.type(type: Int): ByteBuffer = put(type.toByte())

    private fun ByteBuffer.u8(value: Int): ByteBuffer {
        require(value in 0..0xFF) { "u8 out of range: $value" }
        return put(value.toByte())
    }

    private fun ByteBuffer.i16(value: Int): ByteBuffer {
        require(value in Short.MIN_VALUE..Short.MAX_VALUE) { "i16 out of range: $value" }
        return putShort(value.toShort())
    }

    private fun ByteBuffer.u16(value: Int): ByteBuffer {
        require(value in 0..0xFFFF) { "u16 out of range: $value" }
        return putShort(value.toShort())
    }

    private fun ByteBuffer.u8(): Int = get().toInt() and 0xFF

    private fun ByteBuffer.u16(): Int = short.toInt() and 0xFFFF
}
