package me.akshitbansal.edgepad.screens

import android.graphics.Rect
import android.graphics.RectF
import android.os.Bundle
import android.view.View
import android.view.accessibility.AccessibilityNodeInfo
import android.view.accessibility.AccessibilityNodeProvider
import android.widget.Button

/**
 * AND-A-8: a custom-drawn view of [count] controls, each one a virtual node a screen reader can find, read and press,
 * the way the keyboard's keys are. The view gives each control's [label], where it is ([bounds], in the view's own
 * pixels, filled into the rect handed over so nothing is allocated per node beyond the node itself) and what a double
 * tap does ([click], for the controls where [clickable] says a tap means something). Built for the accessibility
 * service's questions only; the view's draw and touch paths never reach it.
 */
internal class ControlNodes(
    private val host: View,
    private val count: Int,
    private val label: (Int) -> CharSequence,
    private val bounds: (Int, RectF) -> Unit,
    private val clickable: (Int) -> Boolean,
    private val click: (Int) -> Unit,
    private val state: (Int) -> CharSequence? = { null },
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
        if (virtualViewId !in 0 until count || action != AccessibilityNodeInfo.ACTION_CLICK) return false
        if (!clickable(virtualViewId)) return false
        click(virtualViewId)
        return true
    }
}
