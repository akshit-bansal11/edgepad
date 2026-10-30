using System.Text;
using Edgepad.Blob;
using Edgepad.Macros;
using Edgepad.Net;
using Edgepad.Protocol;
using Xunit;
using static System.FormattableString;

namespace Edgepad.Tests;

/// <summary>
/// The trust boundary is what is worth pinning here: whatever token arrives, the store either gains a row this laptop's
/// own catalog offered or stays byte-identical on disk. Nothing launches: the launcher only writes down what it is asked.
/// </summary>
public sealed class AppPickerTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "edgepad-tests-" + Guid.NewGuid().ToString("N"));
    private readonly List<Frame> wire = [];
    private readonly List<int> pauses = [];
    private readonly FakeCatalog catalog = new();
    private readonly NoLauncher launcher = new();
    private readonly MacroStore store;

    public AppPickerTests() => store = new MacroStore(MacrosFile, launcher, work => work());

    private string MacrosFile => Path.Combine(dir, "macros.txt");

    [Fact]
    public void TheListIsSortedAndEachRowCarriesItsToken()
    {
        catalog.Apps = [new("Zed", "z.lnk", null), new("alpha", "a.lnk", null), new("Beta", "b.lnk", null)];

        Assert.True(Picker().List());

        string[] expected = ["1.0\talpha", "1.1\tBeta", "1.2\tZed"];
        Assert.Equal(expected, Lines());
    }

    [Fact]
    public void TheListStopsAtItsCapAndAlwaysLosesTheSameApps()
    {
        catalog.Apps = [.. Enumerable.Range(0, AppPicker.MaxApps + 1).Reverse().Select(i => App(Invariant($"{i:D3}")))];

        Picker().List();

        var lines = Lines();
        Assert.Equal(AppPicker.MaxApps, lines.Length);
        Assert.Equal("1.0\t000", lines[0]);
        Assert.Equal("1.399\t399", lines[^1]);
    }

    [Fact]
    public void ANameIsCutToItsCapOnAWholeCharacter()
    {
        var pair = char.ConvertFromUtf32(0x1F600);
        catalog.Apps = [App(new string('a', AppPicker.MaxNameChars + 10)), App(new string('b', AppPicker.MaxNameChars - 1) + pair)];

        Picker().List();

        var lines = Lines();
        Assert.Equal("1.0\t" + new string('a', AppPicker.MaxNameChars), lines[0]);
        Assert.Equal("1.1\t" + new string('b', AppPicker.MaxNameChars - 1), lines[1]);
    }

    [Fact]
    public void ANameCannotBreakARow()
    {
        catalog.Apps = [App("One\ttwo\nthree\r"), App(" \t ")];

        Picker().List();

        Assert.Equal("1.0\tOne two three", Assert.Single(Lines()));
    }

    [Fact]
    public void AScanThatFailsStillAnswersThePhone()
    {
        catalog.ScanThrows = true;

        Assert.True(Picker().List());

        Assert.Empty(Lines());
        Assert.Single(wire, frame => frame is BlobEnd);
    }

    [Fact]
    public void EachIconIsLabelledWithItsTokenAndPaced()
    {
        catalog.Apps = [App("One"), App("Two")];
        catalog.Icon = _ => new byte[10];

        Picker().List();

        string[] labels = ["1.0", "1.1"];
        Assert.Equal(labels, wire.OfType<BlobBegin>().Where(b => b.Kind == (byte)BlobKind.AppIcon).Select(b => b.Label));
        int[] ticks = [BlobSender.TickMs, BlobSender.TickMs];
        Assert.Equal(ticks, pauses);
    }

    [Fact]
    public void AnIconThatIsMissingTooBigOrFailingCostsOnlyThatIcon()
    {
        catalog.Apps = [App("A"), App("B"), App("C"), App("D"), App("E")];
        catalog.Icon = app => app.Name switch
        {
            "A" => null,
            "B" => new byte[AppPicker.MaxIconBytes + 1],
            "C" => throw new IOException("the file is locked"),
            "D" => new byte[AppPicker.MaxIconBytes],
            _ => [],
        };

        Picker().List();

        var icon = Assert.Single(wire.OfType<BlobBegin>(), b => b.Kind == (byte)BlobKind.AppIcon);
        Assert.Equal("1.3", icon.Label);
        Assert.Equal((uint)AppPicker.MaxIconBytes, icon.Size);
    }

    [Fact]
    public void ANewListStopsTheOldListsIcons()
    {
        var picker = Picker();
        catalog.Apps = [App("One"), App("Two")];
        catalog.Icon = app =>
        {
            if (app.Name == "One" && catalog.Scans == 1)
            {
                picker.List();
            }

            return [1];
        };

        picker.List();

        // Two lists and the second's two icons. The first's own icon opened but never ended, and its second never began.
        Assert.Equal(4, wire.OfType<BlobEnd>().Count());
        Assert.DoesNotContain(wire.OfType<BlobBegin>(), b => b.Label == "1.1");
    }

    [Fact]
    public void CancelStopsTheIconsAndLeavesTheListUsable()
    {
        var picker = Picker();
        catalog.Apps = [App("One"), App("Two")];
        catalog.Icon = _ =>
        {
            picker.Cancel();
            return [1];
        };

        picker.List();

        Assert.Single(wire, frame => frame is BlobEnd);
        Assert.Equal(AppAddStatus.Added, picker.Add("1.1"));
    }

    [Fact]
    public void ACancelDuringTheScanSendsNothing()
    {
        var work = new List<Action>();
        var picker = new AppPicker(catalog, store, Sender(), TestContext.Current.CancellationToken, work.Add, _ => true);
        catalog.Apps = [App("One")];

        picker.List();
        picker.Cancel();
        work[0]();

        Assert.Empty(wire);
        Assert.Equal(AppAddStatus.Stale, picker.Add("1.0"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("1")]
    [InlineData("1.")]
    [InlineData(".0")]
    [InlineData("abc")]
    [InlineData("1.x")]
    [InlineData("x.0")]
    [InlineData("1.-1")]
    [InlineData("-1.0")]
    [InlineData("+1.0")]
    [InlineData("1.1")]
    [InlineData("1.99")]
    [InlineData("1.99999999999")]
    [InlineData("99999999999.0")]
    [InlineData("0.0")]
    [InlineData("2.0")]
    [InlineData("1.0.0")]
    [InlineData(" 1.0")]
    [InlineData("1.0 ")]
    [InlineData("C:\\Windows\\System32\\cmd.exe")]
    [InlineData("/usr/bin/sh")]
    public void ATokenThatNamesNoRowChangesNothing(string token)
    {
        store.Save([new Macro("Kept", "kept.exe", null)]);
        var before = File.ReadAllBytes(MacrosFile);
        var picker = Picker();
        catalog.Apps = [App("One")];
        picker.List();

        Assert.Equal(AppAddStatus.Stale, picker.Add(token));

        Assert.Equal(before, File.ReadAllBytes(MacrosFile));
        Assert.Equal("Kept", Assert.Single(store.Macros).Name);
    }

    [Fact]
    public void NoTokenWorksBeforeAList()
    {
        var picker = Picker();

        Assert.Equal(AppAddStatus.Stale, picker.Add("0.0"));
        Assert.Equal(AppAddStatus.Stale, picker.Add("1.0"));
        Assert.False(File.Exists(MacrosFile));
    }

    [Fact]
    public void ATokenFromTheLastEpochIsStale()
    {
        var picker = Picker();
        catalog.Apps = [App("One")];
        picker.List();
        picker.List();

        Assert.Equal(AppAddStatus.Stale, picker.Add("1.0"));
        Assert.False(File.Exists(MacrosFile));
        Assert.Equal(AppAddStatus.Added, picker.Add("2.0"));
    }

    [Fact]
    public void NoTokenWorksWhileTheNewListIsBeingScanned()
    {
        var work = new List<Action>();
        var picker = new AppPicker(catalog, store, Sender(), TestContext.Current.CancellationToken, work.Add, _ => true);
        catalog.Apps = [App("One")];

        picker.List();
        work[0]();
        picker.List();

        Assert.Equal(AppAddStatus.Stale, picker.Add("1.0"));
        Assert.Equal(AppAddStatus.Stale, picker.Add("2.0"));
    }

    [Fact]
    public void AFullListStaysAsItIs()
    {
        store.Save([.. Enumerable.Range(0, MacroStore.MaxMacros).Select(i => new Macro(Invariant($"M{i}"), Invariant($"m{i}.exe"), null))]);
        var before = File.ReadAllBytes(MacrosFile);
        var picker = Picker();
        catalog.Apps = [App("One")];
        picker.List();

        Assert.Equal(AppAddStatus.Full, picker.Add("1.0"));

        Assert.Equal(before, File.ReadAllBytes(MacrosFile));
    }

    [Fact]
    public void TheLastFreeSlotCanBeFilled()
    {
        store.Save([.. Enumerable.Range(0, MacroStore.MaxMacros - 1).Select(i => new Macro(Invariant($"M{i}"), Invariant($"m{i}.exe"), null))]);
        var picker = Picker();
        catalog.Apps = [App("One")];
        picker.List();

        Assert.Equal(AppAddStatus.Added, picker.Add("1.0"));

        Assert.Equal(MacroStore.MaxMacros, store.Macros.Count);
    }

    [Fact]
    public void ATargetAlreadyInTheListIsADuplicateWhateverItsCase()
    {
        store.Save([new Macro("Kept", "C:\\Apps\\Tool.LNK", "-X")]);
        var before = File.ReadAllBytes(MacrosFile);
        var picker = Picker();
        catalog.Apps = [new("Tool", "c:\\apps\\tool.lnk", "-x")];
        picker.List();

        Assert.Equal(AppAddStatus.Duplicate, picker.Add("1.0"));

        Assert.Equal(before, File.ReadAllBytes(MacrosFile));
    }

    [Fact]
    public void TheSameTargetWithOtherArgumentsIsAnotherMacro()
    {
        store.Save([new Macro("Kept", "tool.exe", "-a")]);
        var picker = Picker();
        catalog.Apps = [new("Tool", "tool.exe", null)];
        picker.List();

        Assert.Equal(AppAddStatus.Added, picker.Add("1.0"));
    }

    [Fact]
    public void AWriteThatFailsIsReportedAndChangesNothing()
    {
        var picker = Picker();
        catalog.Apps = [App("One")];
        picker.List();

        // A folder where the file should be fails the write the same way on Windows and on Linux.
        Directory.CreateDirectory(MacrosFile);

        Assert.Equal(AppAddStatus.Failed, picker.Add("1.0"));
        Assert.Empty(store.Macros);
    }

    [Fact]
    public void AnEntryWithNothingToOpenFails()
    {
        var picker = Picker();
        catalog.Apps = [new("Blank", "  ", null)];
        picker.List();

        Assert.Equal(AppAddStatus.Failed, picker.Add("1.0"));
        Assert.False(File.Exists(MacrosFile));
    }

    [Fact]
    public void AnAddedAppPersistsAndIsAnnouncedToThePhone()
    {
        var picker = Picker();
        catalog.Apps = [new("Visual Studio Code Insiders", "code.lnk", "--new", "code.ico")];
        picker.List();
        var seen = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var arrived = new CountdownEvent(2);
        using var watch = store.Watch(names =>
        {
            seen.Enqueue(names);
            arrived.Signal();
        });

        Assert.Equal(AppAddStatus.Added, picker.Add("1.0"));

        var expected = new Macro("Visual Studio Co", "code.lnk", "--new", "code.ico");
        Assert.Equal(expected, Assert.Single(store.Macros));
        Assert.Equal(expected, Assert.Single(new MacroStore(MacrosFile, launcher).Macros));
        Assert.True(
            arrived.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken),
            "the watcher was never told");
        Assert.Equal(["", "Visual Studio Co"], seen);
        Assert.Empty(launcher.Launched);
    }

    public void Dispose()
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static AppEntry App(string name) => new(name, name + ".lnk", null);

    private BlobSender Sender() => new(wire.Add, LinkKind.Bluetooth, () => 0, TestContext.Current.CancellationToken, () => long.MaxValue, _ => true);

    private AppPicker Picker() => new(
        catalog,
        store,
        Sender(),
        TestContext.Current.CancellationToken,
        work => work(),
        ms =>
        {
            pauses.Add(ms);
            return true;
        });

    /// <summary>The rows of the first list on the wire.</summary>
    private string[] Lines()
    {
        var chunks = wire
            .SkipWhile(frame => frame is not BlobBegin { Kind: (byte)BlobKind.AppList })
            .Skip(1)
            .TakeWhile(frame => frame is BlobChunk)
            .OfType<BlobChunk>();
        return Encoding.UTF8.GetString([.. chunks.SelectMany(chunk => chunk.Data)]).Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private sealed class NoLauncher : IMacroLauncher
    {
        public List<string> Launched { get; } = [];

        public void Launch(string target, string? arguments) => Launched.Add(target);
    }
}

/// <summary>A catalog in memory, which answers what the test set.</summary>
internal sealed class FakeCatalog : IAppCatalog
{
    public IReadOnlyList<AppEntry> Apps { get; set; } = [];

    public bool ScanThrows { get; set; }

    public int Scans { get; private set; }

    public Func<AppEntry, byte[]?> Icon { get; set; } = _ => null;

    public IReadOnlyList<AppEntry> Scan()
    {
        Scans++;
        return ScanThrows ? throw new IOException("the folder is gone") : Apps;
    }

    byte[]? IAppCatalog.Icon(AppEntry app) => Icon(app);
}
