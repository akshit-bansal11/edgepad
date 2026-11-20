using Edgepad.Gamepad;
using Edgepad.Protocol;
using Nefarius.ViGEm.Client.Exceptions;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// What a laptop without ViGEmBus does, which is the state most laptops are in: the driver is a separate
/// install. Nothing here opens the real client — every pad is handed one that throws what an absent driver
/// throws — so these prove the same thing on a build machine as on a desk that does have it, and neither
/// machine ends up with a controller plugged into it. The paths that need the driver (a pad that plugs in,
/// and the attach-failed answer) have no test: a fake client cannot produce a real Xbox360 target.
/// </summary>
public sealed class VirtualPadTests
{
    [Fact]
    public void AMissingDriverIsAnAnswerAndNeverAnException()
    {
        using var pad = NoDriver();

        // The four things a session asks of it, none of which may throw: an escape from any of them would
        // take the session thread, and with it the link and everything the phone was holding down.
        Assert.Equal(PadStatus.NoDriver, pad.Probe());
        Assert.Equal(PadStatus.NoDriver, pad.Attach());
        Assert.Equal(PadStatus.NoDriver, pad.Detach());
        Assert.False(pad.Update(new PadState((ushort)PadButton.A, 255, 0, -32768, 0, 0, 0)));
    }

    [Fact]
    public void AButtonBitOutsideTheDefinedSetIsDroppedAndTheRestKept()
    {
        const ushort Everything = 0xFFFF;
        Assert.Equal((ushort)0xF7FF, VirtualPad.DefinedButtons(Everything));
        Assert.Equal((ushort)0, VirtualPad.DefinedButtons(0x0800));
        Assert.Equal((ushort)((ushort)PadButton.A | (ushort)PadButton.Guide), VirtualPad.DefinedButtons((ushort)((ushort)PadButton.A | (ushort)PadButton.Guide | 0x0800)));
    }

    [Fact]
    public void TheDriverIsLookedForAgainOnEachAttachOnly()
    {
        // An attach is the gamepad screen opening, so a driver installed while connected is found then; the
        // probe and a detach do not look again.
        var looks = 0;
        using var pad = new VirtualPad(() =>
        {
            looks++;
            throw new VigemBusNotFoundException();
        });

        pad.Probe();
        pad.Attach();
        pad.Attach();
        pad.Detach();

        Assert.Equal(3, looks);
    }

    [Fact]
    public void DisposingWithNothingPluggedInIsHarmless()
    {
        // The session disposes the pad in the same finally that releases held keys, whether or not the
        // phone ever asked for a controller. That path runs on every disconnect, so it must not throw.
        var pad = NoDriver();
        pad.Attach();

        pad.Dispose();
        pad.Dispose();

        Assert.Equal(PadStatus.NoDriver, pad.Probe());
    }

    private static VirtualPad NoDriver() => new(() => throw new VigemBusNotFoundException());
}
