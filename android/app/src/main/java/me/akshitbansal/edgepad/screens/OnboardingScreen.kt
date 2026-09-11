package me.akshitbansal.edgepad.screens

import android.content.Context
import android.view.View
import android.widget.Button
import me.akshitbansal.edgepad.R

/** First run: what to set up on the laptop, and what the fingers do. */
object OnboardingScreen {
    fun build(
        context: Context,
        onDone: () -> Unit,
    ): View =
        Page.build(context) {
            title(R.string.onboarding_title)
            body(R.string.onboarding_intro)
            heading(R.string.onboarding_setup_heading)
            body(R.string.onboarding_setup)
            heading(R.string.onboarding_dials_heading)
            body(R.string.onboarding_dials)
            heading(R.string.onboarding_trackpad_heading)
            body(R.string.onboarding_trackpad)
            gap()
            addView(
                Button(context).apply {
                    setText(R.string.onboarding_continue)
                    setOnClickListener { onDone() }
                },
            )
        }
}
