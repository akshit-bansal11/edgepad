using Edgepad.Protocol;
using Edgepad.Trust;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// The PIN question as the session runs it, driven frame by frame: what the phone reads arrives from a queue, what
/// the laptop sends lands in a list, and the lockout's clock is moved by hand.
/// </summary>
public sealed class PinGateTests : IDisposable
{
    private const string Phone = "(AA:BB:CC:DD:EE:FF)";

    private static readonly PinAnswer Right = new(new Pin("123456"));
    private static readonly PinAnswer Wrong = new(new Pin("654321"));

    private readonly string dir = Path.Combine(Path.GetTempPath(), "edgepad-tests-" + Guid.NewGuid().ToString("N"));
    private readonly List<Frame> sent = [];
    private readonly List<string> logs = [];

    // Milliseconds on the store's clock, moved by hand.
    private long time;

    // One iteration, as in PinStoreTests: the real work factor would take most of a second per answer.
    private PinStore NewStore() => new(dir, () => time, 1);

    private PinStore StoreAtLevel(int level)
    {
        var store = NewStore();
        Assert.True(store.SetPin("123456"));
        Assert.True(store.SetLevel(level));
        return store;
    }

    // An empty queue throws, so a gate that reads when it should not fails the test. Giving up is a no-op here: it
    // would run on a timer thread a minute in, long after these finish.
    private bool Gate(PinStore store, params Frame[] answers)
    {
        var queue = new Queue<Frame>(answers);
        var passed = PinGate.Run(Phone, store, queue.Dequeue, sent.Add, logs.Add, () => { });
        Assert.Empty(queue);
        return passed;
    }

    [Fact]
    public void LevelOneSendsNothing()
    {
        var store = NewStore();

        Assert.False(PinGate.LockedOut(Phone, store, sent.Add, logs.Add));
        Assert.True(Gate(store));
        Assert.Empty(sent);
    }

    [Fact]
    public void LevelThreeAsksEveryTime()
    {
        var store = StoreAtLevel(3);

        Assert.True(Gate(store, Right));
        Assert.True(Gate(store, Right));
        Assert.Equal([new PinRequest(5, 0), new PinRequest(5, 0)], sent);
    }

    [Fact]
    public void LevelTwoAsksOnce()
    {
        var store = StoreAtLevel(2);

        Assert.True(Gate(store, Right));
        Assert.True(Gate(store));
        Assert.Equal([new PinRequest(5, 0)], sent);
    }

    [Fact]
    public void AWrongPinIsAskedAgainWithFewerTriesAndARightOneProceeds()
    {
        var store = StoreAtLevel(3);

        Assert.True(Gate(store, Wrong, Wrong, Right));
        Assert.Equal([new PinRequest(5, 0), new PinRequest(4, 0), new PinRequest(3, 0)], sent);
        Assert.Equal(5, store.AttemptsLeft(Phone));
    }

    [Fact]
    public void FiveWrongOnesLockAndCloseUntilTheLockRunsOut()
    {
        var store = StoreAtLevel(3);

        Assert.False(Gate(store, Wrong, Wrong, Wrong, Wrong, Wrong));
        Assert.Equal(
            [new PinRequest(5, 0), new PinRequest(4, 0), new PinRequest(3, 0), new PinRequest(2, 0), new PinRequest(1, 0), new PinRequest(0, 30)],
            sent);

        // The next connection is turned away before the trust question, with what is left of the wait.
        sent.Clear();
        time += 10_000;
        Assert.True(PinGate.LockedOut(Phone, store, sent.Add, logs.Add));
        Assert.Equal([new PinRequest(0, 20)], sent);

        // And the gate itself refuses without reading, should the lock begin while the owner was being asked.
        sent.Clear();
        Assert.False(Gate(store));
        Assert.Equal([new PinRequest(0, 20)], sent);

        sent.Clear();
        time += 20_000;
        Assert.False(PinGate.LockedOut(Phone, store, sent.Add, logs.Add));
        Assert.True(Gate(store, Right));
        Assert.Equal([new PinRequest(5, 0)], sent);
    }

    [Fact]
    public void ALockOutLeftFromAHigherLevelSendsNothingAtLevelOne()
    {
        var store = StoreAtLevel(3);
        Assert.False(Gate(store, Wrong, Wrong, Wrong, Wrong, Wrong));
        Assert.True(store.SetLevel(1));
        sent.Clear();

        Assert.False(PinGate.LockedOut(Phone, store, sent.Add, logs.Add));
        Assert.True(Gate(store));
        Assert.Empty(sent);
    }

    [Fact]
    public void AnyOtherFrameWhileWaitingIsAProtocolError()
    {
        var store = StoreAtLevel(3);

        Assert.Throws<InvalidDataException>(() => Gate(store, new Ping(1)));
        Assert.Equal(5, store.AttemptsLeft(Phone));
    }

    [Fact]
    public void NoAnswerGivesUp()
    {
        var store = StoreAtLevel(3);
        using var closed = new ManualResetEventSlim();

        // What closing the socket does to a blocked read: it ends with an IOException.
        Assert.Throws<IOException>(() => PinGate.Run(
            Phone,
            store,
            () =>
            {
                closed.Wait(TimeSpan.FromSeconds(10));
                throw new IOException("closed");
            },
            sent.Add,
            logs.Add,
            closed.Set,
            TimeSpan.FromMilliseconds(50)));
        Assert.True(closed.IsSet);
        Assert.Contains(logs, line => line.Contains("no PIN", StringComparison.Ordinal));
    }

    [Fact]
    public void UnreadableSettingsCloseWithoutCountingTheAnswer()
    {
        var store = StoreAtLevel(3);
        File.WriteAllText(Path.Combine(dir, "security.txt"), "nonsense\n");

        Assert.False(Gate(store, Right));
        Assert.Equal([new PinRequest(5, 0)], sent);
        Assert.Equal(5, store.AttemptsLeft(Phone));
    }

    [Fact]
    public void NoLogLineHoldsTheDigits()
    {
        var store = StoreAtLevel(3);
        Gate(store, Wrong, Right);
        Gate(store, Wrong, Wrong, Wrong, Wrong, Wrong);

        Assert.NotEmpty(logs);
        Assert.DoesNotContain(logs, line => line.Contains("123456", StringComparison.Ordinal) || line.Contains("654321", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
