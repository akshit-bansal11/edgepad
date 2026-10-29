package me.akshitbansal.edgepad.screens

import android.content.Context
import android.view.Gravity
import android.view.MotionEvent
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.HorizontalScrollView
import android.widget.LinearLayout
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import kotlin.math.ceil
import kotlin.math.floor
import kotlin.math.roundToInt

/**
 * Pages side by side, each as wide as the pager, that a finger slides between and that always come to rest
 * on one: a fling goes on to the next page in its direction, and a slow release goes to the nearer one. Add
 * the pages to [row]. It is a scroll view, so a screen reader pages through it with its own scroll actions.
 * [onPage] is told the page that is showing, as it changes.
 */
class Pager(
    context: Context,
    private val onPage: (Int) -> Unit,
) : HorizontalScrollView(context) {
    constructor(context: Context) : this(context, {})

    val row = LinearLayout(context)
    private var page = 0
    private var flung = false

    init {
        isHorizontalScrollBarEnabled = false
        overScrollMode = OVER_SCROLL_NEVER
        addView(row, FrameLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.MATCH_PARENT))
    }

    /** Shows [index] without a slide, once the pager has been laid out. */
    fun jumpTo(index: Int) {
        post { scrollTo(index * width, 0) }
    }

    override fun onScrollChanged(
        l: Int,
        t: Int,
        oldl: Int,
        oldt: Int,
    ) {
        super.onScrollChanged(l, t, oldl, oldt)
        val now = if (width > 0) (l + width / 2) / width else 0
        if (now != page) {
            page = now
            onPage(now)
        }
    }

    override fun fling(velocityX: Int) {
        flung = true
        val at = scrollX.toFloat() / width
        settle(if (velocityX > 0) ceil(at).toInt() else floor(at).toInt())
    }

    // The scroll view handles the release inside this call, and starts a fling from it when the finger was quick.
    override fun dispatchTouchEvent(event: MotionEvent): Boolean {
        val handled = super.dispatchTouchEvent(event)
        if (event.actionMasked == MotionEvent.ACTION_UP || event.actionMasked == MotionEvent.ACTION_CANCEL) {
            if (!flung && width > 0) settle((scrollX.toFloat() / width).roundToInt())
            flung = false
        }
        return handled
    }

    private fun settle(index: Int) {
        smoothScrollTo(index.coerceIn(0, row.childCount - 1) * width, 0)
    }
}

/**
 * One dot per page of a [Pager], the showing page's in the accent. Its description says which page that is,
 * and is a live region, so a screen reader reads it out when a slide lands on another page.
 */
class PageDots(
    private val ui: Ui,
    private val count: Int,
) : LinearLayout(ui.context) {
    constructor(context: Context) : this(Ui(context), 1)

    private val dots = List(count) { View(ui.context) }

    init {
        gravity = Gravity.CENTER
        accessibilityLiveRegion = ACCESSIBILITY_LIVE_REGION_POLITE
        dots.forEachIndexed { at, dot ->
            addView(
                dot,
                LinearLayout.LayoutParams(ui.dp(Space.S), ui.dp(Space.S)).apply {
                    if (at > 0) marginStart = ui.dp(Space.S)
                },
            )
        }
        select(0)
    }

    fun select(page: Int) {
        dots.forEachIndexed { at, dot ->
            dot.background = ui.rounded(if (at == page) ui.palette.accent else ui.palette.off, Space.S / 2)
        }
        contentDescription = ui.string(R.string.macros_page_of, page + 1, count)
    }
}
