using Edgepad.Annotation;
using Edgepad.Controls;
using Edgepad.Dispatch;
using Edgepad.Gamepad;
using Edgepad.Injection;
using Edgepad.Macros;
using Edgepad.Protocol;
using NAudio.CoreAudioApi;
using Nefarius.ViGEm.Client.Exceptions;
using Xunit;
using Pen = Edgepad.Protocol.Pen; // System.Drawing.Pen is in scope too, through the WinForms implicit usings

namespace Edgepad.Tests;

/// <summary>
/// The paths that must never reach the desktop, through the shared dispatcher and Windows' own table. Every frame
/// here is dropped before any input is sent or any device is touched, so these run safely on a build machine.
/// The routing itself is tested once, for every laptop, in the core's DispatcherTests.
/// </summary>
public sealed class WindowsPlatformTests : IDisposable
{
    private readonly InputInjector input = new();
    private readonly AudioEndpoint speakers = new(DataFlow.Render);
    private readonly AudioEndpoint microphone = new(DataFlow.Capture);
    private readonly BrightnessControl brightness = new();
    private readonly MediaSessions media = new();
    private readonly DisplayModes display = new();
    private readonly LevelOverlay overlay = new();

    // A store pointed at a file that does not exist, so it holds no macros: every macro id is therefore
    // out of range and must be dropped. Nothing is written, nothing run.
    private readonly MacroStore macros =
        new(Path.Combine(Path.GetTempPath(), $"edgepad-dispatcher-{Guid.NewGuid():N}.txt"), new ShellLauncher());

    // A pad whose driver is missing whatever the machine running these tests actually has installed: the
    // client it would open throws what an absent ViGEmBus throws. Nothing here can plug a controller into
    // whoever runs the suite, and the answers are the same on a build machine as on a desk with the driver.
    private readonly VirtualPad pad = new(() => throw new VigemBusNotFoundException());

    // The annotator's UI thread is a context that only counts what is posted to it and runs none of it, so no
    // overlay can ever be built on whoever runs the suite, and a test can see how many repaints were asked for.
    private readonly HeldPosts ui = new();
    private readonly Annotator annotator;

    private readonly Dispatcher dispatcher;

    public WindowsPlatformTests()
    {
        annotator = new Annotator(ui);
        var platform = new WindowsPlatform(input, pad, speakers, microphone, brightness, media, display, overlay, annotator);
        dispatcher = new Dispatcher(platform, macros);
    }

    [Theory]
    [InlineData(64)]
    [InlineData(70)]
    [InlineData(78)]
    public void AMacroSlotWithNothingInItIsDropped(byte id)
    {
        // The real store, empty: a slot it does not have is refused, not counted as handled.
        dispatcher.Handle(new RunAction(id));
        Assert.Equal(1, dispatcher.Dropped);
    }

    [Fact]
    public void EditingStrokesWithNoSessionOpenIsDropped()
    {
        // Undo, Clear, the six tools and Save all need an annotate or sign session to act on. Counting them as
        // handled would tell the phone something is drawn when nothing is.
        for (byte id = (byte)ActionId.AnnotateUndo; id <= (byte)ActionId.ToolEllipse; id++)
        {
            dispatcher.Handle(new RunAction(id));
        }

        dispatcher.Handle(new RunAction((byte)ActionId.SignSave));
        Assert.Equal(9, dispatcher.Dropped);
        Assert.Equal(0, ui.Count);
    }

    [Fact]
    public void APenPointWithNoSessionOpenIsDropped()
    {
        dispatcher.Handle(new Pen(0, 0, 1));
        dispatcher.Handle(new Pen(1, 2, 2));
        dispatcher.Handle(new Pen(1, 2, 0));
        Assert.Equal(3, dispatcher.Dropped);
    }

    [Fact]
    public void AnUnknownPenStateIsDroppedEvenWhileDrawing()
    {
        // Signing, because it shows nothing on this laptop. State 7 is a codec-level success and a dispatcher-level
        // drop: a newer phone may add states, and this one must not guess what they mean.
        dispatcher.Handle(new RunAction((byte)ActionId.SignBegin));
        dispatcher.Handle(new Pen(1, 2, 7));
        dispatcher.Handle(new Pen(1, 2, 1));
        dispatcher.Handle(new Pen(3, 4, 2));
        dispatcher.Handle(new Pen(3, 4, 0));
        Assert.Equal(1, dispatcher.Dropped);
    }

    [Fact]
    public void OnlyThePenToolIsTakenWhileSigning()
    {
        dispatcher.Handle(new RunAction((byte)ActionId.SignBegin));
        for (byte id = (byte)ActionId.ToolPen; id <= (byte)ActionId.ToolEllipse; id++)
        {
            dispatcher.Handle(new RunAction(id));
        }

        Assert.Equal(5, dispatcher.Dropped);
    }

    [Fact]
    public void APenColourOutsideThePaletteIsDropped()
    {
        // 8 passes the 0..100 guard every SET goes through; the palette is what refuses it.
        dispatcher.Handle(new SetValue((byte)ControlId.PenColor, 6));
        dispatcher.Handle(new SetValue((byte)ControlId.PenColor, 8));
        Assert.Equal(1, dispatcher.Dropped);
    }

    [Fact]
    public void APenWidthOrOpacityOutOfRangeIsDropped()
    {
        dispatcher.Handle(new SetValue((byte)ControlId.PenWidth, 1));
        dispatcher.Handle(new SetValue((byte)ControlId.PenWidth, 100));
        dispatcher.Handle(new SetValue((byte)ControlId.PenOpacity, 10));
        dispatcher.Handle(new SetValue((byte)ControlId.PenOpacity, 100));
        Assert.Equal(0, dispatcher.Dropped);

        dispatcher.Handle(new SetValue((byte)ControlId.PenWidth, 0));
        dispatcher.Handle(new SetValue((byte)ControlId.PenWidth, 101));
        dispatcher.Handle(new SetValue((byte)ControlId.PenOpacity, 9));
        dispatcher.Handle(new SetValue((byte)ControlId.PenOpacity, 0));
        Assert.Equal(4, dispatcher.Dropped);
    }

    [Fact]
    public void OnlySixHexDigitsSetThePenRgb()
    {
        dispatcher.Handle(new Text((byte)TextKind.PenRgb, "12abEF"));
        Assert.Equal(0, dispatcher.Dropped);

        dispatcher.Handle(new Text((byte)TextKind.PenRgb, "12345"));
        dispatcher.Handle(new Text((byte)TextKind.PenRgb, "#123456"));
        dispatcher.Handle(new Text((byte)TextKind.PenRgb, "GGGGGG"));
        Assert.Equal(3, dispatcher.Dropped);
    }

    [Fact]
    public void SavingASignatureWithNoInkAnswersEmptyAndExportsNothing()
    {
        List<string> answers = [];
        annotator.SignStatusReply = answers.Add;

        dispatcher.Handle(new RunAction((byte)ActionId.SignBegin));
        var posted = ui.Count;
        dispatcher.Handle(new RunAction((byte)ActionId.SignSave));

        // Answered at once, and nothing handed to the UI thread: the export, the file and the clipboard all run
        // there, so no post means none of them ran.
        Assert.Equal(0, dispatcher.Dropped);
        Assert.Equal(SignStatus.Empty, Assert.Single(answers));
        Assert.Equal(posted, ui.Count);
    }

    [Fact]
    public void AStormOfPenPointsAsksForOneRepaint()
    {
        // The posts are never run here, so the first is still in flight for every point after it, exactly as when
        // the UI thread falls behind a finger. Each point must not queue a repaint of its own.
        dispatcher.Handle(new RunAction((byte)ActionId.AnnotateBegin));
        dispatcher.Handle(new Pen(0, 0, 1));
        for (var i = 1; i < 1000; i++)
        {
            dispatcher.Handle(new Pen((ushort)i, (ushort)i, 2));
        }

        Assert.Equal(0, dispatcher.Dropped);
        Assert.Equal(1, ui.Count);
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

        // The real pad, with no driver behind it, says so rather than failing.
        Assert.Equal(0, dispatcher.Dropped);
        Assert.Equal(PadStatus.NoDriver, Assert.Single(answers));
    }

    [Theory]
    [InlineData((byte)ActionId.CaptureRegion, new[] { Keys.LWin, Keys.ShiftKey, Keys.S })]
    [InlineData((byte)ActionId.CaptureScreen, new[] { Keys.PrintScreen })]
    [InlineData((byte)ActionId.CaptureRecord, new[] { Keys.LWin, Keys.ShiftKey, Keys.R })]
    public void ACaptureActionPressesTheSystemsOwnShortcut(byte id, Keys[] chord)
    {
        // Read from the table, never pressed: injecting these would open Snipping Tool on whoever runs the suite.
        Assert.Equal(chord, WindowsPlatform.CaptureChord((ActionId)id));
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
        annotator.Dispose();
    }

    /// <summary>A UI thread that never runs anything, so a test can count posts without a window appearing.</summary>
    private sealed class HeldPosts : SynchronizationContext
    {
        public int Count { get; private set; }

        public override void Post(SendOrPostCallback d, object? state) => Count++;
    }
}
