package me.akshitbansal.edgepad

/**
 * The one logo lookup, keyed by the `LogoId` of the laptop's `DevProviders` list. A brand with an official mark saved
 * under `res/drawable` answers its drawable; every other id answers null and the caller draws its letter tile.
 * `docs/brand-marks.md` says which brands still wait for a file.
 */
object Logos {
    /** The vector for [logoId], or null when the brand has none yet. */
    fun drawable(logoId: String): Int? =
        when (logoId) {
            "github" -> R.drawable.ic_github
            "gitlab" -> R.drawable.logo_gitlab
            "opencode" -> R.drawable.logo_opencode
            else -> null
        }
}
