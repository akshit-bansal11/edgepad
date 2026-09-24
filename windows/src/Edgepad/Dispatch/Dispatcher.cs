using Edgepad.Controls;
using Edgepad.Gamepad;
using Edgepad.Injection;
using Edgepad.Macros;
using Edgepad.Protocol;

namespace Edgepad.Dispatch;

/// <summary>
/// Turns frames from the trusted phone into input and control changes, on the session thread. Every id is
/// checked against the protocol's own enums: an id this laptop does not know is dropped and counted,
/// never guessed at. The phone can name an action; only this table decides what it does.
/// </summary>
internal sealed class Dispatcher(
    InputInjector input,
    VirtualPad pad,
    AudioEndpoint speakers,
    AudioEndpoint microphone,
    BrightnessControl brightness,
    MediaSessions media,
    DisplayModes display,
    MacroStore macros,
    LevelOverlay overlay)
{
    private const byte MaxButton = 2;
    private const byte MaxPercent = 100;
    private const int BrightnessStep = 10;

    /// <summary>Frames that asked for something unknown, out of range, or unavailable (no microphone).</summary>
    public int Dropped { get; private set; }

    /// <summary>
    /// Where the answer to PAD_ATTACH and PAD_DETACH goes. Set by the session once it has a socket to write
    /// to: this class deliberately holds no reference to the connection, for the same reason PONG is
    /// answered in the session and not here. The pad is the one thing in this table the phone has to hear
    /// back about, because it draws a different screen depending on the token.
    /// </summary>
    public Action<string>? PadStatusReply { get; set; }

    public void Handle(Frame frame)
    {
        var handled = frame switch
        {
            Move m => Do(() => input.Move(m.Dx, m.Dy)),
            PointerButton b when b.Id <= MaxButton => Do(() => input.Button(b.Id, b.Down)),
            Scroll s => Do(() => input.Scroll(s.Dx, s.Dy)),
            Zoom z => Do(() => input.Zoom(z.Delta)),
            RunAction a => Run(a.Id),
            SetValue v when v.Value <= MaxPercent => Set(v.Control, v.Value),
            Text t when t.Kind == (byte)TextKind.Type => Do(() => input.Type(t.Value)),
            Key k => Do(() => input.Key(k.Code, k.Down)),
            // A pad frame with no pad plugged in is dropped and counted like any other id this laptop
            // cannot act on. The phone is not meant to send one before its PAD_ATTACH was answered
            // "ready", and a laptop that quietly accepted them would look to it exactly like one playing.
            PadState p => pad.Update(p),
            _ => false,
        };

        if (!handled)
        {
            Dropped++;
        }
    }

    private bool Run(byte id)
    {
        // Macros are a block of ids, not one each, so they are checked before the table. The phone names a
        // slot and nothing else: what that slot opens was typed into this laptop's own editor, which is the
        // whole reason a phone can be trusted with the button at all.
        var slot = id - (byte)ActionId.MacroBase;
        if (slot >= 0 && slot < MacroStore.MaxMacros)
        {
            return macros.Run(slot);
        }

        // The default arm rejects ids this laptop does not know; there is no pre-check to keep in step.
        switch ((ActionId)id)
        {
            case ActionId.MuteToggle:
                input.Chord(Keys.VolumeMute);
                return true;
            case ActionId.PlayPause:
                input.Chord(Keys.MediaPlayPause);
                return true;
            case ActionId.NextTrack:
                input.Chord(Keys.MediaNextTrack);
                return true;
            case ActionId.PreviousTrack:
                input.Chord(Keys.MediaPreviousTrack);
                return true;
            case ActionId.MicMuteToggle:
                return microphone.ToggleMute();
            case ActionId.Lock:
                return NativeMethods.LockWorkStation();
            case ActionId.TaskView:
                input.Chord(Keys.LWin, Keys.Tab);
                return true;
            case ActionId.ShowDesktop:
                input.Chord(Keys.LWin, Keys.D);
                return true;
            case ActionId.Search:
                input.Chord(Keys.LWin, Keys.S);
                return true;
            case ActionId.Notifications:
                input.Chord(Keys.LWin, Keys.N);
                return true;
            case ActionId.DesktopLeft:
                input.Chord(Keys.LWin, Keys.ControlKey, Keys.Left);
                return true;
            case ActionId.DesktopRight:
                input.Chord(Keys.LWin, Keys.ControlKey, Keys.Right);
                return true;
            case ActionId.AppSwitchBegin:
                // Alt stays down across frames, exactly as a three-finger swipe keeps the switcher open.
                input.Hold(Keys.Menu);
                input.Chord(Keys.Tab);
                return true;
            case ActionId.AppSwitchNext:
                // Without Alt held this would be a bare Tab into whatever has focus.
                return input.IsHeld(Keys.Menu) && Do(() => input.Chord(Keys.Tab));
            case ActionId.AppSwitchPrevious:
                return input.IsHeld(Keys.Menu) && Do(() => input.Chord(Keys.ShiftKey, Keys.Tab));
            case ActionId.AppSwitchEnd:
                input.Release(Keys.Menu);
                return true;
            case ActionId.ZoomReset:
                input.Chord(Keys.ControlKey, Keys.D0);
                return true;
            case ActionId.VolumeUp:
                input.Chord(Keys.VolumeUp);
                return true;
            case ActionId.VolumeDown:
                input.Chord(Keys.VolumeDown);
                return true;
            case ActionId.BrightnessUp:
                return brightness.Step(BrightnessStep);
            case ActionId.BrightnessDown:
                return brightness.Step(-BrightnessStep);
            case ActionId.PadAttach:
                return Answer(pad.Attach());
            case ActionId.PadDetach:
                return Answer(pad.Detach());
            default:
                return false;
        }
    }

    private bool Set(byte control, byte percent) => (ControlId)control switch
    {
        ControlId.Volume => Show("VOLUME", percent, speakers.SetLevel(percent)),
        ControlId.MicLevel => Show("MIC", percent, microphone.SetLevel(percent)),
        ControlId.Brightness => Show("BRIGHTNESS", percent, Do(() => brightness.Set(percent))),
        ControlId.MediaPosition => media.Seek(percent),
        // The value is an index into the rate list, never hertz: the 0-100 guard above is what makes that
        // safe, and it is also why a rate this display cannot do is unrepresentable rather than merely
        // refused. Queued rather than applied here, because a switch blanks the panel for about a second.
        ControlId.RefreshRate => RefreshRate(percent),
        _ => false,
    };

    private bool RefreshRate(byte index)
    {
        if (index >= display.Rates.Count)
        {
            return false;
        }

        display.SetLatest(index);
        // No readout for this one. The overlay's number is a percentage with a bar behind it, and a rate is
        // neither; and a mode switch blanks the whole panel, which is feedback nothing needs to improve on.
        return true;
    }

    /// <summary>
    /// Puts the laptop's own readout on screen when a change took. Windows shows one of these for its
    /// volume keys but not for a level set through Core Audio or WMI, so without it the only feedback for
    /// a dial the user is not looking at is the sound itself — and brightness has none at all.
    ///
    /// Muting is not routed here: it goes through the media key, for which Windows draws its own readout.
    /// Reading the mute flag back would also mean a Core Audio call on the receive thread for every frame
    /// of a drag, to draw a state that setting a level cannot have changed.
    /// </summary>
    private bool Show(
        string label,
        int percent,
        bool changed)
    {
        if (changed)
        {
            overlay.Show(label, percent);
        }

        return changed;
    }

    /// <summary>
    /// Sends the pad's state back to the phone. Always handled, even for "no-driver": the ask was
    /// understood and answered, and a phone that heard nothing could not tell a laptop without the driver
    /// from one too old to know the id at all — which are two different things to put on its screen.
    /// </summary>
    private bool Answer(string status)
    {
        PadStatusReply?.Invoke(status);
        return true;
    }

    private static bool Do(Action action)
    {
        action();
        return true;
    }
}
