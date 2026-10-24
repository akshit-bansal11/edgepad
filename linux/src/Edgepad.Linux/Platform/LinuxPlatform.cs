using Edgepad.Dispatch;
using Edgepad.Input;
using Edgepad.Macros;
using Edgepad.Protocol;

namespace Edgepad.Platform;

/// <summary>
/// Linux's half of the <see cref="Dispatcher"/>, one per session: what each action, control and input frame does to
/// this desktop. Keys, pointer and typing go to the uinput device (<see cref="LinuxInput"/>), the gamepad to a second
/// one plugged in when the phone asks (<see cref="LinuxPad"/>), levels and media to the sound server, logind and
/// MPRIS, and macros to the shared <see cref="MacroStore"/> with <see cref="LinuxLauncher"/>.
///
/// Everything slow (starting wpctl, a D-Bus call, loginctl) is posted to <see cref="Worker"/> and answers true at
/// once, so the session's reader never waits on it; the cost is that a frame whose work then fails is not counted as
/// dropped. Only Pen is still a stand-in, refused until annotate lands (step 8.7), as are the annotate and sign
/// actions. Disposing releases whatever the phone still holds down, unplugs the pad and stops the threads.
/// </summary>
internal sealed class LinuxPlatform : IPlatform, IMacroRunner, IDisposable
{
    private const int BrightnessStep = 10;

    // Windows virtual-key codes, for the keys that are the same on every desktop.
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
    private readonly MacroStore macros = new(Path.Combine(Xdg.ConfigFolder, "macros.txt"), new LinuxLauncher());
    private readonly Worker worker;
    private readonly Lock reporting = new();
    private StateReporter? reporter;

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
            case ActionId.CaptureRegion:
            case ActionId.CaptureScreen:
            case ActionId.CaptureRecord:
                // A desktop with no known chord for it drops the action: the dispatcher counts a false.
                return Desktop.Chord(desktop, id) is { } chord && Do(() => input.Chord(chord));
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
            default:
                // Annotate and sign (step 8.7); the pad actions never get here, the dispatcher answers them.
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
            default:
                // Refresh rate (no shared way to switch modes) and pen colour (no annotate yet).
                return false;
        }
    }

    public bool Pad(PadState state) => pad.Update(state);

    public bool Pen(Pen pen) => false;

    public string AttachPad() => pad.Attach();

    public string DetachPad() => pad.Detach();

    /// <summary>The PAD_STATUS token sent once after the handshake, before the phone can ask.</summary>
    public string ProbePad() => pad.Probe();

    public bool Run(int index) => macros.Run(index);

    /// <summary>
    /// Starts reporting the laptop's levels, media, macro names and features through <paramref name="send"/>, for the
    /// rest of the session. Called once, on the session's reader, after the handshake.
    /// </summary>
    public void StartReporting(Action<Frame> send)
    {
        lock (reporting)
        {
            reporter ??= new StateReporter(speakers, microphone, backlight, mpris, macros, send);
        }
    }

    public void Dispose()
    {
        // Input first: letting go of keys and unplugging the pad is what matters if a join below runs long.
        input.Dispose();
        pad.Dispose();
        lock (reporting)
        {
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
