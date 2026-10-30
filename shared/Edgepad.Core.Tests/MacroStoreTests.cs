using System.Text;
using Edgepad.Macros;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// The sanitising is what is worth pinning here: a name that kept a '/' or a tab would split into two on the
/// phone, and every button after it would run the wrong macro. Run only ever reaches a recording launcher here,
/// never the shell — a test that launched something would be a test that launched something on whoever ran it.
/// </summary>
public sealed class MacroStoreTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "edgepad-tests-" + Guid.NewGuid().ToString("N"));

    private string MacrosFile => Path.Combine(dir, "macros.txt");

    private readonly RecordingLauncher launcher = new();

    private MacroStore NewStore() => new(MacrosFile, launcher, work => work());

    private MacroStore StoreWith(MacroOpenWatcherTests.FakeWindows windows)
    {
        var store = new MacroStore(MacrosFile, launcher, work => work(), windows, _ => Task.CompletedTask);
        store.Save([new Macro("One", "one.exe", null), new Macro("Two", "two.exe", "-x")]);
        return store;
    }

    [Fact]
    public void NamesAreTheButtonLabelsJoinedForOneTextFrame()
    {
        var store = NewStore();
        store.Save([new Macro("Mail", "https://mail.google.com", null), new Macro("Code", "code.exe", "F:\\projects")]);

        Assert.Equal("Mail/Code", store.Names());
    }

    [Fact]
    public void ANameCannotKeepWhatSeparatesOneNameFromTheNext()
    {
        var store = NewStore();
        store.Save([new Macro("In/box\tnow", "mail.exe", null)]);

        Assert.Equal("In-box now", store.Macros[0].Name);
        Assert.Equal("In-box now", store.Names());
    }

    [Fact]
    public void ALongNameIsCutToWhatAPhoneButtonCanShow()
    {
        var store = NewStore();
        store.Save([new Macro("Visual Studio Code Insiders", "code.exe", null)]);

        Assert.Equal("Visual Studio Co", store.Macros[0].Name);
    }

    [Fact]
    public void AMacroWithNothingToLaunchIsDroppedRatherThanTakingAnIndex()
    {
        var store = NewStore();
        store.Save([new Macro("First", "one.exe", null), new Macro("Blank", "  ", null), new Macro("Last", "two.exe", null)]);

        Assert.Equal(2, store.Macros.Count);
        Assert.Equal("First/Last", store.Names());
    }

    [Fact]
    public void TheEditorIsToldWhichMacroHasNothingToOpen()
    {
        // The same test Save applies when it drops a row, so what the editor flags and what the store would
        // have dropped cannot drift apart: blank, spaces, and a tab that Plain turns into a space.
        Assert.Equal(-1, MacroStore.WithoutTarget([new Macro("One", "one.exe", null), new Macro("Two", "two.exe", null)]));
        Assert.Equal(1, MacroStore.WithoutTarget([new Macro("One", "one.exe", null), new Macro("Blank", "  ", null)]));
        Assert.Equal(0, MacroStore.WithoutTarget([new Macro("Tab", "\t", null), new Macro("", "", null)]));
        Assert.Equal(-1, MacroStore.WithoutTarget([]));
    }

    [Fact]
    public void AMacroWithNoArgumentsHasNoneRatherThanAnEmptyCommandLine()
    {
        var store = NewStore();
        store.Save([new Macro("Files", "explorer.exe", "   ")]);

        Assert.Null(store.Macros[0].Arguments);
        Assert.Null(NewStore().Macros[0].Arguments);
    }

    [Fact]
    public void MacrosSurviveARestart()
    {
        var saved = new Macro("Code", "code.exe", "F:\\projects");
        NewStore().Save([saved]);

        Assert.Equal(saved, Assert.Single(NewStore().Macros));
    }

    [Fact]
    public void NoMoreThanTheCapIsKept()
    {
        var store = NewStore();
        store.Save([.. Enumerable.Range(0, MacroStore.MaxMacros + 8).Select(i => new Macro($"M{i}", "one.exe", null))]);

        Assert.Equal(MacroStore.MaxMacros, store.Macros.Count);
        Assert.Equal("M0", store.Macros[0].Name);
    }

    [Fact]
    public void SavingTellsAWatcherTheNewNames()
    {
        // The bug this covers: the names went out once at the handshake, so a macro added while the phone
        // was connected did not reach it until the app was closed and opened again.
        // Reports after the opening one are handed to the thread pool, so a watcher cannot block the UI
        // thread that saved. The test therefore waits for them instead of assuming they have arrived, and
        // collects into a concurrent queue rather than a List that two threads would race.
        var store = NewStore();
        var seen = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var arrived = new CountdownEvent(3);
        using var watch = store.Watch(names =>
        {
            seen.Enqueue(names);
            arrived.Signal();
        });

        store.Save([new Macro("Chrome", "chrome.exe", null)]);
        store.Save([new Macro("Chrome", "chrome.exe", null), new Macro("Notes", "notes.exe", null)]);

        Assert.True(
            arrived.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken),
            "the watcher was never told");
        Assert.Equal(["", "Chrome", "Chrome/Notes"], seen);
    }

    [Fact]
    public void ADisposedWatchHearsNothingMore()
    {
        // A second watcher, still subscribed, is what makes this deterministic: once it has been told about
        // the save, that save's round of dispatch is done, so a disposed watcher that has heard nothing by
        // then never will. Sleeping instead would only prove the pool had not got round to it yet.
        var store = NewStore();
        var gone = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var kept = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var told = new CountdownEvent(2);
        using var live = store.Watch(names =>
        {
            kept.Enqueue(names);
            told.Signal();
        });
        store.Watch(gone.Enqueue).Dispose();

        store.Save([new Macro("Chrome", "chrome.exe", null)]);

        Assert.True(
            told.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken),
            "the live watcher was never told");

        // The live one heard both: its opening report and the save. The disposed one heard only its own
        // opening report, which Watch makes on the caller's thread before handing back the handle.
        Assert.Equal(["", "Chrome"], kept);
        Assert.Single(gone);
    }

    [Fact]
    public void AFullGridOfLongestNamesStillFitsOneFrame()
    {
        // The whole reason the grid is fifteen. If this fails, either the grid grew or the name budget did,
        // and some macro is about to reach the phone as a blank button.
        var store = NewStore();
        var full = new string('n', MacroStore.MaxNameBytes);
        store.Save([.. Enumerable.Range(0, MacroStore.MaxMacros).Select(_ => new Macro(full, "one.exe", null))]);

        var names = store.Names();

        Assert.Equal(MacroStore.MaxMacros, names.Split('/').Length);
        Assert.All(names.Split('/'), name => Assert.Equal(full, name));
        Assert.True(
            Encoding.UTF8.GetByteCount(names) <= MacroStore.MaxNamesBytes,
            $"{Encoding.UTF8.GetByteCount(names)} bytes is past what a TEXT payload's length byte can say");
    }

    [Fact]
    public void ANameIsCutByItsBytesAndNeverThroughACharacter()
    {
        // Sixteen emoji are sixteen characters and sixty-four bytes. A cap in characters would have let this
        // through and lost a label on the wire; a careless cut in bytes would have split a surrogate pair.
        var store = NewStore();
        store.Save([new Macro(string.Concat(Enumerable.Repeat("😀", 16)), "one.exe", null)]);

        var name = store.Macros[0].Name;

        // Four emoji at four bytes each is the budget exactly, and eight chars is four whole surrogate
        // pairs: a cut through one would leave an odd length and a replacement character on the round trip.
        Assert.Equal(MacroStore.MaxNameBytes, Encoding.UTF8.GetByteCount(name));
        Assert.Equal(8, name.Length);
        Assert.DoesNotContain('�', Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(name)));
    }

    [Fact]
    public void AHandEditedFileIsHeldToTheSameRulesAsTheEditor()
    {
        Directory.CreateDirectory(dir);
        File.WriteAllLines(MacrosFile, ["", "no tabs at all", "Way too long a name\tone.exe", "  Spaced  \ttwo.exe\t-x  "]);

        var store = NewStore();

        Assert.Equal(2, store.Macros.Count);
        Assert.Equal(new Macro("Way too long a n", "one.exe", null), store.Macros[0]);
        Assert.Equal(new Macro("Spaced", "two.exe", "-x"), store.Macros[1]);
    }

    [Fact]
    public void ReloadPicksUpAFileChangedUnderneathIt()
    {
        var store = NewStore();
        store.Save([new Macro("First", "one.exe", null)]);

        File.WriteAllLines(MacrosFile, ["Second\ttwo.exe\t"]);
        store.Reload();

        Assert.Equal("Second", store.Names());
    }

    [Fact]
    public void AnIndexOutsideTheListRunsNothing()
    {
        var store = NewStore();
        store.Save([new Macro("Only", "one.exe", null)]);

        Assert.False(store.Run(-1));
        Assert.False(store.Run(1));
        Assert.False(new MacroStore(Path.Combine(dir, "none.txt"), launcher, work => work()).Run(0));
    }

    [Fact]
    public void RunLaunchesTheTargetOfThatSlotAndNothingElse()
    {
        var store = NewStore();
        store.Save([new Macro("One", "one.exe", null), new Macro("Two", "two.exe", "-x")]);

        Assert.True(store.Run(1));
        Assert.True(store.Run(0));

        Assert.Equal(2, launcher.Launched.Count);
        Assert.Equal(("two.exe", "-x"), launcher.Launched[0]);
        Assert.Equal(("one.exe", (string?)null), launcher.Launched[1]);
    }

    [Fact]
    public void AnEmptyOrOutOfRangeSlotLaunchesNothing()
    {
        var store = NewStore();
        store.Save([new Macro("Only", "one.exe", null)]);

        Assert.False(store.Run(-1));
        Assert.False(store.Run(1));
        Assert.False(store.Run(IMacroRunner.MaxMacros));
        Assert.Empty(launcher.Launched);
    }

    [Fact]
    public void ALaunchThatThrowsDoesNotEscape()
    {
        launcher.Fail = true;
        var store = NewStore();
        store.Save([new Macro("Gone", "moved.exe", null)]);

        Assert.True(store.Run(0));
        Assert.Empty(launcher.Launched);
    }

    [Fact]
    public void ASlowLaunchDoesNotBlockTheCaller()
    {
        using var release = new ManualResetEventSlim();
        using var started = new ManualResetEventSlim();
        var blocking = new BlockingLauncher(started, release);
        var store = new MacroStore(MacrosFile, blocking);
        store.Save([new Macro("Slow", "slow.exe", null)]);

        try
        {
            Assert.True(store.Run(0));
            Assert.True(started.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        }
        finally
        {
            release.Set();
        }

        Assert.True(blocking.Done.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void AFailedSaveReportsWhyAndLeavesTheListAlone()
    {
        var store = NewStore();
        store.Save([new Macro("Kept", "one.exe", null)]);
        var seen = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var arrived = new CountdownEvent(2);
        using var watch = store.Watch(names =>
        {
            seen.Enqueue(names);
            arrived.Signal();
        });

        // A folder where the file should be fails the write the same way on Windows and on Linux.
        File.Delete(MacrosFile);
        Directory.CreateDirectory(MacrosFile);

        var failure = store.Save([new Macro("Lost", "two.exe", null)]);

        Assert.False(string.IsNullOrEmpty(failure));
        Assert.Equal("Kept", Assert.Single(store.Macros).Name);
        Assert.Equal("Kept", store.Names());
        Assert.False(File.Exists(MacrosFile + ".tmp"), "the failed write left its temporary file behind");

        // A save that works afterwards is the fence: the watcher hears its opening report and this, and a phone
        // told about the failed one would have heard a list that was never on disk.
        Directory.Delete(MacrosFile);
        Assert.Null(store.Save([new Macro("Again", "two.exe", null)]));
        Assert.True(
            arrived.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken),
            "the watcher was never told");
        Assert.Equal(["Kept", "Again"], seen);
    }

    [Fact]
    public void ASuccessfulSaveReturnsNoFailure()
    {
        Assert.Null(NewStore().Save([new Macro("Fine", "one.exe", null)]));
    }

    [Fact]
    public void AnIconSurvivesARestart()
    {
        var saved = new Macro("Code", "code.exe", "F:\\projects", "F:\\art\\code.png");
        NewStore().Save([saved]);

        Assert.Equal(saved, Assert.Single(NewStore().Macros));
    }

    [Fact]
    public void AMacroWithNoIconWritesTheSameLineItAlwaysDid()
    {
        // The icon is a fourth field, and a list with none on it must still produce the bytes a build from
        // before icons wrote. Otherwise the first run after an update rewrites a file it has nothing to add
        // to, which is the sort of churn that makes an owner wonder what else changed.
        NewStore().Save([new Macro("Files", "explorer.exe", null)]);

        var line = Assert.Single(File.ReadAllLines(MacrosFile));

        Assert.Equal("Files\texplorer.exe\t", line);
        Assert.Equal(3, line.Split('\t').Length);
    }

    [Fact]
    public void AFileWrittenBeforeIconsExistedStillReads()
    {
        Directory.CreateDirectory(dir);
        File.WriteAllLines(MacrosFile, ["Chrome\tchrome.exe\t", "Notes\tnotes.exe"]);

        var store = NewStore();

        Assert.Equal(new Macro("Chrome", "chrome.exe", null), store.Macros[0]);
        Assert.Equal(new Macro("Notes", "notes.exe", null), store.Macros[1]);
        Assert.All(store.Macros, macro => Assert.Null(macro.Icon));
    }

    [Fact]
    public void AnIconPathIsScrubbedLikeEveryOtherField()
    {
        // A tab inside the icon path would split the line into five fields, and the fifth would be silently
        // dropped on the next read — a macro that quietly lost its picture rather than one that failed.
        var store = NewStore();
        store.Save([new Macro("Art", "one.exe", null, "  F:\\a\tb.png  ")]);

        Assert.Equal("F:\\a b.png", store.Macros[0].Icon);
        Assert.Equal(4, Assert.Single(File.ReadAllLines(MacrosFile)).Split('\t').Length);
    }

    [Fact]
    public void AnIconThatIsOnlySpacesIsNoIcon()
    {
        var store = NewStore();
        store.Save([new Macro("Files", "explorer.exe", null, "   ")]);

        Assert.Null(store.Macros[0].Icon);
        Assert.Null(NewStore().Macros[0].Icon);
    }

    [Fact]
    public void AnAppInFrontIsMinimizedAndNeverLaunched()
    {
        var windows = new MacroOpenWatcherTests.FakeWindows { State = _ => OpenState.Front };

        Assert.True(StoreWith(windows).Run(1));

        Assert.Equal(["Minimize Two"], windows.Calls);
        Assert.Empty(launcher.Launched);
    }

    [Fact]
    public void AnAppBehindOthersIsRaisedAndNeverLaunched()
    {
        var windows = new MacroOpenWatcherTests.FakeWindows { State = _ => OpenState.Open };

        Assert.True(StoreWith(windows).Run(0));

        Assert.Equal(["Raise One"], windows.Calls);
        Assert.Empty(launcher.Launched);
    }

    [Fact]
    public void AClosedAppLaunches()
    {
        var windows = new MacroOpenWatcherTests.FakeWindows { State = _ => OpenState.Closed };

        Assert.True(StoreWith(windows).Run(1));

        Assert.Equal(("two.exe", "-x"), Assert.Single(launcher.Launched));
        Assert.Empty(windows.Calls);
    }

    [Fact]
    public void AnUntrackedTargetLaunchesAndIsNotFollowed()
    {
        var windows = new MacroOpenWatcherTests.FakeWindows { State = _ => OpenState.Untracked };

        Assert.True(StoreWith(windows).Run(0));

        Assert.Single(launcher.Launched);
        Assert.Equal(1, windows.Surveys);
    }

    [Fact]
    public void ASurveyThatThrowsLaunches()
    {
        var windows = new MacroOpenWatcherTests.FakeWindows { State = _ => throw new InvalidOperationException("no desktop") };

        Assert.True(StoreWith(windows).Run(0));

        Assert.Single(launcher.Launched);
    }

    [Fact]
    public void ALaptopThatCannotSeeWindowsNeverSurveysAndLaunches()
    {
        var windows = new MacroOpenWatcherTests.FakeWindows { Available = false, State = _ => OpenState.Front };

        Assert.True(StoreWith(windows).Run(0));

        Assert.Equal(0, windows.Surveys);
        Assert.Single(launcher.Launched);
    }

    [Fact]
    public void AMinimizeThatDidNotActFallsThroughToALaunch()
    {
        var windows = new MacroOpenWatcherTests.FakeWindows { State = _ => OpenState.Front, Acts = false };

        Assert.True(StoreWith(windows).Run(0));

        Assert.Equal(["Minimize One"], windows.Calls);
        Assert.Single(launcher.Launched);
    }

    [Fact]
    public void ALaunchFromClosedRaisesAWindowThatAppearsBehind()
    {
        var surveys = 0;
        var windows = new MacroOpenWatcherTests.FakeWindows
        {
            State = _ => ++surveys == 1 ? OpenState.Closed : OpenState.Open,
        };

        Assert.True(StoreWith(windows).Run(0));

        Assert.Single(launcher.Launched);
        Assert.Equal(["Raise One"], windows.Calls);
    }

    [Fact]
    public void ALaunchThatThrowsIsNotFollowed()
    {
        launcher.Fail = true;
        var windows = new MacroOpenWatcherTests.FakeWindows { State = _ => OpenState.Closed };

        Assert.True(StoreWith(windows).Run(0));

        Assert.Equal(1, windows.Surveys);
    }

    [Fact]
    public void CloseActsOnTheMacroInThatSlot()
    {
        var windows = new MacroOpenWatcherTests.FakeWindows();

        Assert.True(StoreWith(windows).Close(1));

        Assert.Equal(["Close Two"], windows.Calls);
        Assert.Empty(launcher.Launched);
    }

    [Fact]
    public void CloseRefusesABadSlotAndALaptopWithoutWindows()
    {
        var windows = new MacroOpenWatcherTests.FakeWindows();
        var store = StoreWith(windows);

        Assert.False(store.Close(-1));
        Assert.False(store.Close(2));
        Assert.False(StoreWith(new MacroOpenWatcherTests.FakeWindows { Available = false }).Close(0));
        Assert.False(NewStore().Close(0));
        Assert.Empty(windows.Calls);
    }

    [Fact]
    public void AWatcherExistsOnlyWhereWindowsCanBeSeen()
    {
        var available = StoreWith(new MacroOpenWatcherTests.FakeWindows());
        var unavailable = StoreWith(new MacroOpenWatcherTests.FakeWindows { Available = false });

        using var watcher = available.CreateWatcher(_ => { });

        Assert.NotNull(watcher);
        Assert.Null(unavailable.CreateWatcher(_ => { }));
        Assert.Null(NewStore().CreateWatcher(_ => { }));
    }

    [Fact]
    public void WindowActionsNeverTouchTheFile()
    {
        var windows = new MacroOpenWatcherTests.FakeWindows { State = _ => OpenState.Front };
        var store = StoreWith(windows);
        var before = File.ReadAllBytes(MacrosFile);

        store.Run(0);
        windows.State = _ => OpenState.Open;
        store.Run(1);
        windows.State = _ => OpenState.Closed;
        store.Run(0);
        store.Close(1);

        Assert.Equal(before, File.ReadAllBytes(MacrosFile));
    }

    private sealed class RecordingLauncher : IMacroLauncher
    {
        public List<(string Target, string? Arguments)> Launched { get; } = [];

        public bool Fail { get; set; }

        public void Launch(string target, string? arguments)
        {
            if (Fail)
            {
                throw new InvalidOperationException("no such target");
            }

            Launched.Add((target, arguments));
        }
    }

    private sealed class BlockingLauncher(ManualResetEventSlim started, ManualResetEventSlim release) : IMacroLauncher
    {
        public ManualResetEventSlim Done { get; } = new();

        public void Launch(string target, string? arguments)
        {
            started.Set();
            release.Wait();
            Done.Set();
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
