using Edgepad.Capture;
using Edgepad.Protocol;
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

        Assert.False(capture.Take(new FileResult(0)));
    }

    [Fact]
    public void SavingToTheLaptopOnlyPresses()
    {
        using var capture = new CaptureToPhone(new NoFolders(), _ => Assert.Fail("nothing may be sent"), () => 0);
        var pressed = 0;

        Assert.True(capture.Run(ActionId.CaptureScreen, () => ++pressed == 1));
        Assert.Equal(1, pressed);
    }

    [Fact]
    public void ASavedBeforeTheEndKeepsTheFile()
    {
        var path = TempCapture();
        CaptureToPhone? capture = null;
        capture = new CaptureToPhone(
            new NoFolders(),
            frame =>
            {
                // A phone, or a bug, claiming SAVED for a file it cannot have had yet.
                if (frame is FileBegin)
                {
                    capture!.Take(new FileResult((byte)FileStatus.Saved));
                }
            },
            () => 0);

        using (capture)
        {
            Assert.False(capture.SendFile(path));
        }

        Assert.True(System.IO.File.Exists(path));
        System.IO.File.Delete(path);
    }

    [Fact]
    public void AFileThatChangedAfterItWasSentIsKept()
    {
        var path = TempCapture();
        using var capture = Answering(path, () => System.IO.File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(1)));

        Assert.False(capture.SendFile(path));
        Assert.True(System.IO.File.Exists(path));
        System.IO.File.Delete(path);
    }

    [Fact]
    public void AnUnchangedFileSavedAfterItsEndIsDeleted()
    {
        var path = TempCapture();
        using var capture = Answering(path, () => { });

        Assert.True(capture.SendFile(path));
        Assert.False(System.IO.File.Exists(path));
    }

    /// <summary>A capture that answers SAVED a moment after FILE_END, as a phone would, after <paramref name="meanwhile"/>.</summary>
    private static CaptureToPhone Answering(string path, Action meanwhile)
    {
        CaptureToPhone? capture = null;
        capture = new CaptureToPhone(
            new NoFolders(),
            frame =>
            {
                if (frame is FileEnd)
                {
                    meanwhile();
                    _ = Task.Delay(50).ContinueWith(_ => capture!.Take(new FileResult((byte)FileStatus.Saved)), TaskScheduler.Default);
                }
            },
            () => 0);
        Assert.True(System.IO.File.Exists(path));
        return capture;
    }

    private static string TempCapture()
    {
        var path = Path.Combine(Path.GetTempPath(), $"Screenshot {Guid.NewGuid():N}.png");
        System.IO.File.WriteAllBytes(path, [.. Enumerable.Range(0, 600).Select(i => (byte)i)]);
        return path;
    }

    private sealed class NoFolders : ICaptureFolders
    {
        public IReadOnlyList<CaptureFolder> For(ActionId capture) => [];

        public bool RecordToggles => false;

        public long? ClipboardStamp() => null;

        public byte[]? ClipboardPng() => null;
    }
}
