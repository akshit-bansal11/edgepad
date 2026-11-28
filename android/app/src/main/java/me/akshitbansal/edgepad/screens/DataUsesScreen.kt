package me.akshitbansal.edgepad.screens

import android.animation.Animator
import android.animation.AnimatorListenerAdapter
import android.animation.ValueAnimator
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.view.accessibility.AccessibilityNodeInfo
import android.view.animation.DecelerateInterpolator
import android.widget.Button
import android.widget.LinearLayout
import me.akshitbansal.edgepad.Motion
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type

private const val CHEVRON_OPEN = 90f

/**
 * Every permission the manifest declares, with the strings that say what it is for. The one list the screen draws and
 * the test reads: a permission added to `AndroidManifest.xml` without a row here fails `DataUsesTest`, so the screen
 * cannot fall behind what the app asks for. [name] is the permission without its `android.permission.` prefix.
 */
object DataUses {
    const val PREFIX = "android.permission."

    class Permission(
        val name: String,
        val title: Int,
        val body: Int,
    )

    val PERMISSIONS =
        listOf(
            Permission("BLUETOOTH_CONNECT", R.string.uses_perm_bluetooth_title, R.string.uses_perm_bluetooth_body),
            Permission("ACCESS_LOCAL_NETWORK", R.string.uses_perm_localnet_title, R.string.uses_perm_localnet_body),
            Permission("ACCESS_NETWORK_STATE", R.string.uses_perm_netstate_title, R.string.uses_perm_netstate_body),
            Permission("ACCESS_WIFI_STATE", R.string.uses_perm_wifistate_title, R.string.uses_perm_wifistate_body),
            Permission(
                "CHANGE_WIFI_MULTICAST_STATE",
                R.string.uses_perm_multicast_title,
                R.string.uses_perm_multicast_body,
            ),
            Permission("INTERNET", R.string.uses_perm_internet_title, R.string.uses_perm_internet_body),
            Permission("REQUEST_INSTALL_PACKAGES", R.string.uses_perm_install_title, R.string.uses_perm_install_body),
            Permission(
                "UPDATE_PACKAGES_WITHOUT_USER_ACTION",
                R.string.uses_perm_update_title,
                R.string.uses_perm_update_body,
            ),
            Permission("FOREGROUND_SERVICE", R.string.uses_perm_service_title, R.string.uses_perm_service_body),
            Permission(
                "FOREGROUND_SERVICE_CONNECTED_DEVICE",
                R.string.uses_perm_servicetype_title,
                R.string.uses_perm_servicetype_body,
            ),
            Permission("POST_NOTIFICATIONS", R.string.uses_perm_notify_title, R.string.uses_perm_notify_body),
            Permission("SYSTEM_ALERT_WINDOW", R.string.uses_perm_overlay_title, R.string.uses_perm_overlay_body),
        )
}

/**
 * "What Edgepad uses": the permissions on this phone, what goes between phone and laptop, what reaches the internet and
 * what the laptop apps ask of the laptop. Static text from the app's own resources, so it costs the phone nothing; the
 * same content is in docs/DATA-AND-PERMISSIONS.md. Sideways the sections sit in two columns.
 */
object DataUsesScreen {
    fun build(
        ui: Ui,
        backLabel: String,
        onBack: () -> Unit,
    ): View =
        ui.page(ui.bar("", onBack, backLabel = backLabel)) {
            largeTitle(ui.string(R.string.uses_title))
            body(ui.string(R.string.uses_intro), Space.XS)
            columns(
                {
                    fold(ui, ui.string(R.string.uses_section_perms)) {
                        card {
                            DataUses.PERMISSIONS.forEachIndexed { i, p ->
                                if (i > 0) hairline()
                                add(row(ui, ui.string(p.title), ui.string(p.body), DataUses.PREFIX + p.name))
                            }
                        }
                    }
                    fold(ui, ui.string(R.string.uses_section_net)) {
                        card { list(ui, R.array.uses_net_keys, R.array.uses_net_values) }
                    }
                },
                {
                    fold(ui, ui.string(R.string.uses_section_flow)) {
                        footnote(ui.string(R.string.uses_flow_out))
                        card(Space.S) { list(ui, R.array.uses_out_keys, R.array.uses_out_values) }
                        footnote(ui.string(R.string.uses_flow_in))
                        card(Space.S) { list(ui, R.array.uses_in_keys, R.array.uses_in_values) }
                        footnote(ui.string(R.string.uses_flow_local))
                        card(Space.S) {
                            add(row(ui, ui.string(R.string.uses_local_key), ui.string(R.string.uses_local_value)))
                        }
                    }
                    fold(ui, ui.string(R.string.uses_section_laptop)) {
                        footnote(ui.string(R.string.uses_windows))
                        card(Space.S) { list(ui, R.array.uses_windows_keys, R.array.uses_windows_values) }
                        footnote(ui.string(R.string.uses_mac))
                        card(Space.S) { list(ui, R.array.uses_mac_keys, R.array.uses_mac_values) }
                        footnote(ui.string(R.string.uses_linux))
                        card(Space.S) { list(ui, R.array.uses_linux_keys, R.array.uses_linux_values) }
                        footnote(ui.string(R.string.uses_laptop_ai))
                        card(Space.S) { list(ui, R.array.uses_ai_keys, R.array.uses_ai_values) }
                    }
                },
            )
        }

    /**
     * A section that starts closed: a [title] header that is a button (48 dp tall, its state spoken) over the [fill]
     * rows. Opening and closing run over [Motion.REVEAL] and are instant when the user has animations off.
     */
    private fun Column.fold(
        ui: Ui,
        title: String,
        fill: Column.() -> Unit,
    ) {
        val content =
            LinearLayout(ui.context).apply {
                orientation = LinearLayout.VERTICAL
                visibility = View.GONE
            }
        Column(ui, content).fill()
        val chevron =
            Glyph(ui.context, Glyph.Shape.CHEVRON_RIGHT, ui.palette.dim).apply {
                importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO
            }
        val label = ui.section(title).apply { isAccessibilityHeading = false }
        val header =
            LinearLayout(ui.context).apply {
                gravity = Gravity.CENTER_VERTICAL
                minimumHeight = ui.dp(Space.TOUCH)
                addView(label, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
                addView(chevron, LinearLayout.LayoutParams(ui.dp(Space.XL), ui.dp(Space.XL)))
                isAccessibilityHeading = true
                stateDescription = ui.string(R.string.annotate_menu_collapsed)
                accessibilityDelegate =
                    object : View.AccessibilityDelegate() {
                        override fun onInitializeAccessibilityNodeInfo(
                            host: View,
                            info: AccessibilityNodeInfo,
                        ) {
                            super.onInitializeAccessibilityNodeInfo(host, info)
                            info.className = Button::class.java.name
                        }
                    }
            }
        var open = false
        var running: ValueAnimator? = null

        fun toggle() {
            open = !open
            header.stateDescription =
                ui.string(if (open) R.string.annotate_menu_expanded else R.string.annotate_menu_collapsed)
            running?.cancel()
            val turnTo = if (open) CHEVRON_OPEN else 0f
            val params = content.layoutParams
            if (!ValueAnimator.areAnimatorsEnabled() || header.width == 0) {
                params.height = ViewGroup.LayoutParams.WRAP_CONTENT
                content.layoutParams = params
                content.visibility = if (open) View.VISIBLE else View.GONE
                chevron.rotation = turnTo
                return
            }
            val from = if (content.visibility == View.VISIBLE) content.height else 0
            val to =
                if (open) {
                    content.measure(
                        View.MeasureSpec.makeMeasureSpec(header.width, View.MeasureSpec.EXACTLY),
                        View.MeasureSpec.makeMeasureSpec(0, View.MeasureSpec.UNSPECIFIED),
                    )
                    content.measuredHeight
                } else {
                    0
                }
            val turnFrom = chevron.rotation
            params.height = from
            content.layoutParams = params
            content.visibility = View.VISIBLE
            val move = ValueAnimator.ofFloat(0f, 1f)
            running = move
            move.duration = Motion.REVEAL
            move.interpolator = DecelerateInterpolator()
            move.addUpdateListener {
                val t = it.animatedValue as Float
                content.layoutParams = content.layoutParams.apply { height = from + ((to - from) * t).toInt() }
                chevron.rotation = turnFrom + (turnTo - turnFrom) * t
            }
            move.addListener(
                object : AnimatorListenerAdapter() {
                    private var cancelled = false

                    override fun onAnimationCancel(animation: Animator) {
                        cancelled = true
                    }

                    override fun onAnimationEnd(animation: Animator) {
                        if (cancelled) return
                        content.layoutParams =
                            content.layoutParams.apply { height = ViewGroup.LayoutParams.WRAP_CONTENT }
                        if (!open) content.visibility = View.GONE
                    }
                },
            )
            move.start()
        }
        ui.tappable(header) { toggle() }
        add(header)
        add(content)
    }

    /** A card's rows from two parallel arrays, a hairline between each. */
    private fun Column.list(
        ui: Ui,
        keys: Int,
        values: Int,
    ) {
        val names = ui.context.resources.getStringArray(keys)
        val texts = ui.context.resources.getStringArray(values)
        names.zip(texts).forEachIndexed { i, (name, text) ->
            if (i > 0) hairline()
            add(row(ui, name, text))
        }
    }

    /** A bold name over its explanation, and under that [tag] (a permission's own name) when there is one. */
    private fun row(
        ui: Ui,
        name: String,
        text: String,
        tag: String? = null,
    ): View =
        LinearLayout(ui.context).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(0, ui.dp(Space.M), 0, ui.dp(Space.M))
            addView(ui.text(name, Type.BODY, ui.palette.ink, face = Type.bold))
            addView(ui.text(text, Type.VALUE, ui.palette.dim).apply { setLineSpacing(0f, Type.LEADING) })
            if (tag != null) addView(ui.text(tag, Type.SMALL, ui.palette.dim))
        }
}
