using Edgepad.Trust;
using Xunit;

namespace Edgepad.Tests;

public sealed class LastConnectedTests : IDisposable
{
    private static readonly DateTimeOffset Moment = new(2026, 11, 8, 9, 30, 0, TimeSpan.Zero);

    private readonly string dir = Path.Combine(Path.GetTempPath(), "edgepad-tests-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(dir, "last-connected.txt");

    [Fact]
    public void ARecordedTimeRoundTripsAndTheAddressIsCaseBlind()
    {
        new LastConnected(FilePath).Record("AA:BB:CC:DD:EE:FF", Moment);

        Assert.Equal(Moment, new LastConnected(FilePath).Get("aa:bb:cc:dd:ee:ff"));
    }

    [Fact]
    public void ARecordKeepsTheOtherPhones()
    {
        var store = new LastConnected(FilePath);
        store.Record("one", Moment);
        store.Record("two", Moment.AddDays(1));

        Assert.Equal(Moment, store.Get("one"));
        Assert.Equal(Moment.AddDays(1), store.Get("two"));
    }

    [Fact]
    public void NoFileOrNoEntryIsUnknown()
    {
        Assert.Null(new LastConnected(FilePath).Get("x"));
        new LastConnected(FilePath).Record("y", Moment);
        Assert.Null(new LastConnected(FilePath).Get("x"));
    }

    [Fact]
    public void ForgetRemovesOneAndForgetAllEveryone()
    {
        var store = new LastConnected(FilePath);
        store.Record("one", Moment);
        store.Record("two", Moment);

        store.Forget("one");
        Assert.Null(store.Get("one"));
        Assert.Equal(Moment, store.Get("two"));

        store.ForgetAll();
        Assert.Null(store.Get("two"));
    }

    [Fact]
    public void BesideSitsNextToTheTrustFile()
    {
        LastConnected.Beside(Path.Combine(dir, "trusted-phone.txt")).Record("one", Moment);

        Assert.True(File.Exists(FilePath));
    }

    [Fact]
    public void ALineWithoutATimeOrWithAnUnreadableOneIsUnknown()
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(FilePath, "one\nthree\tnot-a-date\ntwo\t2026-11-08T09:30:00Z\n");

        var store = new LastConnected(FilePath);
        Assert.Null(store.Get("one"));
        Assert.Null(store.Get("three"));
        Assert.Equal(Moment, store.Get("two"));
    }

    [Fact]
    public void RowTextHasTheDayAndMonthThisYear() =>
        Assert.Equal(
            "Pixel · last connected 8 Nov",
            LastConnected.RowText("Pixel", Moment, Moment.AddDays(10), TimeZoneInfo.Utc));

    [Fact]
    public void RowTextAddsTheYearWhenItIsNotThisYear() =>
        Assert.Equal(
            "Pixel · last connected 8 Nov 2026",
            LastConnected.RowText("Pixel", Moment, new DateTimeOffset(2027, 1, 2, 0, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc));

    [Fact]
    public void RowTextForAnUnknownTimeSaysSo() =>
        Assert.Equal(
            "Pixel · not connected since this update",
            LastConnected.RowText("Pixel", null, Moment, TimeZoneInfo.Utc));

    public void Dispose()
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
