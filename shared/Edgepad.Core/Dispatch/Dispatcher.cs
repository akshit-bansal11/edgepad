using System.Diagnostics;
using System.Runtime.InteropServices;
using Edgepad.Blob;
using Edgepad.Capture;
using Edgepad.Macros;
using Edgepad.Protocol;

namespace Edgepad.Dispatch;

/// <summary>
/// Turns frames from the trusted phone into input and control changes, on the session thread. Every id is
/// checked against the protocol's own enums: an id this laptop does not know is dropped and counted,
/// never guessed at. The phone can name an action; only this table decides what it does.
///
/// What is the same on every laptop lives here: the bounds, the macro block, the drop count. What an action
/// does to the desktop is the <see cref="IPlatform"/>'s, so each OS supplies only its own table.
/// </summary>
internal sealed class Dispatcher(IPlatform platform, IMacroRunner macros)
{
    private const byte MaxButton = 2;
    private const byte MaxPercent = 100;

    /// <summary>Frames that asked for something unknown, out of range, or unavailable (no microphone).</summary>
    public int Dropped { get; private set; }

    /// <summary>Frames whose handler threw, all of them also counted in <see cref="Dropped"/>.</summary>
    public int Failed { get; private set; }

    /// <summary>
    /// Where the answer to PAD_ATTACH and PAD_DETACH goes. Set by the session once it has a socket to write
    /// to: this class deliberately holds no reference to the connection, for the same reason PONG is
    /// answered in the session and not here. The pad is the one thing in this table the phone has to hear
    /// back about, because it draws a different screen depending on the token.
    /// </summary>
    public Action<string>? PadStatusReply { get; set; }

    /// <summary>
    /// Sends captures to the phone when it asks for that. Set by the session, which owns the link, like
    /// <see cref="PadStatusReply"/>; without one, CAPTURE_TARGET and FILE_RESULT are dropped and counted.
    /// </summary>
    public CaptureToPhone? Capture { get; set; }

    /// <summary>
    /// Told when the phone's macro grid opens (true) and closes (false). Set by the session on a laptop that can see
    /// windows, like <see cref="Capture"/>; without one, MACROS_WATCH and MACROS_UNWATCH are dropped and counted.
    /// </summary>
    public Action<bool>? MacroWatch { get; set; }

    /// <summary>
    /// Shares the clipboard when the phone asks for that. Set by the session, like <see cref="Capture"/>; without one,
    /// SET CLIPBOARD and the blob frames are dropped and counted.
    /// </summary>
    public ClipboardSync? Clipboard { get; set; }

    /// <summary>
    /// Offers this laptop's apps to the phone as macros. Set by the session, like <see cref="Capture"/>; without one,
    /// APPS_LIST, APPS_CANCEL and APP_ADD are dropped and counted.
    /// </summary>
    public AppPicker? Apps { get; set; }

    /// <summary>Where the answer to APP_ADD goes, one <see cref="AppAddStatus"/> token. Set by the session, like <see cref="PadStatusReply"/>.</summary>
    public Action<string>? AppAddReply { get; set; }

    /// <summary>When the phone last sent input, by <see cref="Environment.TickCount64"/>: a capture on its way slows down for it.</summary>
    public long LastInputMs => Interlocked.Read(ref lastInput);

    private long lastInput;

    /// <summary>How evenly this session's Move frames arrived; the session logs it when it ends.</summary>
    public PointerCadence Cadence { get; } = new();

    /// <summary>
    /// Carries out one frame. A handler that throws is dropped and counted like an unknown id, because this
    /// runs on the session thread, where an escape ends the tray app and releases nothing the phone was
    /// holding down. Only what the session reads as a dead link passes through: the pad's reply writes to the
    /// socket, and a write that failed must still end the session rather than be counted and ignored.
    /// </summary>
    public void Handle(Frame frame)
    {
        if (frame is Move or PointerButton or Scroll or Zoom or Key or PadState or Pen or Text)
        {
            Interlocked.Exchange(ref lastInput, Environment.TickCount64);
        }

        if (frame is Move)
        {
            // Stopwatch, never TickCount64: its resolution is about 15 ms on Windows, coarser than the gaps measured.
            Cadence.Record(Stopwatch.GetTimestamp());
        }

        bool handled;
        try
        {
            handled = Carry(frame);
        }
        catch (Exception e) when (e is not (IOException or ObjectDisposedException or COMException or OperationCanceledException))
        {
            // Logged once per connection: the same fault on every frame of a drag would write the log from
            // the input path dozens of times a second. The rest show in the session's closing count.
            if (Failed++ == 0)
            {
                Log.Write($"A {frame.GetType().Name} frame failed and was dropped: {e}");
            }

            handled = false;
        }

        if (!handled)
        {
            Dropped++;
        }
    }

    private bool Carry(Frame frame) => frame switch
    {
        Move m => Do(() => platform.Move(m.Dx, m.Dy)),
        PointerButton b when b.Id <= MaxButton => Do(() => platform.Button(b.Id, b.Down)),
        Scroll s => Do(() => platform.Scroll(s.Dx, s.Dy)),
        Zoom z => Do(() => platform.Zoom(z.Delta)),
        RunAction a => Run(a.Id),
        SetValue v when v.Value <= MaxPercent => Set(v.Control, v.Value),
        Text t when t.Kind == (byte)TextKind.Type => Do(() => platform.Type(t.Value)),
        // The one phone string that can end in a macro, and it is only ever a token: see AppPicker.
        Text t when t.Kind == (byte)TextKind.AppAdd => AddApp(t.Value),
        Key k => Do(() => platform.Key(k.Code, k.Down)),
        // A pad frame with no pad plugged in is dropped and counted like any other id this laptop
        // cannot act on. The phone is not meant to send one before its PAD_ATTACH was answered
        // "ready", and a laptop that quietly accepted them would look to it exactly like one playing.
        PadState p => platform.Pad(p),
        // An absolute point on the laptop screen, which only the annotator reads; outside a session it is dropped.
        Pen p => platform.Pen(p),
        // The phone's answer to a capture this laptop sent it. Only the transfer waiting for one takes it.
        FileResult r => Capture?.Take(r) ?? false,
        BlobBegin b => Clipboard?.Take(b) ?? false,
        BlobChunk c => Clipboard?.Take(c) ?? false,
        BlobEnd e => Clipboard?.Take(e) ?? false,
        _ => false,
    };

    private bool Run(byte id)
    {
        // Macros are a block of ids, not one each, so they are checked before the table. The phone names a
        // slot and nothing else: what that slot opens was put there on this laptop, by its own editor or from
        // its own app list, which is the whole reason a phone can be trusted with the button at all.
        var slot = id - (byte)ActionId.MacroBase;
        if (slot >= 0 && slot < IMacroRunner.MaxMacros)
        {
            return macros.Run(slot);
        }

        // The close block on the same terms: a slot, and the laptop decides what that slot is.
        var closing = id - (byte)ActionId.MacroCloseBase;
        if (closing >= 0 && closing < IMacroRunner.MaxMacros)
        {
            return macros.Close(closing);
        }

        // An id the enum does not name is rejected here, so no platform is ever handed one; an id it names but
        // a laptop cannot do falls to that platform's own table, which drops it.
        var action = (ActionId)id;
        if (!Enum.IsDefined(action))
        {
            return false;
        }

        return action switch
        {
            ActionId.PadAttach => Answer(platform.AttachPad()),
            ActionId.PadDetach => Answer(platform.DetachPad()),
            // The capture lists its folders before the shortcut is pressed, so the press can be told apart from them.
            ActionId.CaptureRegion or ActionId.CaptureScreen or ActionId.CaptureRecord when Capture is { } capture =>
                capture.Run(action, () => platform.Run(action)),
            ActionId.CaptureCancel => Cancel(),
            ActionId.MacrosWatch => Watch(true),
            ActionId.MacrosUnwatch => Watch(false),
            ActionId.AppsList => Apps?.List() ?? false,
            ActionId.AppsCancel => Apps?.Cancel() ?? false,
            _ => platform.Run(action),
        };
    }

    private bool AddApp(string token)
    {
        if (Apps is not { } apps)
        {
            return false;
        }

        AppAddReply?.Invoke(apps.Add(token));
        return true;
    }

    private bool Watch(bool open)
    {
        if (MacroWatch is not { } watch)
        {
            return false;
        }

        watch(open);
        return true;
    }

    private bool Set(byte control, byte percent) => (ControlId)control switch
    {
        ControlId.CaptureTarget => Capture?.SetTarget(percent) ?? false,
        ControlId.Clipboard => Clipboard?.SetShared(percent) ?? false,
        var id => Enum.IsDefined(id) && platform.Set(id, percent),
    };

    /// <summary>The overlay's Escape is the platform's; the watch it may cut short is the capture's.</summary>
    private bool Cancel()
    {
        var handled = platform.Run(ActionId.CaptureCancel);
        Capture?.Cancel();
        return handled;
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
