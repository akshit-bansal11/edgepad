using System.Text;
using Edgepad.Macros;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// The sanitising is what is worth pinning here: a name that kept a '/' or a tab would split into two on the
/// phone, and every button after it would run the wrong macro. Nothing here ever calls Run on an index that
/// exists — a test that launched something would be a test that launched something on whoever ran it.
/// </summary>
public sealed class MacroStoreTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "edgepad-tests-" + Guid.NewGuid().ToString("N"));

    private string MacrosFile => Path.Combine(dir, "macros.txt");

    private MacroStore NewStore() => new(MacrosFile);

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
        Assert.False(new MacroStore(Path.Combine(dir, "none.txt")).Run(0));
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

    public void Dispose()
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
