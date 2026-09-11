package me.akshitbansal.edgepad.screens

import android.app.UiModeManager
import android.content.Context
import android.content.res.Configuration
import android.view.View
import android.widget.AdapterView
import android.widget.ArrayAdapter
import android.widget.Button
import android.widget.SeekBar
import android.widget.Spinner
import android.widget.Switch
import me.akshitbansal.edgepad.R
import me.akshitbansal.edgepad.Settings
import me.akshitbansal.edgepad.surface.DialKind
import me.akshitbansal.edgepad.surface.Edge
import me.akshitbansal.edgepad.surface.Placement

/**
 * Theme, where each dial sits, and the remembered laptop. Every change is saved as it is made; the
 * surface reads the settings when it is next built.
 */
object SettingsScreen {
    private const val PERCENT = 100

    fun build(
        context: Context,
        settings: Settings,
        onForgetLaptop: () -> Unit,
    ): View =
        Page.build(context) {
            title(R.string.settings)

            heading(R.string.settings_theme)
            val dark =
                (context.resources.configuration.uiMode and Configuration.UI_MODE_NIGHT_MASK) ==
                    Configuration.UI_MODE_NIGHT_YES
            addView(
                Switch(context).apply {
                    setText(R.string.settings_dark)
                    isChecked = dark
                    setOnCheckedChangeListener { _, checked ->
                        // The system remembers this per app and rebuilds the activity; the link survives that.
                        context.getSystemService(UiModeManager::class.java)?.setApplicationNightMode(
                            if (checked) UiModeManager.MODE_NIGHT_YES else UiModeManager.MODE_NIGHT_NO,
                        )
                    }
                },
            )
            gap()

            heading(R.string.settings_dials)
            body(R.string.settings_dials_hint)
            for (kind in DialKind.entries) dialRow(context, settings, kind)

            heading(R.string.settings_laptop)
            addView(
                Button(context).apply {
                    setText(R.string.forget_laptop)
                    isEnabled = settings.laptop != null
                    setOnClickListener {
                        onForgetLaptop()
                        isEnabled = false
                    }
                },
            )
        }

    private fun android.widget.LinearLayout.dialRow(
        context: Context,
        settings: Settings,
        kind: DialKind,
    ) {
        var placement = settings.placement(kind)
        body(context.getString(kind.labelRes))
        val edges = Edge.entries
        val names = edges.map { context.getString(edgeLabel(it)) }
        addView(
            Spinner(context).apply {
                adapter =
                    ArrayAdapter(context, android.R.layout.simple_spinner_item, names).apply {
                        setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item)
                    }
                setSelection(edges.indexOf(placement.edge))
                onItemSelectedListener =
                    object : AdapterView.OnItemSelectedListener {
                        override fun onItemSelected(
                            parent: AdapterView<*>?,
                            view: View?,
                            position: Int,
                            id: Long,
                        ) {
                            placement = placement.copy(edge = edges[position])
                            settings.place(kind, placement)
                        }

                        override fun onNothingSelected(parent: AdapterView<*>?) = Unit
                    }
            },
        )
        addView(
            SeekBar(context).apply {
                max = PERCENT
                progress = (placement.along * PERCENT).toInt()
                contentDescription =
                    context.getString(R.string.settings_position_description, context.getString(kind.labelRes))
                setOnSeekBarChangeListener(
                    object : SeekBar.OnSeekBarChangeListener {
                        override fun onProgressChanged(
                            seekBar: SeekBar?,
                            progress: Int,
                            fromUser: Boolean,
                        ) {
                            if (!fromUser) return
                            placement = placement.copy(along = progress / PERCENT.toFloat())
                            settings.place(kind, placement)
                        }

                        override fun onStartTrackingTouch(seekBar: SeekBar?) = Unit

                        override fun onStopTrackingTouch(seekBar: SeekBar?) = Unit
                    },
                )
            },
        )
        gap()
    }

    private fun edgeLabel(edge: Edge): Int =
        when (edge) {
            Edge.OFF -> R.string.edge_off
            Edge.TOP -> R.string.edge_top
            Edge.RIGHT -> R.string.edge_right
            Edge.BOTTOM -> R.string.edge_bottom
            Edge.LEFT -> R.string.edge_left
        }
}
