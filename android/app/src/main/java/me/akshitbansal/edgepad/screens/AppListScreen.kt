package me.akshitbansal.edgepad.screens

import android.text.Editable
import android.text.InputType
import android.text.TextUtils
import android.text.TextWatcher
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.view.inputmethod.EditorInfo
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.TextView
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type
import me.akshitbansal.edgepad.link.AppCatalogState

/**
 * The apps the laptop listed, to pick one as a macro: a search field over a row for each app, its icon beside its name.
 * A tap asks "Add this to the laptop?" and only a yes calls [onAdd], which sends the app's token and nothing else, so
 * what the macro launches is the laptop's business and not this screen's.
 *
 * It draws [catalog], which the link fills: [listChanged] when a new list lands and [iconArrived] for each picture
 * after it, since the pictures follow the list and a row is drawn without one until its own arrives. The rows are built
 * once a list and a search only hides the ones it does not match, so typing never builds a view. On a Windows laptop,
 * which lists the Start menu and nothing else, the page says how to get another app onto that list.
 *
 * @param catalog The selected laptop's list. Read on the main thread only, like everything here.
 * @param windows True for a Windows laptop, the only kind whose list a person can add to from the laptop.
 * @param onAdd The person said yes to adding this app.
 * @param onRefresh Asks the laptop for its list again.
 */
class AppListScreen(
    private val ui: Ui,
    private val laptopName: String,
    private val catalog: AppCatalogState,
    private val windows: Boolean,
    private val onAdd: (AppCatalogState.App) -> Unit,
    onRefresh: () -> Unit,
    onBack: () -> Unit,
) {
    /** One app's row: [view] holds the row and the hairline above it, [line] is that hairline alone. */
    private class Row(
        val app: AppCatalogState.App,
        val view: View,
        val line: View,
        val picture: ImageView,
    )

    private var rows: List<Row> = emptyList()
    private var query = ""
    private lateinit var list: LinearLayout
    private lateinit var message: TextView
    private lateinit var hint: TextView

    val view: View =
        ui.page(
            ui.bar(
                ui.string(R.string.apps_title),
                onBack,
                ui.icon(Glyph.Shape.REFRESH, ui.string(R.string.refresh), onRefresh),
                backLabel = ui.string(R.string.macros_title),
            ),
        ) {
            card { add(searchField()) }
            list = card(Space.M) {}
            message = body("", Space.L).apply { gravity = Gravity.CENTER }
            hint = footnote(ui.string(R.string.apps_windows_hint))
        }

    init {
        listChanged()
    }

    /** A new list, or none yet: the rows are built again from [catalog], and what the search matches is kept. */
    fun listChanged() {
        list.removeAllViews()
        rows = catalog.apps.map(::row)
        rows.forEach { list.addView(it.view) }
        applyFilter()
    }

    /** The picture for the app [token] names has arrived: that row is dressed in it and no other is touched. */
    fun iconArrived(token: String) {
        val row = rows.firstOrNull { it.app.token == token } ?: return
        row.picture.setImageDrawable(iconDrawable(ui, catalog.icon(token)))
    }

    private fun applyFilter() {
        val matching = AppCatalogState.filter(catalog.apps, query).map { it.token }.toSet()
        var first = true
        for (row in rows) {
            val shown = row.app.token in matching
            row.view.visibility = if (shown) View.VISIBLE else View.GONE
            row.line.visibility = if (shown && !first) View.VISIBLE else View.GONE
            if (shown) first = false
        }
        list.visibility = if (matching.isEmpty()) View.GONE else View.VISIBLE
        val words =
            when {
                !catalog.loaded -> ui.string(R.string.apps_loading, laptopName)
                catalog.apps.isEmpty() -> ui.string(R.string.apps_none, laptopName)
                matching.isEmpty() -> ui.string(R.string.apps_no_match)
                else -> ""
            }
        message.text = words
        message.visibility = if (words.isEmpty()) View.GONE else View.VISIBLE
        // Said once a list is in and not while it is awaited: it answers "where is my app", not "is it coming".
        hint.visibility = if (windows && catalog.loaded) View.VISIBLE else View.GONE
    }

    private fun row(app: AppCatalogState.App): Row {
        val picture =
            ImageView(ui.context).apply {
                scaleType = ImageView.ScaleType.FIT_CENTER
                val inset = ui.dp(Space.XS)
                setPadding(inset, inset, inset, inset)
                setImageDrawable(iconDrawable(ui, catalog.icon(app.token)))
            }
        // Until its picture arrives, and for an app that never sends one, a plain tile holds the place.
        val tile =
            FrameLayout(ui.context).apply {
                background = ui.rounded(ui.palette.faint, Space.TILE_RADIUS)
                importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO
                val match = ViewGroup.LayoutParams.MATCH_PARENT
                addView(picture, FrameLayout.LayoutParams(match, match))
            }
        val name =
            ui.text(app.name, Type.BODY, ui.palette.ink).apply {
                maxLines = 1
                ellipsize = TextUtils.TruncateAt.END
            }
        val content =
            LinearLayout(ui.context).apply {
                gravity = Gravity.CENTER_VERTICAL
                minimumHeight = ui.dp(Space.ROW)
                val side = ui.dp(TILE_DP)
                addView(tile, LinearLayout.LayoutParams(side, side).apply { marginEnd = ui.dp(Space.M) })
                addView(name, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
                ui.tappable(this) { confirm(app) }
            }
        val line = ui.hairline()
        val holder =
            LinearLayout(ui.context).apply {
                orientation = LinearLayout.VERTICAL
                addView(line, ViewGroup.LayoutParams.MATCH_PARENT, ui.dp(Space.HAIR))
                addView(content)
            }
        return Row(app, holder, line, picture)
    }

    /** Over the list and not the row: a list that arrives while the question is up rebuilds the rows, and it must stay. */
    private fun confirm(app: AppCatalogState.App) {
        ui.confirm(
            list,
            ui.string(R.string.apps_confirm_title, app.name, laptopName),
            ui.string(R.string.apps_confirm_body),
            ui.string(R.string.apps_confirm_action),
        ) { onAdd(app) }
    }

    /** The platform's own field, bare inside a card: nothing in [Ui] draws one, and the card is its frame. */
    private fun searchField(): EditText =
        EditText(ui.context).apply {
            setSingleLine()
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS
            imeOptions = EditorInfo.IME_ACTION_DONE
            hint = ui.string(R.string.apps_search_hint)
            importantForAutofill = View.IMPORTANT_FOR_AUTOFILL_NO
            background = null
            typeface = Type.face
            setTextSize(TypedValue.COMPLEX_UNIT_SP, Type.BODY)
            setTextColor(ui.palette.ink)
            setHintTextColor(ui.palette.dim)
            minimumHeight = ui.dp(Space.ROW)
            addTextChangedListener(
                object : TextWatcher {
                    override fun beforeTextChanged(
                        text: CharSequence?,
                        start: Int,
                        count: Int,
                        after: Int,
                    ) = Unit

                    override fun onTextChanged(
                        text: CharSequence?,
                        start: Int,
                        before: Int,
                        count: Int,
                    ) = Unit

                    override fun afterTextChanged(text: Editable?) {
                        query = text?.toString().orEmpty()
                        applyFilter()
                    }
                },
            )
        }

    private companion object {
        const val TILE_DP = 36f
    }
}
