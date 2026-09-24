package me.akshitbansal.edgepad.screens

import android.app.AlertDialog
import android.content.Context
import android.graphics.Canvas
import android.graphics.RectF
import android.text.InputFilter
import android.view.MotionEvent
import android.view.View
import android.view.ViewGroup
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ScrollView
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.gamepad.Binding
import me.akshitbansal.edgepad.gamepad.Control
import me.akshitbansal.edgepad.gamepad.ControlGeometry
import me.akshitbansal.edgepad.gamepad.ControlKind
import me.akshitbansal.edgepad.gamepad.GamepadLayout
import me.akshitbansal.edgepad.gamepad.GamepadStore
import me.akshitbansal.edgepad.gamepad.InputNames
import me.akshitbansal.edgepad.protocol.PadButton
import kotlin.math.abs
import kotlin.math.roundToInt

/**
 * Builds the pad the player uses, on a full-size canvas with a snapping grid like [MediaLayoutScreen]'s
 * editor: a control near the middle snaps to it, and the centre lines light up to say so.
 *
 * Drag moves a control and a long press opens its menu — what it drives, how big it is, what is written
 * on it, and whether it stays at all. The options popup switches between layouts, adds a control, and
 * keeps the named layouts themselves: save as, rename, delete. Everything but a name is picked from a
 * list, because a list is the only kind of input this app draws; see [Editor.askName] for the one
 * exception and why it is one.
 */
object GamepadLayoutScreen {
    private val SIZES_DP = listOf(44, 56, 64, 80, 110, 150, 200)

    /**
     * How tall the list of layouts may grow inside the popup. The editor is held sideways, so there is
     * only ever a phone's short side of room under the options button, and a list that grows a row per
     * saved game would push RESET and DONE off the bottom of it. Past this it scrolls.
     */
    private const val MAX_LIST_DP = 120f

    /** What ADD CONTROL offers: a kind, what a fresh one of it drives, and what is written on it. */
    private class Addable(
        val nameRes: Int,
        val kind: ControlKind,
        val binding: Binding,
        val label: String,
    )

    /**
     * A trigger is a shoulder bound to a trigger rather than a kind of its own, which is why the last two
     * share [ControlKind.SHOULDER]. Every one of these is rebindable the moment it lands; what it starts
     * on is only what saves the user a second menu in the common case.
     */
    private val ADDABLE =
        listOf(
            Addable(R.string.gamepad_kind_button, ControlKind.BUTTON, Binding.Button(PadButton.A), "A"),
            Addable(R.string.gamepad_kind_dpad, ControlKind.DPAD, Binding.Dpad, "D"),
            Addable(R.string.gamepad_kind_stick, ControlKind.STICK, Binding.Stick(Binding.Side.LEFT), "L"),
            Addable(
                R.string.gamepad_kind_shoulder,
                ControlKind.SHOULDER,
                Binding.Button(PadButton.LEFT_SHOULDER),
                "LB",
            ),
            Addable(
                R.string.gamepad_kind_trigger,
                ControlKind.SHOULDER,
                Binding.Trigger(Binding.Side.LEFT),
                "LT",
            ),
        )

    /** The four directions of a d-pad or a stick, in the order [Binding.Keys] stores their codes. */
    private val DIRECTIONS =
        listOf(R.string.gamepad_up, R.string.gamepad_down, R.string.gamepad_left, R.string.gamepad_right)

    fun build(
        ui: Ui,
        store: GamepadStore,
        onBack: () -> Unit,
    ): View {
        val canvas = Editor(ui.context, store)
        return EditorFrame.build(ui, ui.string(R.string.gamepad_layout_title).uppercase(), canvas, onBack) { close ->
            LinearLayout(ui.context).apply {
                orientation = LinearLayout.VERTICAL
                addView(
                    layouts(ui, store.all.map { it.name }, store.current.name) { name ->
                        store.choose(name)
                        canvas.reload()
                        close()
                    },
                )
                addView(
                    ui
                        .mono(
                            ui.string(R.string.gamepad_layout_hint),
                            Type.MICRO,
                            ui.palette.dim,
                            Type.TRACKING_ROW,
                        ).apply {
                            setPadding(0, ui.dp(Space.S), 0, ui.dp(Space.S))
                        },
                )
                addView(
                    pair(
                        ui,
                        ui.button(ui.string(R.string.gamepad_add), Ui.Style.QUIET) {
                            close()
                            canvas.addControl()
                        },
                        ui.button(ui.string(R.string.gamepad_layouts), Ui.Style.QUIET) {
                            close()
                            canvas.manageLayouts()
                        },
                    ),
                )
                addView(
                    EditorFrame.resetAndDone(ui, {
                        canvas.reset()
                        close()
                    }, close),
                )
            }
        }
    }

    /**
     * Every layout as a list of choices, the one called [current] marked, capped at [MAX_LIST_DP] and
     * scrolling past it. Shared with [GamepadScreen], whose options popup offers the same switch.
     */
    fun layouts(
        ui: Ui,
        names: List<String>,
        current: String,
        onPick: (String) -> Unit,
    ): View =
        LinearLayout(ui.context).apply {
            orientation = LinearLayout.VERTICAL
            val section = ui.string(R.string.gamepad_layout_section)
            addView(ui.mono(section, Type.MICRO, ui.palette.dim, Type.TRACKING_ROW))
            // The scroll bar is left on, unlike everywhere else in the app: this box is two and a half
            // rows tall inside a popup, and it is the only thing that says the list goes on past it.
            val list =
                ScrollView(ui.context).apply {
                    addView(ui.choices(names, names.indexOf(current)) { i -> onPick(names[i]) })
                }
            val wanted = names.size * (Space.TOUCH + Space.HAIR)
            addView(
                list,
                LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ui.dp(minOf(wanted, MAX_LIST_DP))),
            )
        }

    /** Two buttons side by side, the way [EditorFrame.resetAndDone] lays its own out. */
    private fun pair(
        ui: Ui,
        first: View,
        second: View,
    ): View =
        LinearLayout(ui.context).apply {
            addView(first, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
            addView(
                second,
                LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply {
                    marginStart = ui.dp(Space.S)
                },
            )
        }

    /** What a binding is called in a menu: a legend for the two vocabularies, prose for the rest. */
    private fun bindingName(
        context: Context,
        binding: Binding,
    ): String =
        when (binding) {
            is Binding.Keys -> {
                binding.codes.joinToString("/") { InputNames.legendOf(it) }
            }

            is Binding.Button -> {
                InputNames.legendOf(binding.button)
            }

            Binding.Dpad -> {
                context.getString(R.string.gamepad_kind_dpad)
            }

            is Binding.Stick -> {
                if (binding.side == Binding.Side.RIGHT) {
                    context.getString(R.string.gamepad_bind_stick_right)
                } else {
                    context.getString(R.string.gamepad_bind_stick_left)
                }
            }

            is Binding.Trigger -> {
                if (binding.side == Binding.Side.RIGHT) {
                    context.getString(R.string.gamepad_bind_trigger_right)
                } else {
                    context.getString(R.string.gamepad_bind_trigger_left)
                }
            }
        }

    /** The surface's own proportions: a control is dragged by its centre and stored as fractions of the size. */
    private class Editor(
        context: Context,
        private val store: GamepadStore,
    ) : LayoutCanvas(context) {
        private var layout = store.current
        private var dragging: Control? = null
        private var downX = 0f
        private var downY = 0f

        init {
            contentDescription = context.getString(R.string.gamepad_layout_title)
        }

        fun reload() {
            layout = store.current
            invalidate()
        }

        fun reset() {
            store.reset()
            reload()
        }

        private fun bounds(
            control: Control,
            out: RectF,
        ) {
            val cx = control.x * width
            val cy = control.y * height
            val w = control.size * density
            val h = ControlGeometry.halfHeight(control.kind, w / 2) * 2
            out.set(cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2)
        }

        override fun onDraw(canvas: Canvas) {
            super.onDraw(canvas)
            for (control in layout.controls) {
                bounds(control, box)
                // The control being dragged lifts off the grid on a shadow.
                if (control == dragging) {
                    if (control.kind == ControlKind.BUTTON || control.kind == ControlKind.STICK) {
                        canvas.drawOval(box, lift)
                    } else {
                        canvas.drawRoundRect(box, CORNER_DP * density, CORNER_DP * density, lift)
                    }
                }
                stroke.color = if (control == dragging) palette.ink else palette.dim
                drawShape(canvas, control.kind, box)
                text.color = stroke.color
                canvas.drawText(
                    control.label,
                    box.centerX(),
                    box.centerY() + text.textSize * Type.CAP_CENTRE,
                    text,
                )
            }
        }

        private fun drawShape(
            canvas: Canvas,
            kind: ControlKind,
            box: RectF,
        ) {
            when (kind) {
                ControlKind.BUTTON, ControlKind.STICK -> {
                    canvas.drawOval(box, stroke)
                }

                ControlKind.SHOULDER -> {
                    val corner = ControlGeometry.CORNER_DP * density
                    canvas.drawRoundRect(box, corner, corner, stroke)
                }

                ControlKind.DPAD -> {
                    val third = box.width() / ControlGeometry.DPAD_CELLS
                    for (row in 0 until ControlGeometry.DPAD_CELLS.toInt()) {
                        for (col in 0 until ControlGeometry.DPAD_CELLS.toInt()) {
                            if (!ControlGeometry.isArmCell(row, col)) continue
                            canvas.drawRect(
                                box.left + col * third,
                                box.top + row * third,
                                box.left + (col + 1) * third,
                                box.top + (row + 1) * third,
                                stroke,
                            )
                        }
                    }
                }
            }
        }

        // Lint's ClickableViewAccessibility wants this on the same class that overrides onTouchEvent;
        // inheriting it from LayoutCanvas does not satisfy the rule, so both editors carry their own.
        override fun performClick(): Boolean {
            super.performClick()
            return true
        }

        override fun onTouchEvent(event: MotionEvent): Boolean {
            when (event.actionMasked) {
                MotionEvent.ACTION_DOWN -> {
                    downX = event.x
                    downY = event.y
                    dragging =
                        layout.controls.lastOrNull {
                            bounds(it, box)
                            box.contains(event.x, event.y)
                        } ?: return false
                }

                MotionEvent.ACTION_MOVE -> {
                    val control = dragging ?: return false
                    val moved = control.copy(x = snap(event.x, width), y = snap(event.y, height))
                    replace(control, moved)
                    dragging = moved
                    setOnCentre(moved.x, moved.y)
                }

                MotionEvent.ACTION_UP -> {
                    val control = dragging
                    dragging = null
                    clearOnCentre()
                    if (control != null) {
                        val moved =
                            abs(event.x - downX) > LONG_PRESS_SLOP_DP * density ||
                                abs(event.y - downY) > LONG_PRESS_SLOP_DP * density
                        if (!moved && event.eventTime - event.downTime >= LONG_PRESS_MS) {
                            showControlMenu(currentControl(control.id) ?: control)
                        } else {
                            store.save(layout)
                        }
                    }
                    performClick()
                }

                MotionEvent.ACTION_CANCEL -> {
                    dragging = null
                    clearOnCentre()
                }

                else -> {
                    return false
                }
            }
            invalidate()
            return true
        }

        private fun currentControl(id: String): Control? = layout.controls.firstOrNull { it.id == id }

        private fun replace(
            old: Control,
            new: Control,
        ) {
            layout = layout.copy(controls = layout.controls.map { if (it.id == old.id) new else it })
        }

        /** Replaces [old] with [new], stores the layout it belongs to, and redraws. */
        private fun commit(
            old: Control,
            new: Control,
        ) {
            replace(old, new)
            store.save(layout)
            invalidate()
        }

        /**
         * Everything one control can have done to it. DELETE is missing while the layout is down to its
         * last control: a layout with nothing on it cannot be stored — [GamepadLayout.decode] refuses one
         * — and a menu entry that would fail is worse than one that is not there.
         */
        private fun showControlMenu(control: Control) {
            val actions = mutableListOf(R.string.gamepad_bind, R.string.gamepad_size, R.string.gamepad_label)
            if (layout.controls.size > 1) actions += R.string.gamepad_delete_control
            AlertDialog
                .Builder(context)
                .setTitle(
                    context.getString(
                        R.string.gamepad_control_title,
                        control.label,
                        bindingName(context, control.binding),
                    ),
                ).setItems(actions.map { context.getString(it) }.toTypedArray()) { _, which ->
                    when (actions[which]) {
                        R.string.gamepad_bind -> showBindPicker(control)
                        R.string.gamepad_size -> showSizePicker(control)
                        R.string.gamepad_label -> showLabelPicker(control)
                        else -> commitLayout(layout.copy(controls = layout.controls - control))
                    }
                }.show()
        }

        private fun showSizePicker(control: Control) {
            val current = SIZES_DP.indexOf(control.size.roundToInt()).coerceAtLeast(0)
            val labels = SIZES_DP.map { context.getString(R.string.dp_value, it) }.toTypedArray()
            AlertDialog
                .Builder(context)
                .setTitle(R.string.gamepad_size)
                .setSingleChoiceItems(labels, current) { dialog, which ->
                    dialog.dismiss()
                    commit(control, control.copy(size = SIZES_DP[which].toFloat()))
                }.show()
        }

        /**
         * What this control drives: every controller input its kind can take, then the keyboard, which is
         * last because it is the one entry that opens another picker rather than settling the matter. Both
         * are always offered — a laptop with no controller driver still has a keyboard, and a layout built
         * out of keys is the only kind that works there.
         */
        private fun showBindPicker(control: Control) {
            val options = Binding.controllerOptions(control.kind)
            val labels = options.map { bindingName(context, it) } + context.getString(R.string.gamepad_bind_keys)
            AlertDialog
                .Builder(context)
                .setTitle(R.string.gamepad_bind)
                .setSingleChoiceItems(labels.toTypedArray(), options.indexOf(control.binding)) { dialog, which ->
                    dialog.dismiss()
                    if (which == options.size) {
                        pickKey(control, IntArray(keysNeeded(control.kind)), 0)
                    } else {
                        commit(control, control.copy(binding = options[which]))
                    }
                }.show()
        }

        /**
         * One key at a time, because a d-pad and a stick each need four of them and a single-choice list
         * is the only picker this app has. Each dialog's title names the direction being chosen, so the
         * fourth one is not a mystery, and the binding is only written once all four are in — a half-bound
         * d-pad would be a control that moves in three directions.
         */
        private fun pickKey(
            control: Control,
            codes: IntArray,
            at: Int,
        ) {
            val title =
                if (codes.size == 1) {
                    context.getString(R.string.gamepad_bind_keys)
                } else {
                    context.getString(R.string.gamepad_bind_key_for, context.getString(DIRECTIONS[at]))
                }
            val was = (control.binding as? Binding.Keys)?.codes?.getOrNull(at)
            AlertDialog
                .Builder(context)
                .setTitle(title)
                .setSingleChoiceItems(
                    InputNames.keys.map { it.legend }.toTypedArray(),
                    InputNames.keys.indexOfFirst { it.code == was },
                ) { dialog, which ->
                    dialog.dismiss()
                    codes[at] = InputNames.keys[which].code
                    if (at + 1 < codes.size) {
                        pickKey(control, codes, at + 1)
                    } else {
                        commit(control, control.copy(binding = Binding.Keys(codes.toList())))
                    }
                }.show()
        }

        private fun keysNeeded(kind: ControlKind): Int =
            if (kind == ControlKind.DPAD || kind == ControlKind.STICK) DIRECTIONS.size else 1

        private fun showLabelPicker(control: Control) {
            askName(R.string.gamepad_label, control.label, GamepadLayout.MAX_LABEL) { label ->
                commit(control, control.copy(label = label))
            }
        }

        /** A fresh control of the chosen kind, in the middle of the canvas where it cannot be missed. */
        fun addControl() {
            AlertDialog
                .Builder(context)
                .setTitle(R.string.gamepad_add_control)
                .setItems(ADDABLE.map { context.getString(it.nameRes) }.toTypedArray()) { _, which ->
                    val addable = ADDABLE[which]
                    val control =
                        Control(
                            freeId(),
                            addable.kind,
                            addable.label,
                            HALF,
                            HALF,
                            Control.sizeOf(addable.kind),
                            addable.binding,
                        )
                    commitLayout(layout.copy(controls = layout.controls + control))
                }.show()
        }

        /** An id nothing in this layout already answers to; ids are the store's, never the player's. */
        private fun freeId(): String {
            val taken = layout.controls.map { it.id }.toSet()
            var next = 1
            while ("c$next" in taken) next++
            return "c$next"
        }

        /**
         * Saving a layout under a name, renaming it and deleting it. The last two are offered only for a
         * layout of the user's own: a layout Edgepad ships lives in the code, so it cannot be renamed or
         * removed, and SAVE AS is exactly the way to take a copy of one and make it yours.
         */
        fun manageLayouts() {
            val name = layout.name
            val actions = mutableListOf(R.string.gamepad_save_as)
            if (!store.isBuiltIn(name)) {
                actions += R.string.gamepad_rename
                actions += R.string.gamepad_delete_layout
            }
            AlertDialog
                .Builder(context)
                .setTitle(R.string.gamepad_layouts)
                .setItems(actions.map { context.getString(it) }.toTypedArray()) { _, which ->
                    when (actions[which]) {
                        R.string.gamepad_save_as -> {
                            askName(R.string.gamepad_save_as, name, GamepadLayout.MAX_NAME) {
                                store.saveAs(layout, it)
                                reload()
                            }
                        }

                        R.string.gamepad_rename -> {
                            askName(R.string.gamepad_rename, name, GamepadLayout.MAX_NAME) {
                                store.rename(name, it)
                                reload()
                            }
                        }

                        else -> {
                            confirmDelete(name)
                        }
                    }
                }.show()
        }

        private fun confirmDelete(name: String) {
            AlertDialog
                .Builder(context)
                .setTitle(R.string.gamepad_delete_layout)
                .setMessage(context.getString(R.string.gamepad_delete_layout_body, name))
                .setPositiveButton(R.string.gamepad_delete) { _, _ ->
                    store.delete(name)
                    reload()
                }.setNegativeButton(android.R.string.cancel, null)
                .show()
        }

        private fun commitLayout(updated: GamepadLayout) {
            layout = updated
            store.save(layout)
            invalidate()
        }

        /**
         * The one place this app asks for typed text.
         *
         * Everything else picks from a list, and this would too if it could — but a preset saved "game
         * wise" is a preset named after a game, and no list anyone can write holds the names of games.
         * It is the platform's own [EditText] inside the dialog the size and binding pickers already use,
         * rather than a new control on a page: nothing in [Ui] draws a text field, and one invented here
         * would be the only one in the app. The field opens on the name the layout already has, so the
         * user who does not want to type can take that and the number [GamepadLayout.freeName] adds.
         */
        private fun askName(
            titleRes: Int,
            initial: String,
            max: Int,
            onName: (String) -> Unit,
        ) {
            val field =
                EditText(context).apply {
                    setSingleLine()
                    filters = arrayOf<InputFilter>(InputFilter.LengthFilter(max))
                    setText(initial)
                    setSelection(length())
                    // A hint rather than a description: a screen reader reads a description instead of
                    // the field's own contents, which on a field the user is editing is the wrong half.
                    hint = context.getString(titleRes)
                    // A layout's name is neither a credential nor an address, so nothing an autofill
                    // service holds could ever fill it and offering it would only put a menu over a
                    // field ten characters wide.
                    importantForAutofill = View.IMPORTANT_FOR_AUTOFILL_NO
                    val pad = (Space.L * density).toInt()
                    setPadding(pad, pad, pad, pad)
                }
            AlertDialog
                .Builder(context)
                .setTitle(titleRes)
                .setView(field)
                .setPositiveButton(android.R.string.ok) { _, _ ->
                    // Nothing usable typed is nothing done: the name rules live in one place, and this is
                    // the trust boundary they guard — a name carrying a separator would corrupt the store.
                    GamepadLayout.clean(field.text.toString(), max)?.let(onName)
                }.setNegativeButton(android.R.string.cancel, null)
                .show()
        }

        private companion object {
            const val LONG_PRESS_MS = 500L
            const val LONG_PRESS_SLOP_DP = 12f
        }
    }
}
