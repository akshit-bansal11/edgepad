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
    public void NamesStopAtTheFramesLengthRatherThanSplittingANameInTwo()
    {
        var store = NewStore();
        var full = new string('n', MacroStore.MaxNameChars);
        store.Save([.. Enumerable.Range(0, MacroStore.MaxMacros).Select(_ => new Macro(full, "one.exe", null))]);

        var names = store.Names();
        var bytes = Encoding.UTF8.GetByteCount(names);

        Assert.True(bytes <= 255, $"{bytes} bytes is past what a TEXT payload's length byte can say");
        Assert.All(names.Split('/'), name => Assert.Equal(full, name));

        // The macros past the frame's length keep their indices and still run; they only lose their labels.
        Assert.Equal(MacroStore.MaxMacros, store.Macros.Count);
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

    public void Dispose()
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
