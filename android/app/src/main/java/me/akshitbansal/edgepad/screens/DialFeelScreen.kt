package me.akshitbansal.edgepad.screens

import android.content.ActivityNotFoundException
import android.content.Intent
import android.net.Uri
import android.util.Log
import android.view.View
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.Switch
import android.widget.Toast
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.Space
import me.akshitbansal.edgepad.surface.DialKind
import me.akshitbansal.edgepad.surface.Perimeter
import kotlin.math.roundToInt
import android.provider.Settings as SystemSettings

/**
 * How the corner dials respond and how big they are, with a live corner beside the sliders.
 *
 * Sensitivity comes in two layers. The shared slider is the one most people ever touch; under the preview,
 * each dial kind may take a speed of its own, because they do not want the same one — volume runs 0-100
 * over a thumb's width and wants a slow ruler, while the app switcher is a stepper that wants a fast one.
 * A per-dial slider at its lowest step reads Shared and removes the override rather than storing a second
 * default, so changing the shared slider later still moves every dial that never asked to differ.
 */
object DialFeelScreen {
    private const val PREVIEW_DP = 220f

    /** Step 0 of a per-dial slider: follow the shared value instead of holding one. */
    private const val SHARED_STEP = 0

    private val sensitivityRange = StepRange(Settings.MIN_SENSITIVITY, Settings.MAX_SENSITIVITY, 0.1f)
    private val lengthRange = StepRange(Settings.MIN_DIAL_LENGTH, Settings.MAX_DIAL_LENGTH, 10f)
    private val heightRange = StepRange(Settings.MIN_DIAL_HEIGHT, Settings.MAX_DIAL_HEIGHT, 0.1f)

    fun build(
        ui: Ui,
        settings: Settings,
        onBack: () -> Unit,
    ): View {
        val placed = placedDials(settings)
        val preview = DialPreview(ui.context, settings)
        // The preview sits on a card of its own, clipped to the card's corners like every other surface here.
        val frame =
            FrameLayout(ui.context).apply {
                background = ui.rounded(ui.palette.card, Space.PANEL_RADIUS)
                clipToOutline = true
                addView(preview)
            }
        return ui.page(
            ui.bar(ui.string(R.string.dial_feel_title), onBack, backLabel = ui.string(R.string.settings_surface_title)),
        ) {
            columns(
                {
                    card(Space.L) {
                        add(
                            ui.slider(
                                ui.string(R.string.slide_sensitivity),
                                sensitivityRange.steps,
                                sensitivityRange.stepOf(settings.sensitivity),
                                { step -> ui.string(R.string.multiplier_value, sensitivityRange.valueAt(step)) },
                            ) { step -> settings.sensitivity = sensitivityRange.valueAt(step) },
                        )
                        hairline()
                        add(
                            ui.slider(
                                ui.string(R.string.dial_length),
                                lengthRange.steps,
                                lengthRange.stepOf(settings.dialLength),
                                { step ->
                                    ui.string(R.string.dial_length_value, lengthRange.valueAt(step).roundToInt())
                                },
                            ) { step ->
                                settings.dialLength = lengthRange.valueAt(step)
                                preview.show(settings.dialLength, settings.dialHeight)
                            },
                        )
                        hairline()
                        add(
                            ui.slider(
                                ui.string(R.string.dial_height),
                                heightRange.steps,
                                heightRange.stepOf(settings.dialHeight),
                                { step -> ui.string(R.string.multiplier_value, heightRange.valueAt(step)) },
                            ) { step ->
                                settings.dialHeight = heightRange.valueAt(step)
                                preview.show(settings.dialLength, settings.dialHeight)
                            },
                        )
                        hairline()
                        add(ui.toggle(ui.string(R.string.snap_round), settings.snap) { settings.snap = it })
                    }
                },
                {
                    add(frame, Space.L, ui.dp(PREVIEW_DP))
                    // Only the dials that are in a corner: a slider for a dial that is nowhere changes nothing.
                    if (placed.isNotEmpty()) {
                        // Off hides the list and nothing else: the speeds stored for each dial stay.
                        val each = LinearLayout(ui.context).apply { orientation = LinearLayout.VERTICAL }
                        each.visibility = if (settings.eachDialSpeed) View.VISIBLE else View.GONE
                        Column(ui, each).apply {
                            card {
                                placed.forEachIndexed { i, kind ->
                                    if (i > 0) hairline()
                                    add(perDial(ui, settings, kind))
                                }
                            }
                        }
                        card(Space.L) {
                            add(
                                ui.toggle(ui.string(R.string.dial_feel_each), settings.eachDialSpeed) { on ->
                                    settings.eachDialSpeed = on
                                    each.visibility = if (on) View.VISIBLE else View.GONE
                                },
                            )
                        }
                        add(each, Space.L)
                    }
                },
            )
        }
    }

    /**
     * The floating dials need a connected laptop, Stay connected (the service keeps the process) and the permission to
     * draw over other apps. Missing one, the switch stays off and says which; the permission is asked for from this
     * tap only. Shown on the surface's settings, with the footnote that says what it needs.
     */
    internal fun floatingToggle(
        ui: Ui,
        settings: Settings,
        laptopConnected: () -> Boolean,
        onFloatingChanged: () -> Unit,
    ): View {
        lateinit var box: Switch
        box =
            ui.toggle(ui.string(R.string.controls_floating), settings.floatingDials) { on ->
                val refusal =
                    when {
                        !on -> null
                        !laptopConnected() -> R.string.floating_needs_link
                        !settings.stayConnected -> R.string.floating_needs_stay
                        !SystemSettings.canDrawOverlays(ui.context) -> R.string.floating_permission
                        else -> null
                    }
                if (refusal == null) {
                    settings.floatingDials = on
                    onFloatingChanged()
                } else {
                    box.isChecked = false
                    if (refusal == R.string.floating_permission) askOverlay(ui)
                    Toast.makeText(ui.context, refusal, Toast.LENGTH_LONG).show()
                }
            }
        return box
    }

    private fun askOverlay(ui: Ui) {
        val context = ui.context
        val intent =
            Intent(SystemSettings.ACTION_MANAGE_OVERLAY_PERMISSION, Uri.parse("package:" + context.packageName))
        try {
            context.startActivity(intent)
        } catch (e: ActivityNotFoundException) {
            Log.w("DialFeelScreen", "no screen for the overlay permission", e)
        }
    }

    /** The dials in the corners now, in the order the list of dials has them, each once. */
    private fun placedDials(settings: Settings): List<DialKind> {
        val split = settings.splitCorners
        val here =
            (0 until if (split) Perimeter.SLOTS else Perimeter.CORNERS)
                .mapNotNull { if (split) settings.slot(it) else settings.corner(it) }
                .toSet()
        return DialKind.entries.filter { it in here }
    }

    /** One dial kind's own sensitivity, with Shared as the step below the range's bottom. */
    private fun perDial(
        ui: Ui,
        settings: Settings,
        kind: DialKind,
    ): View {
        val start =
            if (settings.hasOwnSensitivity(kind)) {
                sensitivityRange.stepOf(settings.sensitivityOf(kind)) + 1
            } else {
                SHARED_STEP
            }
        return ui.slider(
            ui.string(kind.nameRes),
            sensitivityRange.steps + 1,
            start,
            { step ->
                if (step == SHARED_STEP) {
                    ui.string(R.string.dial_feel_shared)
                } else {
                    ui.string(R.string.multiplier_value, sensitivityRange.valueAt(step - 1))
                }
            },
        ) { step ->
            settings.setSensitivityOf(kind, if (step == SHARED_STEP) null else sensitivityRange.valueAt(step - 1))
        }
    }
}
