package me.akshitbansal.edgepad.screens

import me.akshitbansal.edgepad.protocol.ActionId

/** What a key does once Fn has been applied: a Windows virtual-key code, a phone action, or nothing. */
sealed interface KeyOut {
    data class Code(
        val code: Int,
    ) : KeyOut

    data class Act(
        val action: ActionId,
    ) : KeyOut

    object None : KeyOut
}

enum class KeyRole { NORMAL, MODIFIER, FN }

/**
 * One key. [label] names [base], [fnLabel] names [fn]. A [swappable] key is one the Function keys setting
 * flips (the F row on sizes that have one, the number row on 65 and 60 percent). A tall key declares
 * [rows] = 2 in its top row and a [Gap] of its own width sits under it in the next.
 */
data class Cap(
    val label: String,
    val units: Float,
    val base: KeyOut,
    val fn: KeyOut? = null,
    val fnLabel: String? = null,
    val swappable: Boolean = false,
    val role: KeyRole = KeyRole.NORMAL,
    val rows: Int = 1,
    val oneShot: Boolean = false,
) : Item

data class Gap(
    val units: Float,
) : Item

sealed interface Item

enum class KeyboardSize(
    val percent: Int,
) {
    FULL(100),
    NINETY_SIX(96),
    EIGHTY(80),
    SEVENTY_FIVE(75),
    SIXTY_FIVE(65),
    SIXTY(60),
}

enum class FnMode { WITH_FN, DIRECT }

class Layout(
    val size: KeyboardSize,
    val width: Float,
    val rows: List<List<Item>>,
)

private typealias Fns = Map<String, Pair<Int, String>>

private const val CODE_ESC = 0x1B
private const val CODE_TAB = 0x09
private const val CODE_BACKSPACE = 0x08
private const val CODE_CAPS = 0x14
private const val CODE_ENTER = 0x0D
private const val CODE_SPACE = 0x20
private const val CODE_BACKTICK = 0xC0
private const val CODE_MINUS = 0xBD
private const val CODE_EQUALS = 0xBB
private const val CODE_ZERO = 0x30
private const val CODE_ONE = 0x31
private const val CODE_LEFT_BRACKET = 0xDB
private const val CODE_RIGHT_BRACKET = 0xDD
private const val CODE_BACKSLASH = 0xDC
private const val CODE_SEMICOLON = 0xBA
private const val CODE_QUOTE = 0xDE
private const val CODE_COMMA = 0xBC
private const val CODE_PERIOD = 0xBE
private const val CODE_SLASH = 0xBF
private const val CODE_LSHIFT = 0xA0
private const val CODE_RSHIFT = 0xA1
private const val CODE_LCTRL = 0xA2
private const val CODE_RCTRL = 0xA3
private const val CODE_LALT = 0xA4
private const val CODE_RALT = 0xA5
private const val CODE_LWIN = 0x5B
private const val CODE_RWIN = 0x5C
private const val CODE_MENU = 0x5D
private const val CODE_PRTSC = 0x2C
private const val CODE_INSERT = 0x2D
private const val CODE_DELETE = 0x2E
private const val CODE_PGUP = 0x21
private const val CODE_PGDN = 0x22
private const val CODE_END = 0x23
private const val CODE_HOME = 0x24
private const val CODE_LEFT = 0x25
private const val CODE_UP = 0x26
private const val CODE_RIGHT = 0x27
private const val CODE_DOWN = 0x28
private const val CODE_NUMLOCK = 0x90
private const val CODE_F1 = 0x70
private const val CODE_NUM0 = 0x60
private const val CODE_NUMMUL = 0x6A
private const val CODE_NUMADD = 0x6B
private const val CODE_NUMSUB = 0x6D
private const val CODE_NUMDOT = 0x6E
private const val CODE_NUMDIV = 0x6F

private const val W_HALF = 0.5f
private const val W_QUARTER = 0.25f
private const val W1_25 = 1.25f
private const val W1_5 = 1.5f
private const val W1_75 = 1.75f
private const val W2_25 = 2.25f
private const val W2_75 = 2.75f
private const val W6_25 = 6.25f

private const val WIDTH_FULL = 22.75f
private const val WIDTH_96 = 19f
private const val WIDTH_80 = 18.5f
private const val WIDTH_75 = 16f
private const val WIDTH_65 = 16f
private const val WIDTH_60 = 15f

fun layoutFor(size: KeyboardSize): Layout =
    when (size) {
        KeyboardSize.FULL -> full()
        KeyboardSize.NINETY_SIX -> ninetySix()
        KeyboardSize.EIGHTY -> eighty()
        KeyboardSize.SEVENTY_FIVE -> seventyFive()
        KeyboardSize.SIXTY_FIVE -> sixtyFive()
        KeyboardSize.SIXTY -> sixty()
    }

private fun full(): Layout {
    val numpadEnter = key("Enter", CODE_ENTER, rows = 2)
    return Layout(
        KeyboardSize.FULL,
        WIDTH_FULL,
        listOf(
            row(
                escape(),
                g(1f),
                fRow(0, 4),
                g(W_HALF),
                fRow(4, 8),
                g(W_HALF),
                fRow(8, 12),
                g(W_HALF),
                extras(),
                g(4.25f),
            ),
            row(n15(), g(W_HALF), nav3(CODE_INSERT, CODE_HOME, CODE_PGUP), g(W_QUARTER), numLockRow()),
            row(
                t15(),
                g(W_HALF),
                nav3(CODE_DELETE, CODE_END, CODE_PGDN),
                g(W_QUARTER),
                num("7"),
                num("8"),
                num("9"),
                key("+", CODE_NUMADD, rows = 2),
            ),
            row(c15(), g(W_HALF), g(3f), g(W_QUARTER), num("4"), num("5"), num("6"), g(1f)),
            row(
                s15(),
                g(W_HALF),
                g(1f),
                arrow("↑", CODE_UP),
                g(1f),
                g(W_QUARTER),
                num("1"),
                num("2"),
                num("3"),
                numpadEnter,
            ),
            row(
                b15(),
                g(W_HALF),
                arrows3(),
                g(W_QUARTER),
                key("0", CODE_NUM0, 2f),
                key(".", CODE_NUMDOT),
                g(1f),
            ),
        ),
    )
}

private fun ninetySix(): Layout =
    Layout(
        KeyboardSize.NINETY_SIX,
        WIDTH_96,
        listOf(
            row(
                escape(),
                fRow(0, 12),
                key("Del", CODE_DELETE),
                key("Home", CODE_HOME),
                key("End", CODE_END),
                key("PgUp", CODE_PGUP),
                key("PgDn", CODE_PGDN),
                light(),
            ),
            row(n15(), numLockRow()),
            row(t15(), num("7"), num("8"), num("9"), key("+", CODE_NUMADD, rows = 2)),
            row(c15(), num("4"), num("5"), num("6"), g(1f)),
            row(s14(), arrow("↑", CODE_UP), num("1"), num("2"), num("3"), key("Enter", CODE_ENTER, rows = 2)),
            row(b13(), arrows3(), key("0", CODE_NUM0), key(".", CODE_NUMDOT), g(1f)),
        ),
    )

private fun eighty(): Layout =
    Layout(
        KeyboardSize.EIGHTY,
        WIDTH_80,
        listOf(
            row(escape(), g(1f), fRow(0, 4), g(W_HALF), fRow(4, 8), g(W_HALF), fRow(8, 12), g(W_HALF), extras()),
            row(n15(), g(W_HALF), nav3(CODE_INSERT, CODE_HOME, CODE_PGUP)),
            row(t15(), g(W_HALF), nav3(CODE_DELETE, CODE_END, CODE_PGDN)),
            row(c15(), g(3.5f)),
            row(s15(), g(W_HALF), g(1f), arrow("↑", CODE_UP), g(1f)),
            row(b15(), g(W_HALF), arrows3()),
        ),
    )

private fun seventyFive(): Layout =
    Layout(
        KeyboardSize.SEVENTY_FIVE,
        WIDTH_75,
        listOf(
            row(escape(), fRow(0, 12), key("PrtSc", CODE_PRTSC), key("Del", CODE_DELETE), light()),
            row(n15(), key("PgUp", CODE_PGUP)),
            row(t15(), key("PgDn", CODE_PGDN)),
            row(c15(), key("Home", CODE_HOME)),
            row(s14(), arrow("↑", CODE_UP), key("End", CODE_END)),
            row(b13(), arrows3()),
        ),
    )

private fun sixtyFive(): Layout =
    Layout(
        KeyboardSize.SIXTY_FIVE,
        WIDTH_65,
        listOf(
            row(
                key("Esc", CODE_ESC, fns = mapOf("Esc" to (CODE_BACKTICK to "`"))),
                numberKeys(),
                key("Backspace", CODE_BACKSPACE, 2f, mapOf("Backspace" to (CODE_DELETE to "Del"))),
                light(),
            ),
            row(t15(), key("Home", CODE_HOME)),
            row(c15(), key("PgUp", CODE_PGUP)),
            row(s14(), arrow("↑", CODE_UP), key("PgDn", CODE_PGDN)),
            row(b13c(), arrows3()),
        ),
    )

private fun sixty(): Layout {
    val fns: Fns =
        mapOf(
            "Esc" to (CODE_BACKTICK to "`"),
            "I" to (CODE_MENU to "menu"),
            "P" to (CODE_INSERT to "ins"),
            "[" to (CODE_PGUP to "pgup"),
            "]" to (CODE_HOME to "home"),
            "K" to (CODE_UP to "up"),
            ";" to (CODE_PGDN to "pgdn"),
            "'" to (CODE_END to "end"),
            "M" to (CODE_LEFT to "left"),
            "," to (CODE_DOWN to "down"),
            "." to (CODE_RIGHT to "right"),
            "/" to (CODE_DELETE to "del"),
        )
    return Layout(
        KeyboardSize.SIXTY,
        WIDTH_60,
        listOf(
            row(key("Esc", CODE_ESC, fns = fns), numberKeys(), key("Backspace", CODE_BACKSPACE, 2f)),
            row(t15(fns)),
            row(c15(fns)),
            row(s15(fns)),
            row(b15c()),
        ),
    )
}

/** A row from keys, gaps and lists of them, left to right. */
private fun row(vararg parts: Any): List<Item> =
    parts.flatMap { part ->
        when (part) {
            is Item -> listOf(part)
            is List<*> -> part.filterIsInstance<Item>()
            else -> error("not a key or a gap: $part")
        }
    }

private fun g(units: Float) = Gap(units)

private fun key(
    label: String,
    code: Int,
    units: Float = 1f,
    fns: Fns = emptyMap(),
    rows: Int = 1,
): Cap {
    val fn = fns[label]
    return Cap(label, units, KeyOut.Code(code), fn?.let { KeyOut.Code(it.first) }, fn?.second, rows = rows)
}

private fun escape() = key("Esc", CODE_ESC)

private fun arrow(
    label: String,
    code: Int,
) = key(label, code)

private fun arrows3() = listOf(arrow("←", CODE_LEFT), arrow("↓", CODE_DOWN), arrow("→", CODE_RIGHT))

private fun nav3(
    a: Int,
    b: Int,
    c: Int,
) = listOf(key(navLabel(a), a), key(navLabel(b), b), key(navLabel(c), c))

private fun navLabel(code: Int) =
    when (code) {
        CODE_INSERT -> "Ins"
        CODE_DELETE -> "Del"
        CODE_HOME -> "Home"
        CODE_END -> "End"
        CODE_PGUP -> "PgUp"
        else -> "PgDn"
    }

private fun light() = Cap("Light", 1f, KeyOut.None)

private fun extras() =
    listOf(
        key("PrtSc", CODE_PRTSC),
        Cap("Mic", 1f, KeyOut.Act(ActionId.MIC_MUTE_TOGGLE)),
        light(),
    )

private fun num(digit: String) = key(digit, CODE_NUM0 + digit.toInt())

private fun numLockRow() =
    listOf(
        key("NumLk", CODE_NUMLOCK),
        key("/", CODE_NUMDIV),
        key("*", CODE_NUMMUL),
        key("-", CODE_NUMSUB),
    )

private fun fKey(
    n: Int,
    printed: String,
    out: KeyOut,
) = Cap(printed, 1f, out, KeyOut.Code(CODE_F1 + n - 1), "F$n", swappable = true)

private val F_ROW =
    listOf(
        fKey(1, "Bri-", KeyOut.Act(ActionId.BRIGHTNESS_DOWN)),
        fKey(2, "Bri+", KeyOut.Act(ActionId.BRIGHTNESS_UP)),
        fKey(3, "Task", KeyOut.Act(ActionId.TASK_VIEW)),
        fKey(4, "Find", KeyOut.Act(ActionId.SEARCH)),
        fKey(5, "Kbd-", KeyOut.None),
        fKey(6, "Kbd+", KeyOut.None),
        fKey(7, "Prev", KeyOut.Act(ActionId.PREVIOUS_TRACK)),
        fKey(8, "Play", KeyOut.Act(ActionId.PLAY_PAUSE)),
        fKey(9, "Next", KeyOut.Act(ActionId.NEXT_TRACK)),
        fKey(10, "Mute", KeyOut.Act(ActionId.MUTE_TOGGLE)),
        fKey(11, "Vol-", KeyOut.Act(ActionId.VOLUME_DOWN)),
        fKey(12, "Vol+", KeyOut.Act(ActionId.VOLUME_UP)),
    )

private fun fRow(
    from: Int,
    to: Int,
) = F_ROW.subList(from, to)

/** The 12 number-row keys of the 65 and 60 percent boards: the digit by default, F1..F12 on the other side of Fn. */
private fun numberKeys(): List<Cap> {
    val labels = "1234567890-="
    val codes = (CODE_ONE..CODE_ONE + 8) + CODE_ZERO + CODE_MINUS + CODE_EQUALS
    return labels.mapIndexed { i, label ->
        Cap(label.toString(), 1f, KeyOut.Code(codes[i]), KeyOut.Code(CODE_F1 + i), "F${i + 1}", swappable = true)
    }
}

private fun n15(): List<Cap> {
    val digits = (CODE_ONE..CODE_ONE + 8).toList() + CODE_ZERO
    return listOf(key("`", CODE_BACKTICK)) +
        digits.mapIndexed { i, code -> key(if (i == 9) "0" else "${i + 1}", code) } +
        listOf(key("-", CODE_MINUS), key("=", CODE_EQUALS), key("Backspace", CODE_BACKSPACE, 2f))
}

private fun letters(
    chars: String,
    fns: Fns,
) = chars.map { key(it.toString(), 0x41 + (it - 'A'), fns = fns) }

private fun t15(fns: Fns = emptyMap()): List<Cap> =
    listOf(key("Tab", CODE_TAB, W1_5)) +
        letters("QWERTYUIOP", fns) +
        listOf(
            key("[", CODE_LEFT_BRACKET, fns = fns),
            key("]", CODE_RIGHT_BRACKET, fns = fns),
            key("\\", CODE_BACKSLASH, W1_5),
        )

private fun c15(fns: Fns = emptyMap()): List<Cap> =
    listOf(Cap("Caps", W1_75, KeyOut.Code(CODE_CAPS), oneShot = true)) +
        letters("ASDFGHJKL", fns) +
        listOf(key(";", CODE_SEMICOLON, fns = fns), key("'", CODE_QUOTE, fns = fns), key("Enter", CODE_ENTER, W2_25))

private fun zRow(fns: Fns): List<Cap> =
    letters("ZXCVBNM", fns) +
        listOf(key(",", CODE_COMMA, fns = fns), key(".", CODE_PERIOD, fns = fns), key("/", CODE_SLASH, fns = fns))

private fun shift(
    label: String,
    code: Int,
    units: Float,
) = Cap(label, units, KeyOut.Code(code), role = KeyRole.MODIFIER)

private fun s15(fns: Fns = emptyMap()): List<Cap> =
    listOf(shift("Shift", CODE_LSHIFT, W2_25)) + zRow(fns) + shift("Shift", CODE_RSHIFT, W2_75)

private fun s14(): List<Cap> =
    listOf(shift("Shift", CODE_LSHIFT, W2_25)) + zRow(emptyMap()) + shift("Shift", CODE_RSHIFT, W1_75)

private fun ctrl(
    code: Int,
    units: Float,
) = shift("Ctrl", code, units)

private fun alt(
    code: Int,
    units: Float,
) = shift("Alt", code, units)

private fun win(
    code: Int,
    units: Float,
) = Cap("Win", units, KeyOut.Code(code), oneShot = true)

private fun fnKey(units: Float) = Cap("Fn", units, KeyOut.None, role = KeyRole.FN)

private fun space() = key("Space", CODE_SPACE, W6_25)

private fun b15(): List<Cap> =
    listOf(
        ctrl(CODE_LCTRL, W1_25),
        alt(CODE_LALT, W1_25),
        win(CODE_LWIN, W1_25),
        space(),
        win(CODE_RWIN, W1_25),
        alt(CODE_RALT, W1_25),
        fnKey(W1_25),
        ctrl(CODE_RCTRL, W1_25),
    )

private fun b13(): List<Cap> =
    listOf(
        ctrl(CODE_LCTRL, W1_25),
        alt(CODE_LALT, W1_25),
        win(CODE_LWIN, W1_25),
        space(),
        win(CODE_RWIN, 1f),
        fnKey(1f),
        ctrl(CODE_RCTRL, 1f),
    )

/** B13 for the 65 percent board: the last key is a second Fn, not Ctrl. */
private fun b13c(): List<Cap> = b13().dropLast(1) + fnKey(1f)

private fun b15c(): List<Cap> =
    listOf(
        ctrl(CODE_LCTRL, W1_25),
        alt(CODE_LALT, W1_25),
        win(CODE_LWIN, W1_25),
        space(),
        win(CODE_RWIN, W1_25),
        fnKey(W1_25),
        fnKey(W1_25),
        ctrl(CODE_RCTRL, W1_25),
    )
