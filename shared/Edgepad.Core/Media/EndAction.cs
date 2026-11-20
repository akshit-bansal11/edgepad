using Edgepad.Protocol;

namespace Edgepad.Media;

/// <summary>What an armed <see cref="EndAction"/> does when the media ends.</summary>
internal enum EndKind
{
    Sleep,
    Shutdown,
}

/// <summary>
/// One look at what is playing, from a laptop's own media report. <c>Identity</c> is empty when nothing is playing or
/// paused; a duration of 0 or less means the player has no timeline.
/// </summary>
internal readonly record struct MediaSnapshot(string Identity, bool Playing, int PositionSeconds, int DurationSeconds);

/// <summary>
/// Sleep or shut this laptop down once the current media has ended. The phone arms it (EndSleep, EndShutdown) and
/// disarms it (EndCancel); each laptop calls <see cref="Observe"/> from the media report it already makes, so this
/// holds no timer. It fires <c>perform</c> once and clears when armed and playback stopped in the last
/// <see cref="EndWindowSeconds"/> of the timeline, or the track changed or vanished after them. With no timeline it
/// refuses to arm, because "the end" could not be told. <c>stateChanged</c> gets one <see cref="EndActionToken"/> on
/// every change. Shared by the session thread (Arm, Cancel) and the media thread (Observe).
/// </summary>
internal sealed class EndAction(Func<EndKind, bool> perform, Action<string> stateChanged)
{
    public const int EndWindowSeconds = 3;

    private readonly Lock gate = new();
    private EndKind? armed;
    private MediaSnapshot last;

    /// <summary>False, and nothing armed, when the last observed media has no timeline. Arming again replaces the kind.</summary>
    public bool Arm(EndKind kind)
    {
        string? token;
        lock (gate)
        {
            if (last.DurationSeconds <= 0)
            {
                return false;
            }

            token = armed == kind ? null : Token(kind);
            armed = kind;
        }

        Report(token);
        return true;
    }

    public void Cancel()
    {
        string? token = null;
        lock (gate)
        {
            if (armed is not null)
            {
                armed = null;
                token = EndActionToken.None;
            }
        }

        Report(token);
    }

    public void Observe(MediaSnapshot now)
    {
        EndKind? fire = null;
        lock (gate)
        {
            if (armed is { } kind && Ended(last, now))
            {
                fire = kind;
                armed = null;
            }

            last = now;
        }

        if (fire is { } due)
        {
            Report(EndActionToken.None);
            _ = perform(due);
        }
    }

    private static bool Ended(MediaSnapshot before, MediaSnapshot now)
    {
        var wasNearEnd = before.DurationSeconds > 0 && before.PositionSeconds >= before.DurationSeconds - EndWindowSeconds;
        var nearEnd = now.DurationSeconds > 0 && now.PositionSeconds >= now.DurationSeconds - EndWindowSeconds;

        // (a) stopped at the end; (b) another track after the end; (c) the session gone after the end.
        return (!now.Playing && nearEnd && now.Identity.Length > 0)
            || (wasNearEnd && now.Identity != before.Identity);
    }

    private static string Token(EndKind kind) => kind == EndKind.Sleep ? EndActionToken.Sleep : EndActionToken.Shutdown;

    private void Report(string? token)
    {
        if (token is not null)
        {
            stateChanged(token);
        }
    }
}
