using Edgepad.Controls;
using Edgepad.Dispatch;
using Edgepad.Gamepad;
using Edgepad.Injection;
using Edgepad.Macros;
using Edgepad.Protocol;
using NAudio.CoreAudioApi;
using Nefarius.ViGEm.Client.Exceptions;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// The paths that must never reach the desktop. Every frame here is dropped before any input is sent or
/// any device is touched, so these run safely on a build machine.
/// </summary>
public sealed class DispatcherTests : IDisposable
{
    private readonly InputInjector input = new();
    private readonly AudioEndpoint speakers = new(DataFlow.Render);
    private readonly AudioEndpoint microphone = new(DataFlow.Capture);
    private readonly BrightnessControl brightness = new();
    private readonly MediaSessions media = new();
    private readonly DisplayModes display = new();
    private readonly LevelOverlay overlay = new();

    // A store pointed at a file that does not exist, so it holds no macros: every macro id is therefore
    // out of range and must be dropped, which is what these tests check. Nothing is written, nothing run.
    private readonly MacroStore macros =
        new(Path.Combine(Path.GetTempPath(), $"edgepad-dispatcher-{Guid.NewGuid():N}.txt"));

    // A pad whose driver is missing whatever the machine running these tests actually has installed: the
    // client it would open throws what an absent ViGEmBus throws. Nothing here can plug a controller into
    // whoever runs the suite, and the answers are the same on a build machine as on a desk with the driver.
    private readonly VirtualPad pad = new(() => throw new VigemBusNotFoundException());

    private readonly Dispatcher dispatcher;

    public DispatcherTests() =>
        dispatcher = new Dispatcher(input, pad, speakers, microphone, brightness, media, display, macros, overlay);

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(255)]
    public void AnUnknownActionIsDropped(byte id)
    {
        dispatcher.Handle(new RunAction(id));
        Assert.Equal(1, dispatcher.Dropped);
    }

    [Theory]
    [InlineData(64)]
    [InlineData(70)]
    [InlineData(78)]
    public void AMacroSlotWithNothingInItIsDropped(byte id)
    {
        // The block is 64..95 whether or not the laptop has filled it. An empty slot must be refused the
        // same way an unknown id is, rather than counting as handled and silently doing nothing.
        dispatcher.Handle(new RunAction(id));
        Assert.Equal(1, dispatcher.Dropped);
    }

    [Theory]
    [InlineData(63)]
    [InlineData(79)]
    public void AnIdEitherSideOfTheMacroBlockIsNotAMacro(byte id)
    {
        // 63 is below the block and 79 is the first slot past the grid's fifteen. Neither is in the action
        // table either, so both drop — the point is that the arithmetic stops at the grid, not at the block.
        dispatcher.Handle(new RunAction(id));
        Assert.Equal(1, dispatcher.Dropped);
    }

    [Fact]
    public void AnUnknownControlIsDropped()
    {
        dispatcher.Handle(new SetValue(9, 50));
        Assert.Equal(1, dispatcher.Dropped);
    }

    [Fact]
    public void AValueAboveOneHundredIsDropped()
    {
        dispatcher.Handle(new SetValue((byte)ControlId.Volume, 101));
        Assert.Equal(1, dispatcher.Dropped);
    }

    [Fact]
    public void AnUnknownMouseButtonIsDropped()
    {
        dispatcher.Handle(new PointerButton(3, Down: true));
        Assert.Equal(1, dispatcher.Dropped);
    }

    [Theory]
    [InlineData((byte)ActionId.AppSwitchNext)]
    [InlineData((byte)ActionId.AppSwitchPrevious)]
    public void SteppingTheAppSwitcherWithoutOpeningItIsDropped(byte id)
    {
        // Otherwise it would type a bare Tab into whatever window has focus.
        dispatcher.Handle(new RunAction(id));
        Assert.Equal(1, dispatcher.Dropped);
    }

    [Fact]
    public void FramesOnlyTheLaptopSendsAreDropped()
    {
        dispatcher.Handle(new Pong(1));
        dispatcher.Handle(new StateReport(0, 0, 0));
        dispatcher.Handle(new HelloAck(1));
        dispatcher.Handle(new Text(0, "x"));
        Assert.Equal(4, dispatcher.Dropped);
    }

    [Fact]
    public void SeekingWithNoPlayerIsDropped()
    {
        dispatcher.Handle(new SetValue((byte)ControlId.MediaPosition, 50));
        Assert.Equal(1, dispatcher.Dropped);
    }

    [Fact]
    public void AControllerFrameWithNoControllerPluggedInIsDropped()
    {
        // The A button and a stick pushed right: a frame that would be acted on if a pad were attached.
        dispatcher.Handle(new PadState((ushort)PadButton.A, 0, 0, 32767, 0, 0, 0));
        Assert.Equal(1, dispatcher.Dropped);
    }

    [Theory]
    [InlineData((byte)ActionId.PadAttach)]
    [InlineData((byte)ActionId.PadDetach)]
    public void AskingForTheControllerIsAnsweredEvenWhenThereIsNoDriver(byte id)
    {
        List<string> answers = [];
        dispatcher.PadStatusReply = answers.Add;

        dispatcher.Handle(new RunAction(id));

        // Answered rather than dropped: the laptop understood the ask and said what it could do about it.
        // A phone that heard nothing back could not tell this laptop from one too old to know the id.
        Assert.Equal(0, dispatcher.Dropped);
        Assert.Equal(PadStatus.NoDriver, Assert.Single(answers));
    }

    [Theory]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("chrome.exe", "Chrome")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", "Media Player")]
    [InlineData("SomePlayer", "SomePlayer")]
    [InlineData("", "")]
    public void PlayerNamesReadLikeTheirTaskbarLabels(string appUserModelId, string expected) =>
        Assert.Equal(expected, MediaSessions.AppName(appUserModelId));

    public void Dispose()
    {
        pad.Dispose();
        input.Dispose();
        speakers.Dispose();
        microphone.Dispose();
        brightness.Dispose();
        media.Dispose();
        display.Dispose();
        overlay.Dispose();
    }
}
