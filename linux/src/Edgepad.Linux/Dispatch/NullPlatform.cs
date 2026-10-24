using Edgepad.Macros;
using Edgepad.Protocol;

namespace Edgepad.Dispatch;

/// <summary>
/// STAND-IN, replaced by steps 8.2-8.4 (uinput input, the virtual pad, the action table and Core's MacroStore):
/// a platform that does nothing. Every action, control, pad frame, pen frame and macro answers false, so the
/// dispatcher drops and counts it; pointer, key and typing frames have no answer to give and are ignored. The
/// pad is reported as having no driver, so the phone shows that rather than a pad that does nothing.
/// It holds nothing down, so disposing it at the end of a session releases nothing; the real one will.
/// </summary>
internal sealed class NullPlatform : IPlatform, IMacroRunner, IDisposable
{
    public void Move(int dx, int dy)
    {
    }

    public void Button(byte id, bool down)
    {
    }

    public void Scroll(int dx, int dy)
    {
    }

    public void Zoom(int delta)
    {
    }

    public void Type(string text)
    {
    }

    public void Key(ushort code, bool down)
    {
    }

    public bool Run(ActionId id) => false;

    public bool Set(ControlId control, byte percent) => false;

    public bool Pad(PadState state) => false;

    public bool Pen(Pen pen) => false;

    public string AttachPad() => PadStatus.NoDriver;

    public string DetachPad() => PadStatus.NoDriver;

    public bool Run(int index) => false;

    public void Dispose()
    {
    }
}
