using Edgepad.Native;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// Pins XClientMessageEvent's LP64 layout from X11/Xlib.h. A wrong offset here sends a message the window manager
/// reads as something else, or ignores, and nothing on the machine would say so.
/// </summary>
public sealed class X11MessageTests
{
    private const nuint Window = 0x1234567;
    private const nuint Atom = 0x2A5;
    private const int EventBytes = 192; // long pad[24]
    private const int ClientMessage = 33;
    private const int FormatOf32 = 32;
    private const long Pager = 2;
    private const long IconicState = 3;

    private const int TypeAt = 0;
    private const int SerialAt = 8;
    private const int SendEventAt = 16;
    private const int DisplayAt = 24;
    private const int WindowAt = 32;
    private const int MessageTypeAt = 40;
    private const int FormatAt = 48;
    private const int DataAt = 56;

    private static long Data(byte[] message, int index) => BitConverter.ToInt64(message, DataAt + (index * sizeof(long)));

    private static void AssertHeader(byte[] message)
    {
        Assert.Equal(EventBytes, message.Length);
        Assert.Equal(ClientMessage, BitConverter.ToInt32(message, TypeAt));
        Assert.Equal(0L, BitConverter.ToInt64(message, SerialAt));
        Assert.Equal(0, BitConverter.ToInt32(message, SendEventAt));
        Assert.Equal(0L, BitConverter.ToInt64(message, DisplayAt));
        Assert.Equal((ulong)Window, BitConverter.ToUInt64(message, WindowAt));
        Assert.Equal((ulong)Atom, BitConverter.ToUInt64(message, MessageTypeAt));
        Assert.Equal(FormatOf32, BitConverter.ToInt32(message, FormatAt));
    }

    [Fact]
    public void RaiseNamesThePagerFirst()
    {
        var message = X11Windows.RaiseMessage(Window, Atom);

        AssertHeader(message);
        Assert.Equal(Pager, Data(message, 0));
        Assert.Equal(0L, Data(message, 1));
    }

    [Fact]
    public void MinimizeAsksForTheIconicState()
    {
        var message = X11Windows.MinimizeMessage(Window, Atom);

        AssertHeader(message);
        Assert.Equal(IconicState, Data(message, 0));
        Assert.Equal(0L, Data(message, 1));
    }

    [Fact]
    public void CloseNamesThePagerSecond()
    {
        var message = X11Windows.CloseMessage(Window, Atom);

        AssertHeader(message);
        Assert.Equal(0L, Data(message, 0));
        Assert.Equal(Pager, Data(message, 1));
    }

    [Fact]
    public void TheRestOfTheEventStaysZero()
    {
        var message = X11Windows.RaiseMessage(Window, Atom);

        Assert.All(message[(DataAt + (2 * sizeof(long)))..], value => Assert.Equal(0, value));
    }
}
