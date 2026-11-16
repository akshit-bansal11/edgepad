namespace Edgepad.Injection;

/// <summary>
/// Whether Windows is discarding this app's input, for TEXT INPUT_STATE. Windows refuses SendInput while an elevated
/// window has focus (UIPI), and says nothing else about it. The first refusal says "blocked" at once; "ok" waits until
/// input has gone through <paramref name="okAfterMs"/> after the last refusal, so a run of mixed batches cannot flicker
/// the phone's notice. Called from the session's read thread only.
/// </summary>
internal sealed class BlockedTracker(Func<long> clock, Action<bool> changed, long okAfterMs = 2000)
{
    private bool blocked;
    private long lastRefusal;

    public void Refused()
    {
        lastRefusal = clock();
        if (!blocked)
        {
            blocked = true;
            changed(true);
        }
    }

    public void Accepted()
    {
        // Called for every input batch, so the common case is one bool.
        if (!blocked)
        {
            return;
        }

        if (clock() >= lastRefusal + okAfterMs)
        {
            blocked = false;
            changed(false);
        }
    }
}
