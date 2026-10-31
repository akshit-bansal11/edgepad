using Edgepad.Annotation;
using Edgepad.Controls;
using Edgepad.Gamepad;
using Edgepad.Injection;
using Edgepad.Protocol;
using Pen = Edgepad.Protocol.Pen; // System.Drawing.Pen is in scope too, through the WinForms implicit usings

namespace Edgepad.Dispatch;

/// <summary>
/// Windows' half of the <see cref="Dispatcher"/>: what each action, control and input frame does to this
/// desktop. The dispatcher has already checked every id and bound, so this table only decides what Windows
/// can do with one, and answers false when it cannot.
/// </summary>
internal sealed class WindowsPlatform(
    InputInjector input,
    VirtualPad pad,
    AudioEndpoint speakers,
    AudioEndpoint microphone,
    BrightnessControl brightness,
    MediaSessions media,
    DisplayModes display,
    LevelOverlay overlay,
    Annotator annotator) : IPlatform
{
    private const int BrightnessStep = 10;

    private readonly CaptureOverlay capture = new(() => Environment.TickCount64);

    public void Move(int dx, int dy) => input.Move(dx, dy);

    public void Button(byte id, bool down) => input.Button(id, down);

    public void Scroll(int dx, int dy) => input.Scroll(dx, dy);

    public void Zoom(int delta) => input.Zoom(delta);

    public void Type(string text) => input.Type(text);

    public void Key(ushort code, bool down) => input.Key(code, down);

    public bool Pad(PadState state) => pad.Update(state);

    public bool Pen(Pen pen) => annotator.Handle(pen);

    public bool PenRgb(int rgb) => annotator.SetRgb(rgb);

    public string AttachPad() => pad.Attach();

    public string DetachPad() => pad.Detach();

    public bool Run(ActionId id)
    {
        // The default arm drops what this laptop cannot do; the dispatcher has already rejected ids it does not know.
        switch (id)
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
            case ActionId.AnnotateBegin:
            case ActionId.AnnotateEnd:
            case ActionId.AnnotateUndo:
            case ActionId.AnnotateClear:
            case ActionId.ToolPen:
            case ActionId.ToolHighlighter:
            case ActionId.ToolLine:
            case ActionId.ToolArrow:
            case ActionId.ToolRectangle:
            case ActionId.ToolEllipse:
            case ActionId.SignBegin:
            case ActionId.SignSave:
                // The annotator decides which of these the current mode allows; a tool while signing is dropped.
                return annotator.Run(id);
            case ActionId.CaptureRegion:
            case ActionId.CaptureScreen:
            case ActionId.CaptureRecord:
                // Region and Record leave a selection overlay open until a drag or Escape. PrtScn is left out: it
                // opens the Snipping Tool overlay only where that setting is on, and a guess here would send an
                // Escape into the person's app on every other machine.
                if (capture.Begin(id != ActionId.CaptureScreen))
                {
                    Dismiss();
                }

                input.Chord(CaptureChord(id));
                return true;
            case ActionId.CaptureCancel:
                // A recording that has started is not stopped: Escape does not stop it on Windows, and a second
                // Win+Shift+R would risk starting another. Only a selection still waiting is dismissed.
                if (capture.Cancel())
                {
                    input.Chord(Keys.Escape);
                }

                return true;
            default:
                return false;
        }
    }

    private void Dismiss()
    {
        input.Chord(Keys.Escape);
        Thread.Sleep(CaptureOverlay.DismissWaitMs);
    }

    /// <summary>
    /// The keys each capture action presses, apart from the injector so a test can read the table without
    /// pressing anything into the desktop that runs it.
    /// </summary>
    internal static Keys[] CaptureChord(ActionId id) => id switch
    {
        // Windows' own Snipping Tool, which is why there is no capture code of ours to maintain.
        ActionId.CaptureRegion => [Keys.LWin, Keys.ShiftKey, Keys.S],
        ActionId.CaptureScreen => [Keys.PrintScreen],
        // Needs a Snipping Tool new enough to record; on an older one the chord does nothing.
        ActionId.CaptureRecord => [Keys.LWin, Keys.ShiftKey, Keys.R],
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Not a capture action."),
    };

    public bool Set(ControlId control, byte percent) => control switch
    {
        ControlId.Volume => Show(LevelKind.Volume, percent, speakers.SetLevel(percent)),
        ControlId.MicLevel => Show(LevelKind.Microphone, percent, microphone.SetLevel(percent)),
        ControlId.Brightness => Show(LevelKind.Brightness, percent, Do(() => brightness.Set(percent))),
        ControlId.MediaPosition => media.Seek(percent),
        // The value is an index into the rate list, never hertz: the dispatcher's 0-100 guard is what makes that
        // safe, and it is also why a rate this display cannot do is unrepresentable rather than merely
        // refused. Queued rather than applied here, because a switch blanks the panel for about a second.
        ControlId.RefreshRate => RefreshRate(percent),
        // A palette index: the dispatcher's guard lets 8..100 through, and the palette refuses them.
        ControlId.PenColor => annotator.SetColour(percent),
        // Width and opacity as the phone sent them; the board refuses what is out of range, 0 for the width included.
        ControlId.PenWidth => annotator.SetWidth(percent),
        ControlId.PenOpacity => annotator.SetOpacity(percent),
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
        LevelKind kind,
        int percent,
        bool changed)
    {
        if (changed)
        {
            overlay.Show(kind, percent);
        }

        return changed;
    }

    private static bool Do(Action action)
    {
        action();
        return true;
    }
}
