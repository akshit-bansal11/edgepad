using Edgepad.Net;
using Edgepad.Protocol;
using Edgepad.Trust;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// The handshake as a laptop runs it, over in-memory streams: what the phone says is written out beforehand, what
/// the laptop sends is read back as frames, the owner's answer and the server's word are flags, and the lockout's
/// clock is moved by hand. A phone stream that runs dry ends the read the way a closed socket does, so a handshake
/// that reads when it should not fails the test.
/// </summary>
public sealed class HandshakeTests : IDisposable
{
    private const string Phone = "(AA:BB:CC:DD:EE:FF)";

    private static readonly Hello CurrentHello = new(ProtocolConstants.Version);
    private static readonly HelloAck Ack = new(ProtocolConstants.Version);
    private static readonly PinAnswer Right = new(new Pin("123456"));
    private static readonly PinAnswer Wrong = new(new Pin("654321"));

    private readonly string dir = Path.Combine(Path.GetTempPath(), "edgepad-tests-" + Guid.NewGuid().ToString("N"));
    private readonly MemoryStream output = new();
    private readonly List<string> logs = [];
    private readonly List<string> statuses = [];
    private readonly TrustStore trust;

    // Milliseconds on the PIN store's clock, moved by hand.
    private long time;

    private bool ownerSaysYes = true;
    private bool serverTakesIt = true;

    // The session's own end: a hold or a hang-up cancels it, and run by the owner's question while it is open.
    private readonly CancellationTokenSource ended = new();
    private Action? whileAsking;
    private int asked;
    private int admitted;

    // What the phone had been sent when the server was asked to let it take over.
    private List<Frame> sentBeforeAdmitted = [];

    public HandshakeTests() => trust = new TrustStore(Path.Combine(dir, "trusted-phone.txt"));

    // One iteration, as in PinStoreTests: the real work factor would take most of a second per answer. A new store
    // is at level 1 with no PIN, as a fresh install is.
    private PinStore PinAtLevel(int level)
    {
        var store = new PinStore(dir, () => time, 1);
        if (level > 1)
        {
            Assert.True(store.SetPin("123456"));
            Assert.True(store.SetLevel(level));
        }

        return store;
    }

    private void TrustThePhone() => Assert.True(trust.Admit(Phone, () => true));

    private bool Connect(PinStore pin, params Frame[] phone)
    {
        using var said = new MemoryStream();
        foreach (var frame in phone)
        {
            said.Write(FrameCodec.Encode(frame));
        }

        return Connect(pin, said.ToArray());
    }

    // Giving up is a no-op here: these phones never go silent, they run dry, and the timers fire a minute in.
    private bool Connect(PinStore pin, byte[] said)
    {
        output.SetLength(0);
        using var input = new MemoryStream(said);
        var live = Run(new FrameStream(input, output), pin, () => { });
        Assert.Equal(input.Length, input.Position);
        return live;
    }

    private bool Run(FrameStream frames, PinStore pin, Action giveUp, TimeSpan? helloTimeout = null, TimeSpan? pinTimeout = null) =>
        Handshake.Run(
            frames,
            Phone,
            trust,
            pin,
            (address, cancel) =>
            {
                Assert.Equal(Phone, address);
                Assert.Equal(ended.Token, cancel);
                asked++;
                whileAsking?.Invoke();
                return ownerSaysYes;
            },
            () =>
            {
                admitted++;
                sentBeforeAdmitted = Sent();
                return serverTakesIt;
            },
            statuses.Add,
            logs.Add,
            giveUp,
            ended.Token,
            helloTimeout,
            pinTimeout);

    private List<Frame> Sent()
    {
        using var sent = new MemoryStream(output.ToArray());
        var reader = new FrameStream(sent, Stream.Null);
        List<Frame> frames = [];
        while (sent.Position < sent.Length)
        {
            frames.Add(reader.ReadFrame());
        }

        return frames;
    }

    [Fact]
    public void AFirstFrameThatIsNotHelloClosesWithNothingSent()
    {
        Assert.False(Connect(PinAtLevel(1), new Ping(1)));
        Assert.Empty(Sent());
        Assert.Equal(0, asked);
        Assert.Equal(0, admitted);
    }

    [Fact]
    public void AnotherVersionIsToldThisOneAndClosedBeforeTheOwnerIsAsked()
    {
        Assert.False(Connect(PinAtLevel(1), new Hello(ProtocolConstants.Version - 1)));
        Assert.Equal([Ack], Sent());
        Assert.Equal(0, asked);
        Assert.Equal(0, admitted);
    }

    [Fact]
    public void ARefusedPhoneIsClosedWithoutHelloAck()
    {
        ownerSaysYes = false;

        Assert.False(Connect(PinAtLevel(3), CurrentHello));
        Assert.Empty(Sent());
        Assert.Equal(1, asked);
        Assert.Equal(0, admitted);
    }

    [Fact]
    public void ATrustedPhoneAtLevelOneIsAdmittedWithNoPinFrames()
    {
        TrustThePhone();

        Assert.True(Connect(PinAtLevel(1), CurrentHello));
        Assert.Equal([Ack], Sent());
        Assert.Equal(0, asked);
        Assert.Equal(1, admitted);
        Assert.Empty(sentBeforeAdmitted);
    }

    [Fact]
    public void AnUnknownPhoneIsAskedAboutBeforeThePinAndAdmittedOnlyAfterIt()
    {
        Assert.True(Connect(PinAtLevel(3), CurrentHello, Right));
        Assert.Equal(1, asked);
        Assert.Equal([new PinRequest(5, 0)], sentBeforeAdmitted);
        Assert.Equal([new PinRequest(5, 0), Ack], Sent());
    }

    [Fact]
    public void LevelTwoAsksAnUnknownPhoneOnceThenRemembersIt()
    {
        var pin = PinAtLevel(2);

        Assert.True(Connect(pin, CurrentHello, Right));
        Assert.Equal([new PinRequest(5, 0), Ack], Sent());

        Assert.True(Connect(pin, CurrentHello));
        Assert.Equal([Ack], Sent());
        Assert.Equal(1, asked);
        Assert.Equal(2, admitted);
    }

    [Fact]
    public void LevelThreeAsksEveryTime()
    {
        TrustThePhone();
        var pin = PinAtLevel(3);

        Assert.True(Connect(pin, CurrentHello, Right));
        Assert.Equal([new PinRequest(5, 0), Ack], Sent());

        Assert.True(Connect(pin, CurrentHello, Right));
        Assert.Equal([new PinRequest(5, 0), Ack], Sent());
        Assert.Equal(2, admitted);
    }

    [Fact]
    public void WrongPinsCountDownThenLockOutAfterFiveWithoutEverAdmitting()
    {
        TrustThePhone();
        var pin = PinAtLevel(3);

        Assert.False(Connect(pin, CurrentHello, Wrong, Wrong, Wrong, Wrong, Wrong));
        Assert.Equal(
            [new PinRequest(5, 0), new PinRequest(4, 0), new PinRequest(3, 0), new PinRequest(2, 0), new PinRequest(1, 0), new PinRequest(0, 30)],
            Sent());
        Assert.Equal(0, admitted);
        Assert.Contains("Refused a phone that did not give the right PIN", statuses);
    }

    [Fact]
    public void ALockedOutPhoneIsToldSoAndClosedBeforeTheOwnerIsAsked()
    {
        var pin = PinAtLevel(3);
        TrustThePhone();
        Assert.False(Connect(pin, CurrentHello, Wrong, Wrong, Wrong, Wrong, Wrong));

        // Forgotten since, so a question would be asked if the lock-out did not come first.
        Assert.True(trust.Forget());
        time += 10_000;

        Assert.False(Connect(pin, CurrentHello));
        Assert.Equal([new PinRequest(0, 20)], Sent());
        Assert.Equal(0, asked);
        Assert.Equal(0, admitted);
        Assert.Contains("Refused a phone locked out after wrong PINs", statuses);
    }

    [Fact]
    public void AnotherFrameWhileWaitingForThePinCloses()
    {
        TrustThePhone();
        var pin = PinAtLevel(3);

        Assert.Throws<InvalidDataException>(() => Connect(pin, CurrentHello, new Ping(1)));
        Assert.Equal([new PinRequest(5, 0)], Sent());
        Assert.Equal(0, admitted);
        Assert.Equal(5, pin.AttemptsLeft(Phone));
    }

    [Fact]
    public void ADigitAboveNineClosesWithoutCountingAsAnAttempt()
    {
        TrustThePhone();
        var pin = PinAtLevel(3);
        byte[] said = [.. FrameCodec.Encode(CurrentHello), 0x04, 1, 2, 3, 4, 5, 10];

        // A protocol error, as docs/PROTOCOL.md puts it: it tests no PIN, so it costs the phone no try.
        Assert.Throws<InvalidDataException>(() => Connect(pin, said));
        Assert.Equal([new PinRequest(5, 0)], Sent());
        Assert.Equal(0, admitted);
        Assert.Equal(5, pin.AttemptsLeft(Phone));
    }

    [Fact]
    public void AServerThatWillNotTakeThePhoneGetsNoHelloAck()
    {
        TrustThePhone();
        serverTakesIt = false;

        Assert.False(Connect(PinAtLevel(1), CurrentHello));
        Assert.Empty(Sent());
        Assert.Equal(1, admitted);
    }

    [Fact]
    public void ASilentPhoneIsGivenUpOn()
    {
        using var closed = new ManualResetEventSlim();
        using var input = new SilentAfter([], closed);

        Assert.Throws<EndOfStreamException>(() => Run(new FrameStream(input, output), PinAtLevel(1), closed.Set, TimeSpan.FromMilliseconds(50)));
        Assert.True(closed.IsSet);
        Assert.Contains(logs, line => line.Contains("no HELLO", StringComparison.Ordinal));
        Assert.Empty(Sent());
    }

    [Fact]
    public void AHelloThatArrivesAsTheTimerGivesUpGoesNoFurther()
    {
        // The race at the deadline, made certain: the HELLO comes in only once giveUp has closed the link. Going on
        // would put a dead link to the owner, and at level 1 hand it the live session.
        using var closed = new ManualResetEventSlim();
        using var input = new SilentAfter([], closed, FrameCodec.Encode(CurrentHello));

        Assert.False(Run(new FrameStream(input, output), PinAtLevel(1), closed.Set, TimeSpan.FromMilliseconds(1)));
        Assert.True(closed.IsSet);
        Assert.Equal(0, asked);
        Assert.Equal(0, admitted);
        Assert.Empty(Sent());
    }

    [Fact]
    public void APhoneThatNeverAnswersThePinIsGivenUpOn()
    {
        TrustThePhone();
        using var closed = new ManualResetEventSlim();
        using var input = new SilentAfter(FrameCodec.Encode(CurrentHello), closed);

        Assert.Throws<EndOfStreamException>(() => Run(new FrameStream(input, output), PinAtLevel(3), closed.Set, pinTimeout: TimeSpan.FromMilliseconds(50)));
        Assert.True(closed.IsSet);
        Assert.Contains(logs, line => line.Contains("no PIN", StringComparison.Ordinal));
        Assert.Equal([new PinRequest(5, 0)], Sent());
        Assert.Equal(0, admitted);
    }

    [Fact]
    public void NoLogOrStatusLineHoldsTheDigits()
    {
        var pin = PinAtLevel(3);
        Connect(pin, CurrentHello, Wrong, Right);
        Connect(pin, CurrentHello, Wrong, Wrong, Wrong, Wrong, Wrong);
        Connect(pin, CurrentHello);

        Assert.NotEmpty(logs);
        Assert.NotEmpty(statuses);
        Assert.DoesNotContain(
            logs.Concat(statuses),
            line => line.Contains("123456", StringComparison.Ordinal) || line.Contains("654321", StringComparison.Ordinal));
    }

    [Fact]
    public void ASessionEndedBetweenHelloAndTheQuestionIsNeverAsked()
    {
        ended.Cancel();

        Assert.Throws<OperationCanceledException>(() => Connect(PinAtLevel(1), CurrentHello));
        Assert.Equal(0, asked);
        Assert.Equal(0, admitted);
        Assert.Empty(Sent());

        // Not taken as the owner declining it, and not left asking: the next phone is asked as usual.
        Assert.False(trust.Refuses(Phone));
    }

    [Fact]
    public void ASessionEndedUnderAnOpenQuestionIsRefusedEvenOnAYes()
    {
        whileAsking = ended.Cancel;

        Assert.Throws<OperationCanceledException>(() => Connect(PinAtLevel(1), CurrentHello));
        Assert.Equal(1, asked);
        Assert.Equal(0, admitted);
        Assert.Empty(Sent());
        Assert.Empty(trust.TrustedPhones);
        Assert.False(trust.Refuses(Phone));
    }

    public void Dispose()
    {
        ended.Dispose();
        output.Dispose();
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// A phone that says <paramref name="said"/> and then nothing: the read blocks until the link is closed, and then
    /// ends, which is what closing a socket does to a blocked read. Bytes in <paramref name="late"/> are read after
    /// the close and before the end, as a frame already on its way when the link was closed would be.
    /// </summary>
    private sealed class SilentAfter(byte[] said, ManualResetEventSlim closed, byte[]? late = null) : Stream
    {
        private readonly byte[] afterClose = late ?? [];
        private int position;
        private bool waited;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var source = waited ? afterClose : said;
            if (position < source.Length)
            {
                var n = Math.Min(count, source.Length - position);
                Array.Copy(source, position, buffer, offset, n);
                position += n;
                return n;
            }

            if (waited)
            {
                return 0;
            }

            closed.Wait(TimeSpan.FromSeconds(10));
            waited = true;
            position = 0;
            return Read(buffer, offset, count);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
