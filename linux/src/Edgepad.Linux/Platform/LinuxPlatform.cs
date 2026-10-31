using Edgepad.Dispatch;
using Edgepad.Input;
using Edgepad.Macros;
using Edgepad.Protocol;
using Edgepad.Ui;

namespace Edgepad.Platform;

/// <summary>
/// Linux's half of the <see cref="Dispatcher"/>, one per session: what each action, control and input frame does to
/// this desktop. Keys, pointer and typing go to the uinput device (<see cref="LinuxInput"/>), the gamepad to a second
/// one plugged in when the phone asks (<see cref="LinuxPad"/>), levels and media to the sound server, logind and
/// MPRIS, and macros to the shared <see cref="MacroStore"/> with <see cref="LinuxLauncher"/>.
///
/// Everything slow (starting wpctl, a D-Bus call, loginctl) is posted to <see cref="Worker"/> and answers true at
/// once, so the session's reader never waits on it; the cost is that a frame whose work then fails is not counted as
/// dropped. Pen, the annotate and sign actions and the pen colour go to the session's <see cref="Annotator"/>, best
/// effort (docs/LINUX.md). Disposing releases whatever the phone still holds down, unplugs the pad, takes the overlay
/// down and stops the threads.
/// </summary>
internal sealed class LinuxPlatform : IPlatform, IMacroRunner, IDisposable
{
    private const int BrightnessStep = 10;

    // Windows virtual-key codes, for the keys that are the same on every desktop.
    private const ushort VkEscape = 0x1B;
    private const ushort VkAlt = 0x12;
    private const ushort VkTab = 0x09;
    private const ushort VkShift = 0x10;
    private const ushort VkControl = 0x11;
    private const ushort Vk0 = 0x30;
    private const ushort VkMute = 0xAD;
    private const ushort VkVolumeDown = 0xAE;
    private const ushort VkVolumeUp = 0xAF;
    private const ushort VkNext = 0xB0;
    private const ushort VkPrevious = 0xB1;
    private const ushort VkPlayPause = 0xB3;

    private readonly LinuxInput input = new();
    private readonly LinuxPad pad = new();
    private readonly DesktopEnvironment desktop = Desktop.Detect(Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"));
    private readonly Audio speakers = new(microphone: false);
    private readonly Audio microphone = new(microphone: true);
    private readonly Backlight backlight = new();
    private readonly Mpris mpris = new();
    private readonly Annotator annotator = new();
    private readonly MacroStore macros = new(
        Path.Combine(Xdg.ConfigFolder, "macros.txt"), new LinuxLauncher(), windows: new LinuxMacroWindows());
    private readonly Worker worker;
    private readonly CaptureOverlay capture = new(() => Environment.TickCount64);
    private readonly Lock reporting = new();
    private StateReporter? reporter;
    private MacroOpenWatcher? openApps;
    private bool ended;

    // Whether the app switcher's Alt is down, so Next and Previous without a Begin are dropped, not a bare Tab. Only
    // the session's reader touches it.
    private bool switching;

    public LinuxPlatform() => worker = new Worker(Apply);

    public void Move(int dx, int dy) => input.Move(dx, dy);

    public void Button(byte id, bool down) => input.Button(id, down);

    public void Scroll(int dx, int dy) => input.Scroll(dx, dy);

    public void Zoom(int delta) => input.Zoom(delta);

    public void Type(string text) => input.Type(text);

    public void Key(ushort code, bool down) => input.Key(code, down);

    public bool Run(ActionId id)
    {
        switch (id)
        {
            case ActionId.MuteToggle:
                return Press(VkMute);
            case ActionId.VolumeUp:
                return Press(VkVolumeUp);
            case ActionId.VolumeDown:
                return Press(VkVolumeDown);
            case ActionId.PlayPause:
                return Player("PlayPause", VkPlayPause);
            case ActionId.NextTrack:
                return Player("Next", VkNext);
            case ActionId.PreviousTrack:
                return Player("Previous", VkPrevious);
            case ActionId.MicMuteToggle:
                worker.Post(() => microphone.ToggleMute());
                return true;
            case ActionId.Lock:
                // The caller's own session, with no argument. The desktop's screen locker answers the signal.
                worker.Post(() => Proc.Run("loginctl", ["lock-session"]));
                return true;
            case ActionId.TaskView:
            case ActionId.ShowDesktop:
            case ActionId.Search:
            case ActionId.Notifications:
            case ActionId.DesktopLeft:
            case ActionId.DesktopRight:
                // A desktop with no known chord for it drops the action: the dispatcher counts a false.
                return Desktop.Chord(desktop, id) is { } shortcut && Do(() => input.Chord(shortcut));
            case ActionId.CaptureRegion:
            case ActionId.CaptureScreen:
            case ActionId.CaptureRecord:
                return Capture(id);
            case ActionId.CaptureCancel:
                // Only a screenshot tool still waiting is dismissed; a recording that is running is never touched,
                // since its shortcut is a toggle and Escape does not stop it.
                if (capture.Cancel())
                {
                    worker.Post(() => Tap(VkEscape));
                }

                return true;
            case ActionId.AppSwitchBegin:
                // Alt stays down across frames, as a three-finger swipe keeps the switcher open; released by End or
                // by the session ending.
                input.Key(VkAlt, true);
                switching = true;
                input.Key(VkTab, true);
                input.Key(VkTab, false);
                return true;
            case ActionId.AppSwitchNext:
                return switching && Do(() => Tap(VkTab));
            case ActionId.AppSwitchPrevious:
                return switching && Do(() => Tap(VkShift, VkTab));
            case ActionId.AppSwitchEnd:
                input.Key(VkAlt, false);
                switching = false;
                return true;
            case ActionId.ZoomReset:
                return Do(() => Tap(VkControl, Vk0));
            case ActionId.BrightnessUp:
                return backlight.Available && Do(() => worker.Post(() => backlight.Step(BrightnessStep)));
            case ActionId.BrightnessDown:
                return backlight.Available && Do(() => worker.Post(() => backlight.Step(-BrightnessStep)));
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
            default:
                // The pad actions never get here, the dispatcher answers them.
                return false;
        }
    }

    public bool Set(ControlId control, byte percent)
    {
        switch (control)
        {
            case ControlId.Volume:
            case ControlId.MicLevel:
            case ControlId.MediaPosition:
                worker.Set(control, percent);
                return true;
            case ControlId.Brightness:
                // A desktop PC has no panel to dim.
                if (!backlight.Available)
                {
                    return false;
                }

                worker.Set(control, percent);
                return true;
            case ControlId.PenColor:
                return annotator.SetColour(percent);
            // As the phone sent them; the board refuses what is out of range, 0 for the width included.
            case ControlId.PenWidth:
                return annotator.SetWidth(percent);
            case ControlId.PenOpacity:
                return annotator.SetOpacity(percent);
            default:
                // Refresh rate: Linux has no shared way to switch modes.
                return false;
        }
    }

    public bool Pad(PadState state) => pad.Update(state);

    public bool Pen(Pen pen) => annotator.Handle(pen);

    public bool PenRgb(int rgb) => annotator.SetRgb(rgb);

    public string AttachPad() => pad.Attach();

    public string DetachPad() => pad.Detach();

    /// <summary>The PAD_STATUS token sent once after the handshake, before the phone can ask.</summary>
    public string ProbePad() => pad.Probe();

    public bool Run(int index) => macros.Run(index);

    public bool Close(int index) => macros.Close(index);

    /// <summary>The list the session answers WANT_ICONS from; the store replaces it whole on a change, so the reference is what the session compares.</summary>
    public IReadOnlyList<Macro> Macros => macros.Macros;

    /// <summary>The store itself, which the app picker adds to.</summary>
    public MacroStore Store => macros;

    /// <summary>
    /// Starts reporting the laptop's levels, media, macro names and features through <paramref name="send"/>, for the
    /// rest of the session. Called once, on the session's reader, after the handshake. Returns what turns the macro
    /// apps' watch on and off, or null where this desktop cannot be surveyed. The features name that watch before the
    /// caller has this, which is safe because the same reader must return here before it can read the phone's answer.
    /// </summary>
    public Action<bool>? StartReporting(Action<Frame> send)
    {
        // A failed survey or send stays inside the watcher, so nothing here can cost a macro its launch.
        var open = macros.CreateWatcher(states => send(new Text((byte)TextKind.MacroOpen, states)));

        // On the reader, before the reporter's thread: the features and the screen size go out first.
        annotator.Start(send, open is not null);
        lock (reporting)
        {
            reporter ??= new StateReporter(speakers, microphone, backlight, mpris, macros, send);
            openApps = open;
        }

        return open is null ? null : on => WatchMacros(open, on);
    }

    /// <summary>Under the lock Dispose takes, because a start after Dispose would leave the timer running for the life of the app.</summary>
    private void WatchMacros(MacroOpenWatcher open, bool on)
    {
        lock (reporting)
        {
            if (ended)
            {
                return;
            }

            if (on)
            {
                open.Start();
            }
            else
            {
                open.Stop();
            }
        }
    }

    public void Dispose()
    {
        // Input first: letting go of keys and unplugging the pad is what matters if a join below runs long.
        input.Dispose();
        pad.Dispose();
        annotator.Dispose();
        lock (reporting)
        {
            ended = true;
            openApps?.Dispose();
            reporter?.Dispose();
        }

        worker.Dispose();
        backlight.Dispose();
        mpris.Dispose();
    }

    /// <summary>The worker's side of <see cref="Set"/>: one control, one level, off the reader's thread.</summary>
    private void Apply(ControlId control, byte percent)
    {
        switch (control)
        {
            case ControlId.Volume:
                speakers.SetLevel(percent);
                break;
            case ControlId.MicLevel:
                microphone.SetLevel(percent);
                break;
            case ControlId.Brightness:
                backlight.Set(percent);
                break;
            case ControlId.MediaPosition:
                mpris.Seek(percent);
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Asks the playing MPRIS player, and presses the media key when there is none to ask. The key goes to whatever
    /// the desktop thinks is the active player, which is the best that can be done for a player with no MPRIS.
    /// </summary>
    private bool Player(string method, ushort fallbackKey)
    {
        worker.Post(() =>
        {
            if (!mpris.Command(method))
            {
                Tap(fallbackKey);
            }
        });
        return true;
    }

    /// <summary>
    /// A capture shortcut, after dismissing a screenshot tool that may still be open. Print opens the desktop's tool
    /// (an overlay, for Region and Screen alike); the recording shortcut is a toggle that opens none. The Escape and
    /// the pause go through the worker, so the session's reader does not wait, and the order is kept.
    /// </summary>
    private bool Capture(ActionId id)
    {
        if (Desktop.Chord(desktop, id) is not { } shortcut)
        {
            return false;
        }

        var dismiss = capture.Begin(id != ActionId.CaptureRecord);
        worker.Post(() =>
        {
            if (dismiss)
            {
                Tap(VkEscape);
                Thread.Sleep(CaptureOverlay.DismissWaitMs);
            }

            input.Chord(shortcut);
        });
        return true;
    }

    private bool Press(ushort windowsKey) => Do(() => Tap(windowsKey));

    /// <summary>Presses Windows virtual keys together, in order, and releases them in reverse.</summary>
    private void Tap(params ReadOnlySpan<ushort> windowsKeys)
    {
        Span<ushort> keys = stackalloc ushort[windowsKeys.Length];
        for (var i = 0; i < keys.Length; i++)
        {
            keys[i] = KeyMap.ToEvdev(windowsKeys[i]);
        }

        input.Chord(keys);
    }

    private static bool Do(Action action)
    {
        action();
        return true;
    }
}
