using Edgepad.Macros;
using Xunit;

namespace Edgepad.Tests;

/// <summary>Against a fake desktop, so nothing here depends on what is running on whoever ran it.</summary>
public sealed class MacroOpenWatcherTests
{
    private readonly List<Macro> macros = [new("One", "one.exe", null), new("Two", "two.exe", null)];
    private readonly List<string> sent = [];
    private readonly FakeWindows windows = new();

    private MacroOpenWatcher NewWatcher() => new(() => macros, windows, sent.Add, TimeSpan.FromHours(1));

    [Fact]
    public void TheFirstPollSendsOneCharacterPerSlot()
    {
        windows.State = macro => macro.Name == "One" ? OpenState.Front : OpenState.Closed;
        using var watcher = NewWatcher();

        watcher.Poll();

        Assert.Equal(["20"], sent);
    }

    [Fact]
    public void EveryStateHasItsWireCharacter()
    {
        macros.AddRange([new("Three", "three.exe", null), new("Four", "four.exe", null)]);
        windows.State = macro => macro.Name switch
        {
            "One" => OpenState.Untracked,
            "Two" => OpenState.Closed,
            "Three" => OpenState.Open,
            _ => OpenState.Front,
        };
        using var watcher = NewWatcher();

        watcher.Poll();

        Assert.Equal(["-012"], sent);
    }

    [Fact]
    public void ThePhoneHearsOnlyAChange()
    {
        using var watcher = NewWatcher();

        watcher.Poll();
        watcher.Poll();
        windows.State = _ => OpenState.Open;
        watcher.Poll();
        watcher.Poll();

        Assert.Equal(["00", "11"], sent);
    }

    [Fact]
    public void ASurveyThatFailsSendsNothingTrackedOnceAndThenRecovers()
    {
        using var watcher = NewWatcher();
        watcher.Poll();

        windows.State = _ => throw new InvalidOperationException("no desktop");
        watcher.Poll();
        watcher.Poll();
        windows.State = _ => OpenState.Closed;
        watcher.Poll();

        Assert.Equal(["00", "--", "00"], sent);
    }

    [Fact]
    public void ASendThatFailsDoesNotEscapeTheTimer()
    {
        using var watcher = new MacroOpenWatcher(
            () => macros, windows, _ => throw new IOException("the phone went away"), TimeSpan.FromHours(1));

        watcher.Poll();
    }

    [Fact]
    public void NothingIsSentAfterStop()
    {
        using var watcher = NewWatcher();
        watcher.Poll();
        watcher.Stop();

        windows.State = _ => OpenState.Open;
        watcher.Poll();

        Assert.Equal(["00"], sent);
    }

    [Fact]
    public void StartPollsAtOnce()
    {
        using var told = new ManualResetEventSlim();
        using var watcher = new MacroOpenWatcher(() => macros, windows, _ => told.Set(), TimeSpan.FromHours(1));

        watcher.Start();

        Assert.True(told.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ASecondStartTellsThePhoneAgainWhateverItWasTold()
    {
        using var second = new SemaphoreSlim(0);
        using var watcher = new MacroOpenWatcher(() => macros, windows, _ => second.Release(), TimeSpan.FromHours(1));

        watcher.Start();
        Assert.True(second.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        watcher.Start();
        Assert.True(second.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void AStartAfterDisposeStartsNothing()
    {
        using var told = new ManualResetEventSlim();
        var watcher = new MacroOpenWatcher(() => macros, windows, _ => told.Set(), TimeSpan.FromHours(1));
        watcher.Dispose();

        watcher.Start();

        Assert.False(told.Wait(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken));
    }

    /// <summary>A desktop that writes each action down and answers whatever the test set.</summary>
    internal sealed class FakeWindows : IMacroWindows
    {
        public bool Available { get; set; } = true;

        public bool Acts { get; set; } = true;

        public Func<Macro, OpenState> State { get; set; } = _ => OpenState.Closed;

        public List<string> Calls { get; } = [];

        public int Surveys { get; private set; }

        public OpenState[] Survey(IReadOnlyList<Macro> macros)
        {
            Surveys++;
            return [.. macros.Select(State)];
        }

        public bool Minimize(Macro macro) => Record($"Minimize {macro.Name}");

        public bool Raise(Macro macro) => Record($"Raise {macro.Name}");

        public bool Close(Macro macro) => Record($"Close {macro.Name}");

        private bool Record(string call)
        {
            Calls.Add(call);
            return Acts;
        }
    }
}
