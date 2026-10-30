using Edgepad.Dispatch;
using Edgepad.Macros;
using Edgepad.Protocol;
using Xunit;
using static System.FormattableString;

namespace Edgepad.Tests;

/// <summary>
/// The routing every laptop shares, against a platform that only writes down what it was asked to do. Nothing
/// here reaches a desktop, so it runs the same on Windows, Linux and a build machine; what a call does to the
/// desktop is each platform's own table, tested where that platform lives.
/// </summary>
public sealed class DispatcherTests
{
    private readonly RecordingPlatform platform = new();
    private readonly RecordingMacros macros = new();
    private readonly Dispatcher dispatcher;

    public DispatcherTests() => dispatcher = new Dispatcher(platform, macros);

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(120)]
    [InlineData(255)]
    public void AnUnknownActionIsDropped(byte id)
    {
        // Rejected before any platform sees it, so no table has to know what this build's enum does not name.
        dispatcher.Handle(new RunAction(id));
        Assert.Equal(1, dispatcher.Dropped);
        Assert.Empty(platform.Calls);
    }

    [Theory]
    [InlineData(64, 0)]
    [InlineData(70, 6)]
    [InlineData(78, 14)]
    public void AMacroIdReachesTheStoreAsASlotIndexOnly(byte id, int slot)
    {
        macros.Answer = true;

        dispatcher.Handle(new RunAction(id));

        Assert.Equal(0, dispatcher.Dropped);
        Assert.Equal(slot, Assert.Single(macros.Slots));
        Assert.Empty(platform.Calls);
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
        macros.Answer = true;

        dispatcher.Handle(new RunAction(id));

        Assert.Equal(1, dispatcher.Dropped);
        Assert.Empty(macros.Slots);
        Assert.Empty(platform.Calls);
    }

    [Theory]
    [InlineData(96, 0)]
    [InlineData(102, 6)]
    [InlineData(110, 14)]
    public void ACloseIdReachesTheStoreAsASlotIndexOnly(byte id, int slot)
    {
        macros.Answer = true;

        dispatcher.Handle(new RunAction(id));

        Assert.Equal(0, dispatcher.Dropped);
        Assert.Equal(slot, Assert.Single(macros.Closed));
        Assert.Empty(macros.Slots);
        Assert.Empty(platform.Calls);
    }

    [Theory]
    [InlineData(111)]
    [InlineData(127)]
    public void ACloseIdPastTheGridIsDropped(byte id)
    {
        macros.Answer = true;

        dispatcher.Handle(new RunAction(id));

        Assert.Equal(1, dispatcher.Dropped);
        Assert.Empty(macros.Closed);
        Assert.Empty(platform.Calls);
    }

    [Fact]
    public void ACloseTheStoreRefusesIsDropped()
    {
        dispatcher.Handle(new RunAction((byte)ActionId.MacroCloseBase));

        Assert.Equal(1, dispatcher.Dropped);
    }

    [Fact]
    public void TheMacroGridOpeningAndClosingReachTheSession()
    {
        List<bool> seen = [];
        dispatcher.MacroWatch = seen.Add;

        dispatcher.Handle(new RunAction((byte)ActionId.MacrosWatch));
        dispatcher.Handle(new RunAction((byte)ActionId.MacrosUnwatch));

        Assert.Equal(0, dispatcher.Dropped);
        Assert.Equal([true, false], seen);
        Assert.Empty(platform.Calls);
    }

    [Fact]
    public void WithNoWatcherTheMacroGridIdsAreDropped()
    {
        // A laptop that cannot see windows answers the way an older one would.
        dispatcher.Handle(new RunAction((byte)ActionId.MacrosWatch));
        dispatcher.Handle(new RunAction((byte)ActionId.MacrosUnwatch));

        Assert.Equal(2, dispatcher.Dropped);
        Assert.Empty(platform.Calls);
    }

    [Theory]
    [InlineData((byte)ActionId.CaptureRegion, "Run CaptureRegion")]
    [InlineData((byte)ActionId.CaptureScreen, "Run CaptureScreen")]
    [InlineData((byte)ActionId.CaptureRecord, "Run CaptureRecord")]
    [InlineData((byte)ActionId.CaptureCancel, "Run CaptureCancel")]
    public void ACaptureActionReachesThePlatformAsItself(byte id, string expected)
    {
        dispatcher.Handle(new RunAction(id));

        Assert.Equal(0, dispatcher.Dropped);
        Assert.Equal(expected, Assert.Single(platform.Calls));
    }

    [Fact]
    public void AnActionThePlatformCannotDoIsDropped()
    {
        // The app switcher stepped without being opened, a stroke edit with no session: the platform says no.
        platform.Answer = false;

        dispatcher.Handle(new RunAction((byte)ActionId.AppSwitchNext));
        dispatcher.Handle(new RunAction((byte)ActionId.AnnotateUndo));

        Assert.Equal(2, dispatcher.Dropped);
        Assert.Equal(["Run AppSwitchNext", "Run AnnotateUndo"], platform.Calls);
    }

    [Fact]
    public void APinFrameAfterTheHandshakeIsDropped()
    {
        // The PIN is only ever asked between trust and HELLO_ACK; once a phone is in, a PIN frame means nothing.
        dispatcher.Handle(new PinAnswer(new Pin("123456")));
        dispatcher.Handle(new PinRequest(5, 0));
        Assert.Equal(2, dispatcher.Dropped);
        Assert.Empty(platform.Calls);
    }

    [Fact]
    public void APenPointGoesToThePlatformAndARefusedOneIsDropped()
    {
        // Whether a point is taken depends on the annotate session, which is the platform's.
        dispatcher.Handle(new Pen(1, 2, 1));
        platform.Answer = false;
        dispatcher.Handle(new Pen(3, 4, 7));

        Assert.Equal(1, dispatcher.Dropped);
        Assert.Equal(["Pen 1 2 1", "Pen 3 4 7"], platform.Calls);
    }

    [Fact]
    public void APenColourReachesThePlatformAsAControl()
    {
        // 8 passes the 0..100 guard every SET goes through; the palette behind the platform is what refuses it.
        dispatcher.Handle(new SetValue((byte)ControlId.PenColor, 6));
        platform.Answer = false;
        dispatcher.Handle(new SetValue((byte)ControlId.PenColor, 8));

        Assert.Equal(1, dispatcher.Dropped);
        Assert.Equal(["Set PenColor 6", "Set PenColor 8"], platform.Calls);
    }

    [Fact]
    public void AnUnknownControlIsDropped()
    {
        dispatcher.Handle(new SetValue(9, 50));
        Assert.Equal(1, dispatcher.Dropped);
        Assert.Empty(platform.Calls);
    }

    [Fact]
    public void AValueAboveOneHundredIsDropped()
    {
        dispatcher.Handle(new SetValue((byte)ControlId.Volume, 101));
        Assert.Equal(1, dispatcher.Dropped);
        Assert.Empty(platform.Calls);
    }

    [Fact]
    public void AnUnknownMouseButtonIsDropped()
    {
        dispatcher.Handle(new PointerButton(3, Down: true));
        Assert.Equal(1, dispatcher.Dropped);
        Assert.Empty(platform.Calls);
    }

    [Fact]
    public void InputFramesReachThePlatformAsTheyCame()
    {
        dispatcher.Handle(new Move(-3, 4));
        dispatcher.Handle(new PointerButton(2, Down: true));
        dispatcher.Handle(new Scroll(5, -6));
        dispatcher.Handle(new Zoom(-120));
        dispatcher.Handle(new Text((byte)TextKind.Type, "hi"));
        dispatcher.Handle(new Key(0x41, Down: false));

        Assert.Equal(0, dispatcher.Dropped);
        Assert.Equal(["Move -3 4", "Button 2 True", "Scroll 5 -6", "Zoom -120", "Type hi", "Key 65 False"], platform.Calls);
    }

    [Fact]
    public void FramesOnlyTheLaptopSendsAreDropped()
    {
        dispatcher.Handle(new Pong(1));
        dispatcher.Handle(new StateReport(0, 0, 0));
        dispatcher.Handle(new HelloAck(1));
        dispatcher.Handle(new Text(0, "x"));
        Assert.Equal(4, dispatcher.Dropped);
        Assert.Empty(platform.Calls);
    }

    [Fact]
    public void AControllerFrameWithNoControllerPluggedInIsDropped()
    {
        // The A button and a stick pushed right: a frame that would be acted on if a pad were attached.
        platform.Answer = false;
        dispatcher.Handle(new PadState((ushort)PadButton.A, 0, 0, 32767, 0, 0, 0));
        Assert.Equal(1, dispatcher.Dropped);
        Assert.Equal("Pad", Assert.Single(platform.Calls));
    }

    [Theory]
    [InlineData((byte)ActionId.PadAttach, "AttachPad")]
    [InlineData((byte)ActionId.PadDetach, "DetachPad")]
    public void AskingForTheControllerIsAnsweredEvenWhenThereIsNoDriver(byte id, string call)
    {
        List<string> answers = [];
        dispatcher.PadStatusReply = answers.Add;

        dispatcher.Handle(new RunAction(id));

        // Answered rather than dropped: the laptop understood the ask and said what it could do about it.
        // A phone that heard nothing back could not tell this laptop from one too old to know the id.
        Assert.Equal(0, dispatcher.Dropped);
        Assert.Equal(PadStatus.NoDriver, Assert.Single(answers));
        Assert.Equal(call, Assert.Single(platform.Calls));
    }

    [Fact]
    public void AHandlerThatThrowsIsDroppedRatherThanEndingTheApp()
    {
        // Anything that is not a dead socket is counted and the session carries on, where it used to escape the
        // session thread.
        dispatcher.PadStatusReply = _ => throw new InvalidOperationException("a bug in a handler");

        dispatcher.Handle(new RunAction((byte)ActionId.PadAttach));

        Assert.Equal(1, dispatcher.Dropped);
        Assert.Equal(1, dispatcher.Failed);
    }

    [Fact]
    public void ADeadSocketStillEndsTheSession()
    {
        // The reply writes to the socket. A failed write is the session's to see, not a frame to count.
        dispatcher.PadStatusReply = _ => throw new IOException("the phone went away");

        Assert.Throws<IOException>(() => dispatcher.Handle(new RunAction((byte)ActionId.PadAttach)));
        Assert.Equal(0, dispatcher.Failed);
    }

    /// <summary>A platform that does nothing but write each call down, and answers whatever the test set.</summary>
    private sealed class RecordingPlatform : IPlatform
    {
        public List<string> Calls { get; } = [];

        public bool Answer { get; set; } = true;

        public void Move(int dx, int dy) => Calls.Add(Invariant($"Move {dx} {dy}"));

        public void Button(byte id, bool down) => Calls.Add(Invariant($"Button {id} {down}"));

        public void Scroll(int dx, int dy) => Calls.Add(Invariant($"Scroll {dx} {dy}"));

        public void Zoom(int delta) => Calls.Add(Invariant($"Zoom {delta}"));

        public void Type(string text) => Calls.Add($"Type {text}");

        public void Key(ushort code, bool down) => Calls.Add(Invariant($"Key {code} {down}"));

        public bool Run(ActionId id) => Record($"Run {id}");

        public bool Set(ControlId control, byte percent) => Record(Invariant($"Set {control} {percent}"));

        public bool Pad(PadState state) => Record("Pad");

        public bool Pen(Pen pen) => Record(Invariant($"Pen {pen.X} {pen.Y} {pen.State}"));

        public string AttachPad()
        {
            Calls.Add("AttachPad");
            return PadStatus.NoDriver;
        }

        public string DetachPad()
        {
            Calls.Add("DetachPad");
            return PadStatus.NoDriver;
        }

        private bool Record(string call)
        {
            Calls.Add(call);
            return Answer;
        }
    }

    /// <summary>A macro list that runs nothing: it notes the slot it was handed and answers what the test set.</summary>
    private sealed class RecordingMacros : IMacroRunner
    {
        public List<int> Slots { get; } = [];

        public List<int> Closed { get; } = [];

        public bool Answer { get; set; }

        public bool Run(int index)
        {
            Slots.Add(index);
            return Answer;
        }

        public bool Close(int index)
        {
            Closed.Add(index);
            return Answer;
        }
    }
}
