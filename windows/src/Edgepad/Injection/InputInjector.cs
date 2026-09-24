using System.Runtime.InteropServices;

namespace Edgepad.Injection;

/// <summary>
/// Sends input into the user's desktop and remembers what it is holding down, so a dropped connection
/// can never leave a key or a mouse button stuck: <see cref="Dispose"/> releases everything, whether it
/// was pressed as a typed <see cref="Keys"/> or as a raw virtual-key code the phone named.
/// Used from one session thread only.
/// </summary>
internal sealed class InputInjector : IDisposable
{
    private static readonly int InputSize = Marshal.SizeOf<NativeInput>();

    private readonly HashSet<Keys> heldKeys = [];
    private readonly HashSet<byte> heldButtons = [];

    /// <summary>
    /// Keys the phone is holding by raw virtual-key code, which is every key the on-screen keyboard and
    /// the gamepad press. Kept apart from <see cref="heldKeys"/> because that one is keyed by the typed
    /// <see cref="Keys"/> this app presses for itself; a key can therefore sit in both, and the extra
    /// release <see cref="Dispose"/> then sends is a key-up on an already-up key, which does nothing.
    /// </summary>
    private readonly HashSet<ushort> heldCodes = [];

    /// <summary>
    /// Batches Windows refused. SendInput is silently blocked when an elevated window has focus (UIPI),
    /// so this is the only trace of it; the session logs it once when it ends.
    /// </summary>
    public int RefusedBatches { get; private set; }

    public void Move(int dx, int dy) => Send([InputBuilder.Mouse(NativeMethods.MouseMove, dx, dy)]);

    public void Button(byte id, bool down)
    {
        var (downFlag, upFlag) = InputBuilder.ButtonFlags(id);
        if (down)
        {
            heldButtons.Add(id);
        }
        else
        {
            heldButtons.Remove(id);
        }

        Send([InputBuilder.Mouse(down ? downFlag : upFlag)]);
    }

    public void Scroll(int dx, int dy) => Send(InputBuilder.Scroll(dx, dy));

    public void Zoom(int delta) => Send(InputBuilder.Zoom(delta));

    public void Chord(params ReadOnlySpan<Keys> keys) => Send(InputBuilder.Chord(keys));

    /// <summary>Types text as the phone's keyboard produced it; backspace and newline become their keys.</summary>
    public void Type(string text)
    {
        foreach (var c in text)
        {
            switch (c)
            {
                case '\b':
                    Chord(Keys.Back);
                    break;
                case '\n':
                    Chord(Keys.Return);
                    break;
                default:
                    Send(InputBuilder.Unicode(c));
                    break;
            }
        }
    }

    /// <summary>
    /// Presses or releases one key by raw Windows virtual-key code, as the phone named it, and remembers
    /// it while it is down. Remembering matters most here: a thumb holding a gamepad stick forward is a
    /// key held across many frames, and a link that drops in that moment would otherwise leave it pressed
    /// with nothing left to lift it.
    /// </summary>
    public void Key(ushort code, bool down)
    {
        if (down)
        {
            heldCodes.Add(code);
        }
        else
        {
            heldCodes.Remove(code);
        }

        Send([InputBuilder.VirtualKey(code, up: !down)]);
    }

    public bool IsHeld(Keys key) => heldKeys.Contains(key);

    public void Hold(Keys key)
    {
        if (heldKeys.Add(key))
        {
            Send([InputBuilder.Key(key, up: false)]);
        }
    }

    public void Release(Keys key)
    {
        if (heldKeys.Remove(key))
        {
            Send([InputBuilder.Key(key, up: true)]);
        }
    }

    public void Dispose()
    {
        foreach (var key in heldKeys)
        {
            Send([InputBuilder.Key(key, up: true)]);
        }

        foreach (var code in heldCodes)
        {
            Send([InputBuilder.VirtualKey(code, up: true)]);
        }

        foreach (var id in heldButtons)
        {
            Send([InputBuilder.Mouse(InputBuilder.ButtonFlags(id).Up)]);
        }

        heldKeys.Clear();
        heldCodes.Clear();
        heldButtons.Clear();
    }

    private void Send(NativeInput[] batch)
    {
        if (batch.Length > 0 && NativeMethods.SendInput((uint)batch.Length, batch, InputSize) != batch.Length)
        {
            RefusedBatches++;
        }
    }
}
