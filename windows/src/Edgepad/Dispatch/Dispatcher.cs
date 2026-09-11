using Edgepad.Controls;
using Edgepad.Injection;
using Edgepad.Protocol;

namespace Edgepad.Dispatch;

/// <summary>
/// Turns frames from the trusted phone into input and control changes, on the session thread. Every id is
/// checked against the protocol's own enums: an id this laptop does not know is dropped and counted,
/// never guessed at. The phone can name an action; only this table decides what it does.
/// </summary>
internal sealed class Dispatcher(InputInjector input, AudioEndpoint speakers, AudioEndpoint microphone, BrightnessControl brightness)
{
    private const byte MaxButton = 2;
    private const byte MaxPercent = 100;

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
            _ => false,
        };

        if (!handled)
        {
            Dropped++;
        }
    }

    private bool Run(byte id)
    {
        if (!Enum.IsDefined((ActionId)id))
        {
            return false;
        }

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
            default:
                return false;
        }
    }

    private bool Set(byte control, byte percent) => (ControlId)control switch
    {
        ControlId.Volume => speakers.SetLevel(percent),
        ControlId.MicLevel => microphone.SetLevel(percent),
        ControlId.Brightness => Do(() => brightness.Set(percent)),
        _ => false,
    };

    private static bool Do(Action action)
    {
        action();
        return true;
    }
}
