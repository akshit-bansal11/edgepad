using System.ComponentModel;
using System.Runtime.InteropServices;
using Edgepad.Protocol;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace Edgepad.Gamepad;

/// <summary>
/// The laptop's virtual Xbox controller. ViGEmBus plugs a pad into Windows that every game already knows how
/// to read, and each PAD_STATE frame is copied onto its report field for field — the frame carries XInput's
/// own layout, so there is nothing to translate on the way in.
/// <para>
/// ViGEmBus is a separate install this laptop may simply not have, may have uninstalled, or may have at a
/// version this client cannot talk to. That is an ordinary state here and never an exception that escapes:
/// every entry point answers with a <see cref="PadStatus"/> token instead, which is the whole reason the
/// protocol has one. A tray app has no dialog to show it and no spare thread to lose — an escape from the
/// session thread would take the link, and everything the phone was holding down, with it.
/// </para>
/// <para>
/// Used from one session thread only, like <c>InputInjector</c>, and unplugged when that session ends.
/// </para>
/// </summary>
internal sealed class VirtualPad(Func<ViGEmClient>? openClient = null) : IDisposable
{
    private const string VigemNamespace = "Nefarius.ViGEm.Client";

    /// <summary>
    /// How the driver handle is opened. The real one by default; the parameter exists so a test can hand in
    /// one that throws what a missing ViGEmBus throws. That is the only way to pin the no-driver answer,
    /// since whether the machine running the suite has the driver is not ours to choose — and a test that
    /// opened the real client on a machine that does have it would plug a controller into whoever ran it.
    /// </summary>
    private readonly Func<ViGEmClient> open = openClient ?? (() => new ViGEmClient());

    private ViGEmClient? client;
    private IXbox360Controller? pad;
    private bool asked;

    /// <summary>
    /// Whether a controller could be offered at all, as the token the phone branches on. This is what goes
    /// out after the handshake, so the phone can hide the pad rather than draw a button that does nothing.
    /// </summary>
    public string Probe() => Client() is null ? PadStatus.NoDriver : PadStatus.Ready;

    /// <summary>Plugs the pad in, and says how that went as one <see cref="PadStatus"/> token.</summary>
    public string Attach()
    {
        if (Client() is not { } bus)
        {
            return PadStatus.NoDriver;
        }

        if (pad is not null)
        {
            // Asking twice is not an error. The phone sends PAD_ATTACH whenever it opens the gamepad
            // screen, and unplugging and replugging under a running game would cost that game its
            // controller for the sake of a request that asked for nothing new.
            return PadStatus.Ready;
        }

        try
        {
            var plugged = bus.CreateXbox360Controller();

            // One report per frame rather than one per field: PAD_STATE is a snapshot of the whole pad, and
            // auto-submit would push seven half-written reports for every frame — a game sampling between
            // two of them would read a stick that had moved and a button that had not.
            plugged.AutoSubmitReport = false;
            plugged.Connect();
            pad = plugged;
            Log.Write("Virtual controller plugged in");
            return PadStatus.Ready;
        }
        catch (Exception e) when (FromDriver(e))
        {
            // The driver is there and still said no: every free slot taken, an allocation that failed, a bus
            // handle that has gone stale. A different answer from no-driver because it is a different thing
            // for the phone to tell its user — this one may work on the next try.
            Log.Write($"Virtual controller: ViGEmBus would not plug one in: {e.Message}");
            return PadStatus.AttachFailed;
        }
    }

    /// <summary>Unplugs the pad, and says whether another one could be plugged in after it.</summary>
    public string Detach()
    {
        Unplug();
        return Probe();
    }

    /// <summary>
    /// Copies one frame onto the pad's report. False when nothing is plugged in, which the dispatcher counts
    /// as a dropped frame: a phone sending pad frames it never asked for a pad for is doing nothing here.
    /// </summary>
    public bool Update(PadState state)
    {
        if (pad is not { } plugged)
        {
            return false;
        }

        try
        {
            // Field for field, in the frame's own order. The buttons go across whole: the payload's mask is
            // XInput's own wButtons, so this is a copy and not a mapping.
            plugged.SetButtonsFull(state.Buttons);
            plugged.SetSliderValue(Xbox360Slider.LeftTrigger, state.Lt);
            plugged.SetSliderValue(Xbox360Slider.RightTrigger, state.Rt);
            plugged.SetAxisValue(Xbox360Axis.LeftThumbX, state.Lx);
            plugged.SetAxisValue(Xbox360Axis.LeftThumbY, state.Ly);
            plugged.SetAxisValue(Xbox360Axis.RightThumbX, state.Rx);
            plugged.SetAxisValue(Xbox360Axis.RightThumbY, state.Ry);
            plugged.SubmitReport();
            return true;
        }
        catch (Exception e) when (FromDriver(e))
        {
            // The driver went away under a live pad: uninstalled mid-session, or restarted by its own
            // installer. Logged once and then given up on, not logged per frame — this runs on the input
            // path, where file I/O costs latency, and a pad that has stopped taking reports will not take
            // the next one either. Dropping it here is what makes every following frame a cheap false.
            Log.Write($"Virtual controller: the pad stopped taking reports: {e.Message}");
            pad = null;
            return false;
        }
    }

    public void Dispose()
    {
        // A phone that drops mid-game must not leave a controller plugged in with a stick pushed forward.
        // Unplugging is what clears it: the game sees the device go away, not a pad frozen mid-input.
        Unplug();
        try
        {
            client?.Dispose();
        }
        catch (Exception e) when (FromDriver(e))
        {
            Log.Write($"Virtual controller: closing the driver handle failed: {e.Message}");
        }

        client = null;
    }

    /// <summary>
    /// True for anything the ViGEm client throws. Its exception types each derive straight from
    /// <see cref="Exception"/> with no base of their own, so naming them one by one here would be a list of
    /// seventeen to keep in step with someone else's library — and the one it missed would be exactly the
    /// unhandled crash this class exists to prevent. The three named types are the ways the interop layer
    /// underneath them fails without an exception of its own: a raw last-error, a fault out of the native
    /// client, and a handle used after it was closed. The session's last act calls into here from a finally
    /// block on a thread whose death would take the process, so the net is drawn wide on purpose.
    /// </summary>
    private static bool FromDriver(Exception e) =>
        e is Win32Exception or SEHException or ObjectDisposedException
        || e.GetType().Namespace?.StartsWith(VigemNamespace, StringComparison.Ordinal) == true;

    /// <summary>
    /// The one driver handle this session gets, opened the first time anything needs it and not asked for
    /// again. Not asked for again because a phone can press attach as often as it likes, and retrying a
    /// driver that is not installed would be a probe and a log line per press. The cost is that a ViGEmBus
    /// installed while the phone is connected is not seen until it reconnects, which is one reconnect after
    /// an install the user has just done and is already watching.
    /// </summary>
    private ViGEmClient? Client()
    {
        if (asked)
        {
            return client;
        }

        asked = true;
        try
        {
            client = open();
        }
        catch (Exception e) when (FromDriver(e))
        {
            // Not installed, a version this client cannot talk to, or a handle it was refused. All three
            // are no-driver to the phone: it falls back to the keyboard either way, and the log is where
            // the difference between them is kept.
            Log.Write($"Virtual controller: ViGEmBus is not usable on this laptop: {e.Message}");
        }

        return client;
    }

    private void Unplug()
    {
        if (pad is not { } plugged)
        {
            return;
        }

        // Cleared before the call, not after: a Disconnect that throws must still leave this pad gone, or
        // the frames after it would go on writing to a target the driver has already taken away.
        pad = null;
        try
        {
            plugged.Disconnect();
            Log.Write("Virtual controller unplugged");
        }
        catch (Exception e) when (FromDriver(e))
        {
            Log.Write($"Virtual controller: unplugging failed: {e.Message}");
        }
    }
}
