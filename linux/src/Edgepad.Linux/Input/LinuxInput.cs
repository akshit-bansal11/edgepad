using System.Runtime.InteropServices;

namespace Edgepad.Input;

/// <summary>
/// The remainder a high-resolution wheel leaves behind for the legacy one. Wheel units are the protocol's, 120 to a
/// notch, which is also what REL_WHEEL_HI_RES counts in (linux/input-event-codes.h); REL_WHEEL counts whole notches
/// and is what X11 clients and older libinput read. A swipe arrives as many small amounts, so each notch is sent
/// once the amounts add up to it, rather than every small one rounding down to nothing.
/// </summary>
internal struct Wheel
{
    public const int Notch = 120;

    private int remainder;

    /// <summary>Adds <paramref name="units"/> and answers the whole notches now due, signed like them.</summary>
    public int Add(int units)
    {
        // C#'s division and remainder both truncate toward zero, so a change of direction first uses up what the
        // other direction left over, and the remainder never reaches a notch.
        var total = remainder + units;
        remainder = total % Notch;
        return total / Notch;
    }
}

/// <summary>
/// The phone's keyboard and pointer as one virtual uinput device, holding a session's keys and buttons, so a dropped
/// connection can never leave one stuck: <see cref="Dispose"/> releases everything and destroys the device. One per
/// session, made when the session is. When /dev/uinput cannot be used (no udev rule, no module) every call is a
/// no-op that is counted in <see cref="Refused"/> and never throws, since it runs inside the dispatcher on the
/// session thread. Locked like the Windows injector is single-threaded: every call holds the one lock, so a session
/// ending under a frame never writes to a destroyed device.
/// </summary>
internal sealed class LinuxInput : IDisposable
{
    private const string DeviceName = "Edgepad";

    // linux/input-event-codes.h.
    private const ushort RelX = 0x00;
    private const ushort RelY = 0x01;
    private const ushort RelHorizontalWheel = 0x06;
    private const ushort RelWheel = 0x08;
    private const ushort RelWheelHiRes = 0x0B;
    private const ushort RelHorizontalWheelHiRes = 0x0C;

    /// <summary>BTN_LEFT; BTN_RIGHT and BTN_MIDDLE follow it, in the protocol's own button order.</summary>
    private const ushort ButtonLeft = 0x110;

    private static readonly ushort[] RelativeAxes = [RelX, RelY, RelHorizontalWheel, RelWheel, RelWheelHiRes, RelHorizontalWheelHiRes];

    /// <summary>Read the first time text is typed, then kept for the run.</summary>
    private static readonly Lazy<bool> UsLayout = new(Typing.DetectUsLayout);

    private static readonly ClipboardTool? Clipboard =
        Typing.ClipboardFor(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"), Environment.GetEnvironmentVariable("DISPLAY"));

    private static int deviceFailureLogged;
    private static int pasteFailureLogged;

    private readonly Lock gate = new();

    /// <summary>Keys and buttons held down, by evdev code; two Windows codes for one key (Shift, LShift) are one entry.</summary>
    private readonly HashSet<ushort> held = [];

    private Uinput? device;
    private Wheel vertical;
    private Wheel horizontal;
    private Wheel zoom;

    public LinuxInput() => device = CreateDevice();

    /// <summary>
    /// Input that did nothing: no device, a key code this laptop has no key for, a write the kernel refused, or
    /// text that could not be pasted. Logged when the session ends, as Windows logs its refused batches.
    /// </summary>
    public int Refused { get; private set; }

    public void Move(int dx, int dy)
    {
        lock (gate)
        {
            Send([Event(Uinput.EvRel, RelX, dx), Event(Uinput.EvRel, RelY, dy), Sync]);
        }
    }

    /// <summary>A mouse button, 0 left, 1 right, 2 middle; the dispatcher has already rejected any other id.</summary>
    public void Button(byte id, bool down)
    {
        lock (gate)
        {
            Press((ushort)(ButtonLeft + id), down);
        }
    }

    /// <summary>Both axes in one report; positive dy is the wheel forward, positive dx is right, as on evdev.</summary>
    public void Scroll(int dx, int dy)
    {
        lock (gate)
        {
            Span<InputEvent> events = stackalloc InputEvent[5];
            var count = WheelEvents(ref vertical, RelWheelHiRes, RelWheel, dy, events);
            count += WheelEvents(ref horizontal, RelHorizontalWheelHiRes, RelHorizontalWheel, dx, events[count..]);
            if (count > 0)
            {
                events[count++] = Sync;
                Send(events[..count]);
            }
        }
    }

    /// <summary>Pinch zoom is Ctrl+wheel, as on Windows, which is what a touchpad's pinch means in most apps.</summary>
    public void Zoom(int delta)
    {
        lock (gate)
        {
            Span<InputEvent> events = stackalloc InputEvent[7];
            events[0] = Event(Uinput.EvKey, KeyMap.LeftCtrl, 1);
            events[1] = Sync;
            var count = 2 + WheelEvents(ref zoom, RelWheelHiRes, RelWheel, delta, events[2..]);
            events[count++] = Sync;
            events[count++] = Event(Uinput.EvKey, KeyMap.LeftCtrl, 0);
            events[count++] = Sync;
            Send(events[..count]);
        }
    }

    /// <summary>
    /// Presses or releases one key by the Windows virtual-key code the phone named, remembering it while it is down:
    /// a thumb holding a gamepad stick forward is a key held across many frames.
    /// </summary>
    public void Key(ushort code, bool down)
    {
        lock (gate)
        {
            var key = KeyMap.ToEvdev(code);
            if (key == 0)
            {
                Refused++;
                return;
            }

            Press(key, down);
        }
    }

    /// <summary>Types text as the phone's keyboard produced it: keys where the layout allows, the clipboard elsewhere.</summary>
    public void Type(string text)
    {
        lock (gate)
        {
            foreach (var step in Typing.Plan(text, UsLayout.Value))
            {
                if (step.Paste is { } paste)
                {
                    Paste(paste);
                }
                else if (step.Shift)
                {
                    Chord(KeyMap.LeftShift, step.Key);
                }
                else
                {
                    Chord(step.Key);
                }
            }
        }
    }

    /// <summary>Presses evdev keys in order and releases them in reverse, as one write so nothing interleaves.</summary>
    public void Chord(params ReadOnlySpan<ushort> keys)
    {
        lock (gate)
        {
            // A report after every key, so each one reaches the keyboard state on its own, modifiers first.
            Span<InputEvent> events = stackalloc InputEvent[keys.Length * 4];
            for (var i = 0; i < keys.Length; i++)
            {
                events[i * 2] = Event(Uinput.EvKey, keys[i], 1);
                events[(i * 2) + 1] = Sync;
                events[^((i * 2) + 2)] = Event(Uinput.EvKey, keys[i], 0);
                events[^((i * 2) + 1)] = Sync;
            }

            Send(events);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (device is null)
            {
                return;
            }

            // Destroying the device would release them too; said here so it does not rest on that.
            foreach (var code in held)
            {
                Send([Event(Uinput.EvKey, code, 0), Sync]);
            }

            held.Clear();
            device.Dispose();
            device = null;
            if (Refused > 0)
            {
                Log.Write($"{Refused} input frames did nothing this session (no /dev/uinput, an unknown key or a failed paste)");
            }
        }
    }

    /// <summary>
    /// The high-resolution amount and any legacy notches it completes, into <paramref name="into"/>; the count
    /// written. An axis at zero writes nothing.
    /// </summary>
    internal static int WheelEvents(ref Wheel wheel, ushort hiRes, ushort legacy, int units, Span<InputEvent> into)
    {
        if (units == 0)
        {
            return 0;
        }

        into[0] = Event(Uinput.EvRel, hiRes, units);
        var notches = wheel.Add(units);
        if (notches == 0)
        {
            return 1;
        }

        into[1] = Event(Uinput.EvRel, legacy, notches);
        return 2;
    }

    private static InputEvent Sync => new(Uinput.EvSyn, Uinput.SynReport, 0);

    private static InputEvent Event(ushort type, ushort code, int value) => new(type, code, value);

    private static Uinput? CreateDevice()
    {
        var device = Uinput.Open(out var error);
        if (device is null)
        {
            LogOnce(ref deviceFailureLogged, $"{error}. Keys and pointer will do nothing; see docs/LINUX.md for the udev rule");
            return null;
        }

        var ok = device.Enable(Uinput.SetEvBit, Uinput.EvKey) && device.Enable(Uinput.SetEvBit, Uinput.EvRel);
        foreach (var key in KeyMap.AllKeys)
        {
            ok = ok && device.Enable(Uinput.SetKeyBit, key);
        }

        for (var button = ButtonLeft; button <= ButtonLeft + 2; button++)
        {
            ok = ok && device.Enable(Uinput.SetKeyBit, button);
        }

        foreach (var axis in RelativeAxes)
        {
            ok = ok && device.Enable(Uinput.SetRelBit, axis);
        }

        ok = ok && device.Create(new InputId(Uinput.BusVirtual, 0, 0, 1), DeviceName);
        if (!ok)
        {
            // Read before Dispose, whose own calls would overwrite it.
            var reason = Marshal.GetLastPInvokeSystemErrorMessage();
            device.Dispose();
            LogOnce(ref deviceFailureLogged, $"The virtual keyboard could not be created: {reason}. Keys and pointer will do nothing");
            return null;
        }

        return device;
    }

    private static void LogOnce(ref int logged, string message)
    {
        if (Interlocked.Exchange(ref logged, 1) == 0)
        {
            Log.Write(message);
        }
    }

    private void Press(ushort code, bool down)
    {
        if (down)
        {
            held.Add(code);
        }
        else
        {
            held.Remove(code);
        }

        Send([Event(Uinput.EvKey, code, down ? 1 : 0), Sync]);
    }

    /// <summary>
    /// Puts the text on the clipboard and sends Ctrl+V. Ctrl+V, not Ctrl+Shift+V: it is paste in nearly every
    /// graphical app, where Ctrl+Shift+V means something else in several (paste without formatting, or nothing);
    /// a terminal is the exception, and gets a literal ^V. The user's clipboard is replaced by the text.
    /// </summary>
    private void Paste(string text)
    {
        if (Clipboard is null)
        {
            Refused++;
            LogOnce(ref pasteFailureLogged, "Text that cannot be typed as keys is pasted, but neither WAYLAND_DISPLAY nor DISPLAY is set");
            return;
        }

        if (Typing.Copy(Clipboard, text) is { } failure)
        {
            Refused++;
            LogOnce(ref pasteFailureLogged, $"Text could not be pasted: {failure}. Install wl-clipboard (Wayland) or xclip (X11)");
            return;
        }

        Chord(KeyMap.LeftCtrl, KeyMap.V);
    }

    /// <summary>
    /// Copies <paramref name="events"/> into <paramref name="into"/> less every KEY_SYSRQ press made while an Alt
    /// is down, and answers how many were kept. Alt+SysRq+key is the kernel's magic SysRq (reboot, sync, kill
    /// all), which the kernel honours from any keyboard, this virtual one too, and even on a locked screen; a phone
    /// must never reach it. The kernel tracks Alt per device, so this device's own Alt is the one that counts:
    /// <paramref name="leftAlt"/> and <paramref name="rightAlt"/> say which were already held before these events.
    /// </summary>
    internal static int WithoutMagicSysRq(ReadOnlySpan<InputEvent> events, bool leftAlt, bool rightAlt, Span<InputEvent> into)
    {
        var count = 0;
        foreach (var e in events)
        {
            if (e.Type == Uinput.EvKey)
            {
                if (e.Code == KeyMap.LeftAlt)
                {
                    leftAlt = e.Value != 0;
                }
                else if (e.Code == KeyMap.RightAlt)
                {
                    rightAlt = e.Value != 0;
                }
                else if (e.Code == KeyMap.SysRq && e.Value != 0 && (leftAlt || rightAlt))
                {
                    continue;
                }
            }

            into[count++] = e;
        }

        return count;
    }

    // Every write goes through here, single keys and chords alike, so the SysRq rule has one place to hold.
    private void Send(ReadOnlySpan<InputEvent> events)
    {
        Span<InputEvent> kept = stackalloc InputEvent[events.Length];
        var count = WithoutMagicSysRq(events, held.Contains(KeyMap.LeftAlt), held.Contains(KeyMap.RightAlt), kept);
        Refused += events.Length - count;
        if (device is null || !device.Write(kept[..count]))
        {
            Refused++;
        }
    }
}
