using Edgepad.Dispatch;
using Edgepad.Input;
using Edgepad.Macros;
using Edgepad.Protocol;

namespace Edgepad.Platform;

/// <summary>
/// Linux's half of the <see cref="Dispatcher"/>, one per session. Keys, pointer and typing go to the uinput device
/// (<see cref="LinuxInput"/>), and the gamepad to a second one plugged in when the phone asks (<see cref="LinuxPad"/>).
/// STAND-IN for the rest until step 8.4 (the action table and Core's MacroStore): every action, control, pen frame
/// and macro answers false, so the dispatcher drops and counts it. Disposing it at the end of a session releases
/// whatever the phone still holds down and unplugs the pad.
/// </summary>
internal sealed class LinuxPlatform : IPlatform, IMacroRunner, IDisposable
{
    private readonly LinuxInput input = new();
    private readonly LinuxPad pad = new();

    public void Move(int dx, int dy) => input.Move(dx, dy);

    public void Button(byte id, bool down) => input.Button(id, down);

    public void Scroll(int dx, int dy) => input.Scroll(dx, dy);

    public void Zoom(int delta) => input.Zoom(delta);

    public void Type(string text) => input.Type(text);

    public void Key(ushort code, bool down) => input.Key(code, down);

    public bool Run(ActionId id) => false;

    public bool Set(ControlId control, byte percent) => false;

    public bool Pad(PadState state) => pad.Update(state);

    public bool Pen(Pen pen) => false;

    public string AttachPad() => pad.Attach();

    public string DetachPad() => pad.Detach();

    /// <summary>The PAD_STATUS token sent once after the handshake, before the phone can ask.</summary>
    public string ProbePad() => pad.Probe();

    public bool Run(int index) => false;

    public void Dispose()
    {
        input.Dispose();
        pad.Dispose();
    }
}
