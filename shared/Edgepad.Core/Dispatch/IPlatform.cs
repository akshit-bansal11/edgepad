using Edgepad.Protocol;

namespace Edgepad.Dispatch;

/// <summary>
/// What one laptop's OS does with a frame the <see cref="Dispatcher"/> has already checked. Every id and value
/// handed here is in range and named by the protocol's enums, so an implementation decides only what its own
/// desktop can do: a <c>bool</c> answer of false means it cannot, and the frame is dropped and counted.
/// Called on the session thread, one frame at a time.
/// </summary>
internal interface IPlatform
{
    void Move(int dx, int dy);

    /// <summary>A mouse button, 0..2.</summary>
    void Button(byte id, bool down);

    void Scroll(int dx, int dy);

    void Zoom(int delta);

    void Type(string text);

    /// <summary>A raw key the phone named by its Windows virtual-key code, the protocol's one key namespace.</summary>
    void Key(ushort code, bool down);

    /// <summary>One action from the table, never a macro slot and never PAD_ATTACH or PAD_DETACH.</summary>
    bool Run(ActionId id);

    /// <summary>A control set to a value of 0..100: a percentage, or an index for the controls that take one.</summary>
    bool Set(ControlId control, byte percent);

    /// <summary>One controller frame. False when no pad is plugged in.</summary>
    bool Pad(PadState state);

    /// <summary>One PEN frame for the annotate layer. False outside an annotate or sign session.</summary>
    bool Pen(Pen pen);

    /// <summary>
    /// The stroke colour as 0xRRGGBB, from a TEXT PEN_RGB the dispatcher has already held to six hex digits. False when
    /// this laptop has no annotate layer.
    /// </summary>
    bool PenRgb(int rgb);

    /// <summary>Plugs in the virtual controller; the answer is the <see cref="PadStatus"/> token to send back.</summary>
    string AttachPad();

    /// <summary>Unplugs the virtual controller; the answer is the <see cref="PadStatus"/> token to send back.</summary>
    string DetachPad();
}
