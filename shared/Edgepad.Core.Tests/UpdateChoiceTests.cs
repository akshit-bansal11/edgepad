using Edgepad.Updates;
using Xunit;

namespace Edgepad.Tests;

public sealed class UpdateChoiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly string directory = Directory.CreateTempSubdirectory("edgepad-choice").FullName;

    private string ChoiceFile => Path.Combine(directory, "update-choice.txt");

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void WithNoFileEveryUpdateIsShown()
    {
        var choice = new UpdateChoice(ChoiceFile);
        Assert.True(choice.ShouldShow(new Version(5, 0, 0), Now));
        Assert.False(choice.WasNotified(new Version(5, 0, 0)));
    }

    [Fact]
    public void ASkippedVersionStaysSkippedAfterAReloadButANewerOneIsShown()
    {
        Assert.True(new UpdateChoice(ChoiceFile).Skip(new Version(5, 0, 0)));
        var reloaded = new UpdateChoice(ChoiceFile);
        Assert.False(reloaded.ShouldShow(new Version(5, 0, 0), Now));
        Assert.True(reloaded.ShouldShow(new Version(5, 0, 1), Now));
    }

    [Fact]
    public void LaterHidesTheOfferForADayOnly()
    {
        Assert.True(new UpdateChoice(ChoiceFile).Later(Now));
        var reloaded = new UpdateChoice(ChoiceFile);
        Assert.False(reloaded.ShouldShow(new Version(5, 0, 0), Now.AddHours(1)));
        Assert.True(reloaded.ShouldShow(new Version(5, 0, 0), Now.AddHours(25)));
    }

    [Fact]
    public void ALaterFarInTheFutureDoesNotHideTheOffer()
    {
        File.WriteAllText(ChoiceFile, "later=99999999999\n");
        Assert.True(new UpdateChoice(ChoiceFile).ShouldShow(new Version(5, 0, 0), Now));
    }

    [Fact]
    public void ANotifiedVersionIsRemembered()
    {
        Assert.True(new UpdateChoice(ChoiceFile).MarkNotified(new Version(5, 0, 0)));
        var reloaded = new UpdateChoice(ChoiceFile);
        Assert.True(reloaded.WasNotified(new Version(5, 0, 0)));
        Assert.False(reloaded.WasNotified(new Version(5, 0, 1)));
    }

    [Fact]
    public void AGarbledFileReadsAsNothingChosen()
    {
        File.WriteAllBytes(ChoiceFile, [0xFF, 0xFE, 0x00, 0x3D, 0x3D, 0x0A, 0x73]);
        var choice = new UpdateChoice(ChoiceFile);
        Assert.True(choice.ShouldShow(new Version(5, 0, 0), Now));
        Assert.False(choice.WasNotified(new Version(5, 0, 0)));
    }

    [Fact]
    public void AFileThatCannotBeReadReadsAsNothingChosen()
    {
        Directory.CreateDirectory(ChoiceFile);
        Assert.True(new UpdateChoice(ChoiceFile).ShouldShow(new Version(5, 0, 0), Now));
    }
}
