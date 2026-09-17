using Edgepad.Controls;
using Edgepad.Dispatch;
using Edgepad.Injection;
using Edgepad.Macros;
using Edgepad.Protocol;
using NAudio.CoreAudioApi;
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

    private readonly Dispatcher dispatcher;

    public DispatcherTests() =>
        dispatcher = new Dispatcher(input, speakers, microphone, brightness, media, display, macros, overlay);

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
    [InlineData(80)]
    [InlineData(95)]
    public void AMacroSlotWithNothingInItIsDropped(byte id)
    {
        // The block is 64..95 whether or not the laptop has filled it. An empty slot must be refused the
        // same way an unknown id is, rather than counting as handled and silently doing nothing.
        dispatcher.Handle(new RunAction(id));
        Assert.Equal(1, dispatcher.Dropped);
    }

    [Theory]
    [InlineData(63)]
    [InlineData(96)]
    public void AnIdEitherSideOfTheMacroBlockIsNotAMacro(byte id)
    {
        // 63 and 96 bound the reserved block. Neither is in the action table either, so both drop — the
        // point is that the block's arithmetic does not reach past its own edges into the table's ids.
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
        input.Dispose();
        speakers.Dispose();
        microphone.Dispose();
        brightness.Dispose();
        media.Dispose();
        display.Dispose();
        overlay.Dispose();
    }
}
