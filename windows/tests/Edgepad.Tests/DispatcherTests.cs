using Edgepad.Controls;
using Edgepad.Dispatch;
using Edgepad.Injection;
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
    private readonly Dispatcher dispatcher;

    public DispatcherTests() => dispatcher = new Dispatcher(input, speakers, microphone, brightness);

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
        Assert.Equal(3, dispatcher.Dropped);
    }

    public void Dispose()
    {
        input.Dispose();
        speakers.Dispose();
        microphone.Dispose();
        brightness.Dispose();
    }
}
