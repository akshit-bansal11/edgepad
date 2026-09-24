package me.akshitbansal.edgepad.screens

import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.view.animation.PathInterpolator
import android.widget.FrameLayout
import android.widget.LinearLayout
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type

/**
 * What Edgepad is and how to use it, in five pages. On the first run it opens the app and ends by
 * finding the laptop; from Settings it is the Guide and ends by going back. STEPS shows one page at a
 * time with a drawing; PAGE shows them all on one scrolling page. The choice is remembered.
 */
object GuideScreen {
    private class Page(
        val kicker: Int,
        val title: Int,
        val keys: Int,
        val values: Int,
        val art: GuideArt.Kind,
    )

    private val pages =
        listOf(
            Page(
                R.string.guide_features_kicker,
                R.string.guide_features_title,
                R.array.guide_features_keys,
                R.array.guide_features_values,
                GuideArt.Kind.FEATURES,
            ),
            Page(
                R.string.guide_anatomy_kicker,
                R.string.guide_anatomy_title,
                R.array.guide_anatomy_keys,
                R.array.guide_anatomy_values,
                GuideArt.Kind.ANATOMY,
            ),
            Page(
                R.string.guide_surface_kicker,
                R.string.guide_surface_title,
                R.array.guide_surface_keys,
                R.array.guide_surface_values,
                GuideArt.Kind.SURFACE,
            ),
            Page(
                R.string.guide_keyboard_kicker,
                R.string.guide_keyboard_title,
                R.array.guide_keyboard_keys,
                R.array.guide_keyboard_values,
                GuideArt.Kind.KEYBOARD,
            ),
            Page(
                R.string.guide_gamepad_kicker,
                R.string.guide_gamepad_title,
                R.array.guide_gamepad_keys,
                R.array.guide_gamepad_values,
                GuideArt.Kind.GAMEPAD,
            ),
        )

    private const val KICKER_TRACKING = 0.16f
    private const val KEY_TRACKING = 0.14f
    private const val KEY_DP = 96f
    private const val ITEM_LEADING = 1.45f
    private const val ART_DP = 132f
    private const val ART_WIDE_DP = 150f
    private const val PAGE_TITLE_SP = 20f
    private const val BAR_HEIGHT_DP = 3f
    private const val BAR_GAP_DP = 6f
    private const val ENTER_DP = 18f
    private const val ENTER_MS = 450L
    private const val EASE_X1 = 0.2f
    private const val EASE_Y1 = 0.7f
    private const val EASE_X2 = 0.2f
    private const val EASE_Y2 = 1f

    /**
     * [intro] is the first run: the mark instead of back, and the last step finds the laptop through
     * [onFinish]. Otherwise the last step and back both leave through [onBack].
     */
    fun build(
        ui: Ui,
        settings: Settings,
        intro: Boolean,
        onFinish: () -> Unit,
        onBack: () -> Unit,
    ): View {
        val root = FrameLayout(ui.context)
        var step = 0

        fun show(direction: Int) {
            root.removeAllViews()
            val modes =
                ui.segmented(
                    listOf(ui.string(R.string.guide_steps), ui.string(R.string.guide_page)),
                    if (settings.guideScroll) 1 else 0,
                ) { i ->
                    settings.guideScroll = i == 1
                    show(0)
                }
            val bar =
                if (intro) {
                    ui.bar(ui.string(R.string.app_name), null, modes, lead = MarkView(ui.context))
                } else {
                    ui.bar(ui.string(R.string.guide_title), onBack, modes)
                }
            val page =
                if (settings.guideScroll) {
                    ui.page(bar) { everything(ui, intro, onFinish) }
                } else {
                    val last = step == pages.lastIndex
                    ui.page(bar) {
                        one(ui, pages[step])
                        grow()
                        add(
                            progress(ui, step) { i ->
                                val from = step
                                step = i
                                show(i.compareTo(from))
                            },
                            Space.L,
                        )
                        val back =
                            ui.button(ui.string(R.string.guide_back), Ui.Style.QUIET) {
                                step--
                                show(-1)
                            }
                        back.visibility = if (step == 0) View.INVISIBLE else View.VISIBLE
                        val nextLabel =
                            when {
                                !last -> R.string.guide_next
                                intro -> R.string.guide_find_laptop
                                else -> R.string.guide_done
                            }
                        val next =
                            ui.button(ui.string(nextLabel), Ui.Style.FILLED) {
                                when {
                                    !last -> {
                                        step++
                                        show(1)
                                    }

                                    intro -> {
                                        onFinish()
                                    }

                                    else -> {
                                        onBack()
                                    }
                                }
                            }
                        add(
                            LinearLayout(ui.context).apply {
                                addView(
                                    back,
                                    LinearLayout.LayoutParams(
                                        ViewGroup.LayoutParams.WRAP_CONTENT,
                                        ViewGroup.LayoutParams.WRAP_CONTENT,
                                    ),
                                )
                                addView(
                                    next,
                                    LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply {
                                        marginStart =
                                            ui.dp(Space.S)
                                    },
                                )
                            },
                            Space.M,
                        )
                    }
                }
            root.addView(page)
            if (direction != 0) enter(ui, page.getChildAt(1), direction)
        }
        show(0)
        return root
    }

    /** One page with its drawing: the drawing, kicker and title, then its items beside or below them. */
    private fun Column.one(
        ui: Ui,
        page: Page,
    ) {
        columns(
            {
                add(GuideArt(ui.context, page.art), Space.S, ui.dp(if (ui.landscape) ART_WIDE_DP else ART_DP))
                add(ui.mono(ui.string(page.kicker), Type.MICRO, ui.palette.dim, KICKER_TRACKING), Space.M)
                headline(ui.string(page.title), Type.TITLE, Space.S).isAccessibilityHeading = true
            },
            {
                if (!ui.landscape) add(View(ui.context), height = ui.dp(Space.L))
                items(ui, page)
            },
        )
    }

    /** Every page, one after another, split into two columns sideways. */
    private fun Column.everything(
        ui: Ui,
        intro: Boolean,
        onFinish: () -> Unit,
    ) {
        val split = (pages.size + 1) / 2
        columns(
            { pages.take(split).forEach { brief(ui, it) } },
            { pages.drop(split).forEach { brief(ui, it) } },
        )
        if (intro) add(ui.button(ui.string(R.string.guide_find_laptop), Ui.Style.FILLED, onFinish), Space.S)
    }

    private fun Column.brief(
        ui: Ui,
        page: Page,
    ) {
        add(ui.mono(ui.string(page.kicker), Type.MICRO, ui.palette.dim, KICKER_TRACKING), Space.L)
        add(
            ui.text(ui.string(page.title), PAGE_TITLE_SP, ui.palette.ink, Type.TRACKING_TIGHT),
            Space.S,
        ).isAccessibilityHeading =
            true
        items(ui, page)
        add(View(ui.context), height = ui.dp(Space.L))
    }

    /** A page's items: a spaced key beside its explanation, a hairline above each. */
    private fun Column.items(
        ui: Ui,
        page: Page,
    ) {
        val keys = ui.context.resources.getStringArray(page.keys)
        val values = ui.context.resources.getStringArray(page.values)
        keys.zip(values).forEach { (key, value) ->
            hairline()
            add(
                LinearLayout(ui.context).apply {
                    setPadding(0, ui.dp(Space.M), 0, ui.dp(Space.M))
                    addView(
                        ui.mono(key, Type.MICRO, ui.palette.ink, KEY_TRACKING),
                        LinearLayout.LayoutParams(ui.dp(KEY_DP), ViewGroup.LayoutParams.WRAP_CONTENT).apply {
                            marginEnd =
                                ui.dp(Space.M)
                        },
                    )
                    addView(
                        ui.text(value, Type.CAPTION, ui.palette.dim).apply { setLineSpacing(0f, ITEM_LEADING) },
                        LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f),
                    )
                },
            )
        }
    }

    /** One short bar per page, lit up to the current one; each is a tap target that jumps to its page. */
    private fun progress(
        ui: Ui,
        step: Int,
        onJump: (Int) -> Unit,
    ): View =
        LinearLayout(ui.context).apply {
            pages.indices.forEach { i ->
                val target =
                    FrameLayout(ui.context).apply {
                        contentDescription = ui.string(R.string.guide_step_description, i + 1, pages.size)
                        if (i == step) stateDescription = ui.string(R.string.chosen)
                        val bar =
                            View(ui.context).apply {
                                setBackgroundColor(
                                    if (i <=
                                        step
                                    ) {
                                        ui.palette.ink
                                    } else {
                                        ui.palette.line
                                    },
                                )
                            }
                        addView(
                            bar,
                            FrameLayout.LayoutParams(
                                ViewGroup.LayoutParams.MATCH_PARENT,
                                ui.dp(BAR_HEIGHT_DP),
                                Gravity.CENTER_VERTICAL,
                            ),
                        )
                        ui.tappable(this) { if (i != step) onJump(i) }
                    }
                addView(
                    target,
                    LinearLayout.LayoutParams(0, ui.dp(Space.XL), 1f).apply {
                        if (i >
                            0
                        ) {
                            marginStart = ui.dp(BAR_GAP_DP)
                        }
                    },
                )
            }
        }

    /** The new page slides in from the side it came from and fades up; instant with animations off. */
    private fun enter(
        ui: Ui,
        content: View,
        direction: Int,
    ) {
        content.alpha = 0f
        content.translationX = direction * ui.dp(ENTER_DP).toFloat()
        content
            .animate()
            .alpha(1f)
            .translationX(0f)
            .setDuration(ENTER_MS)
            .setInterpolator(PathInterpolator(EASE_X1, EASE_Y1, EASE_X2, EASE_Y2))
            .start()
    }
}
