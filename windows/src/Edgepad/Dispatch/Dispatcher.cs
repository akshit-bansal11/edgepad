using Edgepad.Controls;
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

    private static bool Do(Action action)
    {
        action();
        return true;
    }
}
