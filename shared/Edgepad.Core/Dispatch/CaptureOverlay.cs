namespace Edgepad.Dispatch;

/// <summary>
/// Remembers whether a capture overlay (the region crosshair, the capture toolbar) may still be open on this
/// laptop, so the next capture action, or the phone leaving its capture screen, can close it first. Pure: it
/// decides and the platform presses, so a test can read every decision without sending a key.
///
/// Why a guess and not a fact: no OS tells us the overlay closed. The person finishing a drag closes it with
/// no frame from the phone. So the memory expires, and the ceiling is the safety limit. An Escape sent while
/// no overlay is open lands in whatever app has focus, where it can close a dialog or leave full screen.
/// <see cref="PendingMs"/> is therefore short: long enough for a person to aim a drag and cancel it from the
/// phone, short enough that "I finished the drag and then tapped Full screen" is usually past it. Inside the
/// window that case still sends one stray Escape, which is the accepted cost.
/// </summary>
internal sealed class CaptureOverlay(Func<long> clockMs)
{
    /// <summary>How long after a capture action that opens an overlay we still believe it is open.</summary>
    public const long PendingMs = 8_000;

    /// <summary>
    /// How long to wait between the dismissing Escape and the next shortcut. The overlay's window tears down
    /// on its own thread and the new shortcut is read by the shell as soon as it arrives; 50 ms is about three
    /// frames at 60 Hz, enough for the teardown to finish, and short enough that the switch feels immediate.
    /// Unverified on real machines, which is why it is one constant.
    /// </summary>
    public const int DismissWaitMs = 50;

    private const long NotPending = -1;

    private long openedAt = NotPending;

    /// <summary>Whether an overlay may be open right now.</summary>
    public bool IsPending => openedAt != NotPending && clockMs() - openedAt < PendingMs;

    /// <summary>
    /// A capture action is about to be pressed. Returns true when an Escape must be pressed first.
    /// <paramref name="opensOverlay"/> is whether the new action leaves an overlay open itself.
    /// </summary>
    public bool Begin(bool opensOverlay)
    {
        var dismiss = IsPending;
        openedAt = opensOverlay ? clockMs() : NotPending;
        return dismiss;
    }

    /// <summary>The phone left its capture screen. Returns true when an Escape must be pressed.</summary>
    public bool Cancel()
    {
        var dismiss = IsPending;
        openedAt = NotPending;
        return dismiss;
    }
}
