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
 * finding the laptop; from Settings it is the Guide and ends by going back. Steps shows one page at a
 * time with a drawing; Page shows them all on one scrolling page. The choice is remembered.
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

    private const val ITEM_SP = 14f
    private const val ITEM_LEADING = 1.4f
    private const val ITEM_GAP_DP = 2f
    private const val ART_DP = 132f
    private const val ART_WIDE_DP = 150f
    private const val STEP_TITLE_SP = 28f
    private const val PAGE_TITLE_SP = 22f
    private const val TITLE_LEADING = 1.2f

    /** Beside a step's kicker and title, so they sit just in from the drawing's rounded panel above them. */
    private const val TEXT_INSET_DP = 4f

    // The page dots: the current page a wide accent pill, the rest small round dots.
    private const val DOT_DP = 8f
    private const val DOT_CURRENT_DP = 22f
    private const val DOT_GAP_DP = 4f
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
                    ui.bar(
                        ui.string(R.string.guide_title),
                        onBack,
                        modes,
                        backLabel = ui.string(R.string.settings_title),
                    )
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
                add(kicker(ui, page, TEXT_INSET_DP), Space.L)
                add(title(ui, page, STEP_TITLE_SP, TEXT_INSET_DP), Space.XS)
            },
            {
                card(if (ui.landscape) Space.S else Space.L) { items(ui, page) }
            },
        )
    }

    /** A page's number and subject, a small bold line over its title, [insetDp] in from either side. */
    private fun kicker(
        ui: Ui,
        page: Page,
        insetDp: Float,
    ): View =
        ui.text(ui.string(page.kicker), Type.SMALL, ui.palette.dim, face = Type.bold).apply {
            setPadding(ui.dp(insetDp), 0, ui.dp(insetDp), 0)
        }

    private fun title(
        ui: Ui,
        page: Page,
        sp: Float,
        insetDp: Float,
    ): View =
        ui.text(ui.string(page.title), sp, ui.palette.ink, Type.TRACKING_TIGHT, Type.black).apply {
            isAccessibilityHeading = true
            setLineSpacing(0f, TITLE_LEADING)
            setPadding(ui.dp(insetDp), 0, ui.dp(insetDp), 0)
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

    /** One page in the scrolling view: its kicker and title as a header over its items' card. */
    private fun Column.brief(
        ui: Ui,
        page: Page,
    ) {
        // Lined up with the card's text, like a section header over a card.
        add(kicker(ui, page, Space.CARD_PAD), Space.XL)
        add(title(ui, page, PAGE_TITLE_SP, Space.CARD_PAD), Space.XS)
        card(Space.M) { items(ui, page) }
    }

    /** A page's items, meant for a [Column.card]: a bold name over its explanation, a hairline between each. */
    private fun Column.items(
        ui: Ui,
        page: Page,
    ) {
        val keys = ui.context.resources.getStringArray(page.keys)
        val values = ui.context.resources.getStringArray(page.values)
        keys.zip(values).forEachIndexed { i, (key, value) ->
            if (i > 0) hairline()
            add(
                LinearLayout(ui.context).apply {
                    orientation = LinearLayout.VERTICAL
                    setPadding(0, ui.dp(Space.M), 0, ui.dp(Space.M))
                    addView(ui.text(key, Type.LEAD, ui.palette.ink, face = Type.bold))
                    addView(
                        ui.text(value, ITEM_SP, ui.palette.dim).apply {
                            setLineSpacing(0f, ITEM_LEADING)
                            setPadding(0, ui.dp(ITEM_GAP_DP), 0, 0)
                        },
                    )
                },
            )
        }
    }

    /**
     * One dot per page, centred: the current page a wide accent pill, the others small grey dots. Each is a
     * tap target that jumps to its page.
     */
    private fun progress(
        ui: Ui,
        step: Int,
        onJump: (Int) -> Unit,
    ): View =
        LinearLayout(ui.context).apply {
            gravity = Gravity.CENTER_HORIZONTAL
            pages.indices.forEach { i ->
                val current = i == step
                val target =
                    FrameLayout(ui.context).apply {
                        contentDescription = ui.string(R.string.guide_step_description, i + 1, pages.size)
                        if (current) stateDescription = ui.string(R.string.chosen)
                        setPadding(ui.dp(DOT_GAP_DP), 0, ui.dp(DOT_GAP_DP), 0)
                        val dot =
                            View(ui.context).apply {
                                background =
                                    ui.rounded(if (current) ui.palette.accent else ui.palette.off, DOT_DP / 2)
                            }
                        addView(
                            dot,
                            FrameLayout.LayoutParams(
                                ui.dp(if (current) DOT_CURRENT_DP else DOT_DP),
                                ui.dp(DOT_DP),
                                Gravity.CENTER_VERTICAL,
                            ),
                        )
                        ui.tappable(this, DOT_DP / 2) { if (!current) onJump(i) }
                    }
                addView(target, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ui.dp(Space.XL)))
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
