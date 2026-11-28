package me.akshitbansal.edgepad.screens

import android.os.Bundle
import android.provider.Settings.Global
import android.view.Gravity
import android.view.View
import android.view.accessibility.AccessibilityNodeInfo
import android.widget.Button
import android.widget.LinearLayout
import me.akshitbansal.edgepad.Motion
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type

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
 * same content is in docs/DATA-AND-PERMISSIONS.md. Sideways the sections sit in two columns. Every section starts
 * closed, so the page opens as a short list of what there is to read instead of a long wall of it.
 */
object DataUsesScreen {
    /** A right-pointing chevron turned this far points down. */
    private const val TURN = 90f

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
     * A section that opens and closes, closed at first. The header is a button: focusable, at least
     * [Space.TOUCH] tall, with a chevron that turns, and it says Expanded or Collapsed to a screen reader, both as its
     * state and as the expand and collapse actions TalkBack offers. The body fades over [Motion.REVEAL], and not at
     * all when the system's animations are off.
     */
    private fun Column.fold(
        ui: Ui,
        title: String,
        fill: Column.() -> Unit,
    ) {
        val still = Global.getFloat(ui.context.contentResolver, Global.ANIMATOR_DURATION_SCALE, 1f) == 0f
        val span = if (still) 0L else Motion.REVEAL
        val body =
            LinearLayout(ui.context).apply {
                orientation = LinearLayout.VERTICAL
                visibility = View.GONE
            }
        Column(ui, body).fill()
        val chevron =
            Glyph(ui.context, Glyph.Shape.CHEVRON_RIGHT, ui.palette.dim).apply {
                importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO
            }
        val header =
            LinearLayout(ui.context).apply {
                gravity = Gravity.CENTER_VERTICAL
                minimumHeight = ui.dp(Space.TOUCH)
                isAccessibilityHeading = true
                addView(
                    ui.text(title, Type.BODY, ui.palette.ink, face = Type.bold),
                    LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f),
                )
                addView(chevron, LinearLayout.LayoutParams(ui.dp(Space.XL), ui.dp(Space.XL)))
            }
        var open = false

        fun toggle() {
            open = !open
            val turned = if (header.layoutDirection == View.LAYOUT_DIRECTION_RTL) -TURN else TURN
            chevron
                .animate()
                .rotation(if (open) turned else 0f)
                .setDuration(span)
                .start()
            header.stateDescription =
                ui.string(if (open) R.string.annotate_menu_expanded else R.string.annotate_menu_collapsed)
            body.animate().cancel()
            if (open) {
                body.alpha = if (still) 1f else 0f
                body.visibility = View.VISIBLE
                body
                    .animate()
                    .alpha(1f)
                    .setDuration(span)
                    .start()
            } else if (still) {
                body.visibility = View.GONE
            } else {
                body
                    .animate()
                    .alpha(0f)
                    .setDuration(span)
                    .withEndAction { body.visibility = View.GONE }
                    .start()
            }
        }
        header.stateDescription = ui.string(R.string.annotate_menu_collapsed)
        ui.tappable(header, Space.CARD_RADIUS) { toggle() }
        header.accessibilityDelegate =
            object : View.AccessibilityDelegate() {
                override fun onInitializeAccessibilityNodeInfo(
                    host: View,
                    info: AccessibilityNodeInfo,
                ) {
                    super.onInitializeAccessibilityNodeInfo(host, info)
                    info.className = Button::class.java.name
                    info.addAction(
                        if (open) {
                            AccessibilityNodeInfo.AccessibilityAction.ACTION_COLLAPSE
                        } else {
                            AccessibilityNodeInfo.AccessibilityAction.ACTION_EXPAND
                        },
                    )
                }

                override fun performAccessibilityAction(
                    host: View,
                    action: Int,
                    args: Bundle?,
                ): Boolean {
                    val expand = AccessibilityNodeInfo.AccessibilityAction.ACTION_EXPAND.id
                    val collapse = AccessibilityNodeInfo.AccessibilityAction.ACTION_COLLAPSE.id
                    if ((action == expand && !open) || (action == collapse && open)) {
                        toggle()
                        return true
                    }
                    return super.performAccessibilityAction(host, action, args)
                }
            }
        add(header, Space.S)
        add(body)
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
