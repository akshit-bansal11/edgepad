package me.akshitbansal.edgepad.screens

import android.text.TextUtils
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.LinearLayout
import android.widget.PopupWindow
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type

private const val ICON_DP = 24f
private const val MIN_WIDTH_DP = 200f
private const val NAME_MAX_DP = 220f
private const val ELEVATION_DP = 8f

/** A connected laptop: the id the phone selects it by and the name it shows. */
data class DeviceRef(
    val id: String,
    val name: String,
)

/** One row of the device dropdown. [phone] rows have no laptop [id]. */
data class DeviceChoice(
    val id: String,
    val name: String,
    val phone: Boolean,
    val current: Boolean,
)

/** What a hub needs to list the laptops and to select one; selecting is the manager's own switch path. */
class DeviceSource(
    val laptops: () -> List<DeviceRef>,
    val selectedId: () -> String?,
    val select: (String) -> Unit,
)

/** Pure: the rows of the device dropdown. */
object DeviceChoices {
    /**
     * [laptops] in their order, then this phone when [phoneName] is not null (the Stats hub; Developer lists laptops
     * only). Exactly one row is current: the phone when [phoneCurrent], else the laptop whose id is [selectedId].
     */
    fun of(
        laptops: List<DeviceRef>,
        selectedId: String?,
        phoneName: String?,
        phoneCurrent: Boolean,
    ): List<DeviceChoice> {
        val rows = laptops.map { DeviceChoice(it.id, it.name, false, !phoneCurrent && it.id == selectedId) }
        return if (phoneName == null) rows else rows + DeviceChoice("", phoneName, true, phoneCurrent)
    }
}

/** A small popup list under [anchor], right edges lined up, closed by a tap outside it or on a row. */
internal fun Ui.showDeviceMenu(
    anchor: View,
    choices: List<DeviceChoice>,
    onPick: (DeviceChoice) -> Unit,
) {
    val window = PopupWindow(context)
    val list =
        LinearLayout(context).apply {
            orientation = LinearLayout.VERTICAL
            minimumWidth = dp(MIN_WIDTH_DP)
            setPadding(0, dp(Space.XS), 0, dp(Space.XS))
            for (choice in choices) {
                addView(
                    deviceRow(choice) {
                        window.dismiss()
                        onPick(choice)
                    },
                )
            }
        }
    window.contentView = list
    window.width = ViewGroup.LayoutParams.WRAP_CONTENT
    window.height = ViewGroup.LayoutParams.WRAP_CONTENT
    window.isFocusable = true
    window.isOutsideTouchable = true
    window.setBackgroundDrawable(rounded(palette.card, Space.PANEL_RADIUS))
    window.elevation = dp(ELEVATION_DP).toFloat()
    window.showAsDropDown(anchor, 0, dp(Space.XS), Gravity.END)
}

/** 48 dp high: the device's icon, its name, and a check when it is the one being looked at. */
private fun Ui.deviceRow(
    choice: DeviceChoice,
    onTap: () -> Unit,
): View {
    val slot = dp(ICON_DP)
    val shape = if (choice.phone) Glyph.Shape.PHONE else Glyph.Shape.LAPTOP
    val name =
        text(choice.name, Type.BODY, palette.ink, face = if (choice.current) Type.bold else Type.face).apply {
            maxLines = 1
            ellipsize = TextUtils.TruncateAt.END
            maxWidth = dp(NAME_MAX_DP)
        }
    val nameParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
    nameParams.marginStart = dp(Space.M)
    nameParams.marginEnd = dp(Space.M)
    return LinearLayout(context).apply {
        gravity = Gravity.CENTER_VERTICAL
        minimumHeight = dp(Space.TOUCH)
        setPaddingRelative(dp(Space.M), 0, dp(Space.M), 0)
        contentDescription = choice.name
        isSelected = choice.current
        addView(Glyph(context, shape, palette.dim), LinearLayout.LayoutParams(slot, slot))
        addView(name, nameParams)
        addView(
            Glyph(context, Glyph.Shape.CHECK, palette.accent).apply {
                visibility = if (choice.current) View.VISIBLE else View.INVISIBLE
            },
            LinearLayout.LayoutParams(slot, slot),
        )
        tappable(this, Space.M, onTap)
    }
}
