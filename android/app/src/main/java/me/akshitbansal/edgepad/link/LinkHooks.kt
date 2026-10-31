package me.akshitbansal.edgepad.link

/**
 * How [LinkService], which outlives the activity's views, reaches the one thing it must do on its own: end every
 * link when the notification's Disconnect is tapped. A hook and not the manager itself, so nothing here can hold an
 * activity: the activity sets it in onCreate over the manager alone and clears it when it is really going away.
 * Called on the main thread, where the manager lives.
 */
object LinkHooks {
    @Volatile
    var disconnectAll: (() -> Unit)? = null
}
