using Edgepad.Capture;
using Xunit;

namespace Edgepad.Tests;

/// <summary>Which file a capture made, decided without a disk: every rule that keeps someone else's file safe.</summary>
public sealed class CapturePickTests
{
    private static readonly DateTime Then = new(2026, 10, 25, 10, 30, 0, DateTimeKind.Utc);

    private static FileStamp File(string name, long length = 100, bool shared = false) =>
        new(Path.Combine("shots", name), length, Then, shared);

    private static readonly HashSet<string> Before = CapturePick.Before([File("old.png"), File("older.mp4")]);

    [Fact]
    public void TheOneNewFileIsPicked()
    {
        var found = CapturePick.NewFile(Before, [File("old.png"), File("Screenshot 1.png"), File("older.mp4")], out var ambiguous);

        Assert.False(ambiguous);
        Assert.Equal(File("Screenshot 1.png"), found);
    }

    [Fact]
    public void NothingNewIsNothing()
    {
        Assert.Null(CapturePick.NewFile(Before, [File("old.png"), File("older.mp4")], out var ambiguous));
        Assert.False(ambiguous);
    }

    [Fact]
    public void TwoNewFilesAreNeitherPicked()
    {
        Assert.Null(CapturePick.NewFile(Before, [File("a.png"), File("b.png")], out var ambiguous));
        Assert.True(ambiguous);
    }

    [Fact]
    public void AFileThatWasThereBeforeIsNeverPickedWhateverItsCase()
    {
        Assert.Null(CapturePick.NewFile(Before, [File("OLD.PNG")], out _));
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("installer.exe")]
    [InlineData(".partial.png")]
    [InlineData("~lock.mp4")]
    [InlineData("png")]
    public void AFileNoCaptureToolWritesIsNotACandidate(string name) =>
        Assert.False(CapturePick.Candidate(Path.Combine("shots", name)));

    [Fact]
    public void ATemporaryFileBesideTheCaptureDoesNotMakeItAmbiguous()
    {
        var found = CapturePick.NewFile(Before, [File(".Screenshot.png.part"), File("Screenshot 2.png")], out var ambiguous);

        Assert.False(ambiguous);
        Assert.Equal(File("Screenshot 2.png"), found);
    }

    [Theory]
    [InlineData("Screenshot From 2026-10-25 10-30-00.png", true)]
    [InlineData("Screenshot_20261025_103000.png", true)]
    [InlineData("Screen Shot 2026-10-25 at 10.30.00.png", true)]
    [InlineData("Screen Recording 2026-10-25 at 10.30.00.mov", true)]
    [InlineData("Screencast From 2026-10-25.webm", true)]
    [InlineData("holiday.jpg", false)]
    [InlineData("download.png", false)]
    public void InAFolderOtherAppsShareOnlyACaptureNameIsTaken(string name, bool taken)
    {
        Assert.Equal(taken, CapturePick.Candidate(Path.Combine("Pictures", name), shared: true));
        // In the tool's own folder, the name does not matter.
        Assert.True(CapturePick.Candidate(Path.Combine("Screenshots", name)));
    }

    [Fact]
    public void AFileIsSettledOnlyOnceItStopsChanging()
    {
        var first = File("Screenshot 3.png", 10);

        Assert.False(CapturePick.Unchanged(null, first));
        Assert.False(CapturePick.Unchanged(first, first with { Length = 20 }));
        Assert.False(CapturePick.Unchanged(first, first with { WrittenUtc = Then.AddSeconds(1) }));
        Assert.True(CapturePick.Unchanged(first, first));
        Assert.False(CapturePick.Unchanged(first with { Length = 0 }, first with { Length = 0 }));
    }

    [Fact]
    public void OnlyLaptopAndPhoneAreTargets()
    {
        using var capture = new CaptureToPhone(new NoFolders(), _ => { }, () => 0);

        Assert.True(capture.SetTarget(0));
        Assert.True(capture.SetTarget(1));
        Assert.False(capture.SetTarget(2));
    }

    [Fact]
    public void AnAnswerWithNoFileOnItsWayIsNotTaken()
    {
        using var capture = new CaptureToPhone(new NoFolders(), _ => { }, () => 0);

        Assert.False(capture.Take(new Protocol.FileResult(0)));
    }

    [Fact]
    public void SavingToTheLaptopOnlyPresses()
    {
        using var capture = new CaptureToPhone(new NoFolders(), _ => Assert.Fail("nothing may be sent"), () => 0);
        var pressed = 0;

        Assert.True(capture.Run(Protocol.ActionId.CaptureScreen, () => ++pressed == 1));
        Assert.Equal(1, pressed);
    }

    private sealed class NoFolders : ICaptureFolders
    {
        public IReadOnlyList<CaptureFolder> For(Protocol.ActionId capture) => [];

        public long? ClipboardStamp() => null;

        public byte[]? ClipboardPng() => null;
    }
}
