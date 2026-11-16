package me.akshitbansal.edgepad.screens

import android.content.Context
import android.graphics.Rect
import android.graphics.RectF
import android.os.Bundle
import android.view.View
import android.view.accessibility.AccessibilityNodeInfo
import android.view.accessibility.AccessibilityNodeProvider
import android.widget.Button
import me.akshitbansal.edgepad.R

/**
 * AND-A-8: a custom-drawn view of [count] controls, each one a virtual node a screen reader can find, read and press,
 * the way the keyboard's keys are. The view gives each control's [label], where it is ([bounds], in the view's own
 * pixels, filled into the rect handed over so nothing is allocated per node beyond the node itself) and what a double
 * tap does ([click], for the controls where [clickable] says a tap means something). Built for the accessibility
 * service's questions only; the view's draw and touch paths never reach it. [actions] are a node's custom actions
 * (ANDU-6: a layout editor's one-cell moves), each tried before a click is.
 */
internal class ControlNodes(
    private val host: View,
    private val count: Int,
    private val label: (Int) -> CharSequence,
    private val bounds: (Int, RectF) -> Unit,
    private val clickable: (Int) -> Boolean,
    private val click: (Int) -> Unit,
    private val state: (Int) -> CharSequence? = { null },
    private val actions: (Int) -> List<NodeAction> = { emptyList() },
) : AccessibilityNodeProvider() {
    private val origin = IntArray(2)
    private val local = RectF()

    override fun createAccessibilityNodeInfo(virtualViewId: Int): AccessibilityNodeInfo? {
        if (virtualViewId == HOST_VIEW_ID) {
            val info = AccessibilityNodeInfo(host)
            host.onInitializeAccessibilityNodeInfo(info)
            for (i in 0 until count) info.addChild(host, i)
            return info
        }
        if (virtualViewId !in 0 until count) return null
        val node = AccessibilityNodeInfo(host, virtualViewId)
        node.setParent(host)
        node.contentDescription = label(virtualViewId)
        node.stateDescription = state(virtualViewId)
        if (clickable(virtualViewId)) {
            node.className = Button::class.java.name
            node.isClickable = true
            node.addAction(AccessibilityNodeInfo.AccessibilityAction.ACTION_CLICK)
        } else {
            node.className = View::class.java.name
        }
        for (action in actions(virtualViewId)) {
            node.addAction(AccessibilityNodeInfo.AccessibilityAction(action.id, action.label))
        }
        host.getLocationOnScreen(origin)
        bounds(virtualViewId, local)
        node.setBoundsInScreen(
            Rect(
                origin[0] + local.left.toInt(),
                origin[1] + local.top.toInt(),
                origin[0] + local.right.toInt(),
                origin[1] + local.bottom.toInt(),
            ),
        )
        return node
    }

    override fun performAction(
        virtualViewId: Int,
        action: Int,
        arguments: Bundle?,
    ): Boolean {
        if (virtualViewId == HOST_VIEW_ID) return host.performAccessibilityAction(action, arguments)
        if (virtualViewId !in 0 until count) return false
        actions(virtualViewId).firstOrNull { it.id == action }?.let { return it.run() }
        if (action != AccessibilityNodeInfo.ACTION_CLICK || !clickable(virtualViewId)) return false
        click(virtualViewId)
        return true
    }
}

/** A node's custom accessibility action: a stable [id] from `ids.xml`, the [label] spoken, [run] true if it took. */
internal class NodeAction(
    val id: Int,
    val label: CharSequence,
    val run: () -> Boolean,
)

/** ANDU-6: the four one-cell moves a layout editor gives each piece; [move] takes the x and y step (-1, 0, 1). */
internal fun moveActions(
    context: Context,
    move: (Int, Int) -> Boolean,
): List<NodeAction> =
    listOf(
        NodeAction(R.id.action_move_left, context.getString(R.string.node_move_left)) { move(-1, 0) },
        NodeAction(R.id.action_move_right, context.getString(R.string.node_move_right)) { move(1, 0) },
        NodeAction(R.id.action_move_up, context.getString(R.string.node_move_up)) { move(0, -1) },
        NodeAction(R.id.action_move_down, context.getString(R.string.node_move_down)) { move(0, 1) },
    )
