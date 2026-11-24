package me.akshitbansal.edgepad.screens

import android.content.Context
import android.content.res.ColorStateList
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.drawable.GradientDrawable
import android.graphics.drawable.RippleDrawable
import android.provider.Settings.Global
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.TextView
import me.akshitbansal.edgepad.Motion
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.Type

/**
 * The opening screen, shown on the first launch and once after each update: the logo playing its pop over the
 * trackpad's thin grid, the name, the version, a link to the project and a line saying a tap goes on. It never
 * dismisses itself. Always dark, whatever the theme: it is the logo's own stage. The name rises in at 600 ms and the
 * rest fades in at 800 ms, each over [Motion.REVEAL]; with the system's animations off everything is already there.
 */
object OpeningScreen {
    private const val LOGO_DP = 112f
    private const val GRID_DP = 22f
    private const val GRID_ALPHA = 14
    private const val NAME_SP = 34f
    private const val SMALL_SP = 14f
    private const val NAME_AT = 600L
    private const val REST_AT = 800L
    private const val RISE_DP = 8f
    private const val BUTTON_RADIUS_DP = 24f
    private const val GITHUB_ICON_DP = 20f
    private const val HINT_BOTTOM_DP = 32f
    private const val WRAP = ViewGroup.LayoutParams.WRAP_CONTENT

    private val ground = Color.parseColor("#0B0C0E")
    private val ink = Color.parseColor("#F2F3F5")
    private val grey = Color.parseColor("#9A9CA3")
    private val pill = Color.parseColor("#14F2F3F5")
    private val pillEdge = Color.parseColor("#3DF2F3F5")
    private val pressed = Color.parseColor("#29F2F3F5")

    /** The trackpad's grid under everything: one hairline every [GRID_DP], the ink at about 5.5%. */
    private class Ground(
        context: Context,
    ) : FrameLayout(context) {
        private val density = resources.displayMetrics.density
        private val line =
            Paint().apply {
                color = ink
                alpha = GRID_ALPHA
                strokeWidth = Space.HAIR * density
            }
        private var lines = FloatArray(0)

        init {
            setBackgroundColor(ground)
            setWillNotDraw(false)
        }

        override fun onSizeChanged(
            w: Int,
            h: Int,
            oldw: Int,
            oldh: Int,
        ) {
            super.onSizeChanged(w, h, oldw, oldh)
            val step = GRID_DP * density
            val across = (w / step).toInt() + 1
            val down = (h / step).toInt() + 1
            lines = FloatArray((across + down) * LINE_FLOATS)
            var at = 0
            for (i in 0 until across) {
                lines[at++] = i * step
                lines[at++] = 0f
                lines[at++] = i * step
                lines[at++] = h.toFloat()
            }
            for (j in 0 until down) {
                lines[at++] = 0f
                lines[at++] = j * step
                lines[at++] = w.toFloat()
                lines[at++] = j * step
            }
        }

        override fun onDraw(canvas: Canvas) {
            canvas.drawLines(lines, line)
        }

        private companion object {
            const val LINE_FLOATS = 4
        }
    }

    fun build(
        ui: Ui,
        version: String,
        onGithub: () -> Unit,
        onContinue: () -> Unit,
    ): View {
        val context = ui.context
        val root = Ground(context)
        root.contentDescription = ui.string(R.string.opening_screen_cd, version)
        root.setOnClickListener { onContinue() }
        ui.padToBars(root)

        val name = label(context, ui.string(R.string.app_name), NAME_SP, ink, bold = true)
        val versionLine = label(context, ui.string(R.string.opening_version, version), SMALL_SP, grey)
        versionLine.fontFeatureSettings = "tnum"
        val github = githubButton(ui, onGithub)
        val hint = label(context, ui.string(R.string.opening_continue), SMALL_SP, grey, announced = true)

        val column =
            LinearLayout(context).apply {
                orientation = LinearLayout.VERTICAL
                gravity = Gravity.CENTER_HORIZONTAL
                addView(LogoView(context), LinearLayout.LayoutParams(ui.dp(LOGO_DP), ui.dp(LOGO_DP)))
                addView(name, spaced(ui.dp(Space.XL)))
                addView(versionLine, spaced(ui.dp(Space.XS)))
                addView(github, spaced(ui.dp(Space.XL + Space.S)))
            }
        root.addView(column, FrameLayout.LayoutParams(WRAP, WRAP, Gravity.CENTER))
        root.addView(
            hint,
            FrameLayout.LayoutParams(WRAP, WRAP, Gravity.BOTTOM or Gravity.CENTER_HORIZONTAL).apply {
                bottomMargin = ui.dp(HINT_BOTTOM_DP)
            },
        )

        val rest = listOf(versionLine, github, hint)
        val still = Global.getFloat(context.contentResolver, Global.ANIMATOR_DURATION_SCALE, 1f) == 0f
        if (!still) {
            name.alpha = 0f
            name.translationY = ui.dp(RISE_DP).toFloat()
            // Invisible, not just clear: a button that cannot be seen must not be tappable.
            rest.forEach { it.visibility = View.INVISIBLE }
            root.addOnAttachStateChangeListener(
                object : View.OnAttachStateChangeListener {
                    override fun onViewAttachedToWindow(v: View) {
                        name
                            .animate()
                            .alpha(1f)
                            .translationY(0f)
                            .setStartDelay(NAME_AT)
                            .setDuration(Motion.REVEAL)
                            .start()
                        rest.forEach { view ->
                            view.alpha = 0f
                            view
                                .animate()
                                .alpha(1f)
                                .setStartDelay(REST_AT)
                                .setDuration(Motion.REVEAL)
                                .withStartAction { view.visibility = View.VISIBLE }
                                .start()
                        }
                    }

                    override fun onViewDetachedFromWindow(v: View) {
                        name.animate().cancel()
                        rest.forEach { it.animate().cancel() }
                    }
                },
            )
        }
        return root
    }

    private fun spaced(top: Int) = LinearLayout.LayoutParams(WRAP, WRAP).apply { topMargin = top }

    private fun label(
        context: Context,
        text: String,
        sp: Float,
        colour: Int,
        bold: Boolean = false,
        announced: Boolean = false,
    ) = TextView(context).apply {
        this.text = text
        setTextSize(TypedValue.COMPLEX_UNIT_SP, sp)
        setTextColor(colour)
        typeface = if (bold) Type.bold else Type.face
        gravity = Gravity.CENTER
        // The root announces the name and version once; these lines would repeat it.
        if (!announced) importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO
    }

    private fun githubButton(
        ui: Ui,
        onGithub: () -> Unit,
    ): View {
        val context = ui.context
        val shape =
            GradientDrawable().apply {
                cornerRadius = ui.dp(BUTTON_RADIUS_DP).toFloat()
                setColor(pill)
                setStroke(ui.dp(Space.HAIR), pillEdge)
            }
        val mark =
            ImageView(context).apply {
                setImageResource(R.drawable.ic_github)
                imageTintList = ColorStateList.valueOf(ink)
                importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO
            }
        val word = label(context, ui.string(R.string.opening_github), SMALL_SP + 1f, ink, bold = true)
        return LinearLayout(context).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            minimumHeight = ui.dp(Space.TOUCH)
            setPadding(ui.dp(Space.L), 0, ui.dp(Space.XL), 0)
            background = RippleDrawable(ColorStateList.valueOf(pressed), shape, null)
            contentDescription = ui.string(R.string.opening_github_cd)
            addView(mark, LinearLayout.LayoutParams(ui.dp(GITHUB_ICON_DP), ui.dp(GITHUB_ICON_DP)))
            addView(word, LinearLayout.LayoutParams(WRAP, WRAP).apply { marginStart = ui.dp(Space.S) })
            setOnClickListener { onGithub() }
        }
    }
}
