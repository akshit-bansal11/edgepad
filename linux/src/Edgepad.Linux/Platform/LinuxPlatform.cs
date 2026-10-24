using Edgepad.Dispatch;
using Edgepad.Input;
using Edgepad.Macros;
using Edgepad.Protocol;

namespace Edgepad.Platform;

/// <summary>
/// Linux's half of the <see cref="Dispatcher"/>, one per session. Keys, pointer and typing go to the uinput device
/// (<see cref="LinuxInput"/>). STAND-IN for the rest until steps 8.3 and 8.4 (the virtual pad, the action table and
/// Core's MacroStore): every action, control, pad frame, pen frame and macro answers false, so the dispatcher drops
/// and counts it, and the pad is reported as having no driver, so the phone shows that rather than a pad that does
/// nothing. Disposing it at the end of a session releases whatever the phone still holds down.
/// </summary>
internal sealed class LinuxPlatform : IPlatform, IMacroRunner, IDisposable
{
    private readonly LinuxInput input = new();

    public void Move(int dx, int dy) => input.Move(dx, dy);

    public void Button(byte id, bool down) => input.Button(id, down);

    public void Scroll(int dx, int dy) => input.Scroll(dx, dy);

    public void Zoom(int delta) => input.Zoom(delta);

    public void Type(string text) => input.Type(text);

    public void Key(ushort code, bool down) => input.Key(code, down);

    public bool Run(ActionId id) => false;

    public bool Set(ControlId control, byte percent) => false;

    public bool Pad(PadState state) => false;

    public bool Pen(Pen pen) => false;

    public string AttachPad() => PadStatus.NoDriver;

    public string DetachPad() => PadStatus.NoDriver;

    public bool Run(int index) => false;

    public void Dispose() => input.Dispose();
}
