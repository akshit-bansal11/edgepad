using Edgepad.Blob;
using Xunit;

namespace Edgepad.Tests;

public sealed class ClipboardHistoryTests
{
    private DateTimeOffset now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private ClipboardHistory History() => new(() => now);

    [Fact]
    public void NewestComesFirst()
    {
        var h = History();
        h.Record("a", ClipSide.Laptop);
        h.Record("b", ClipSide.Phone);

        Assert.Equal(["b", "a"], h.Items.Select(e => e.Text));
    }

    [Fact]
    public void TheTwentySixthDropsTheOldest()
    {
        var h = History();
        for (var i = 0; i < ClipboardHistory.MaxItems + 1; i++)
        {
            h.Record($"item {i}", ClipSide.Laptop);
        }

        Assert.Equal(ClipboardHistory.MaxItems, h.Items.Count);
        Assert.DoesNotContain(h.Items, e => e.Text == "item 0");
        Assert.Equal("item 25", h.Items[0].Text);
    }

    [Fact]
    public void ARepeatMovesToFrontAndTakesTheNewSideAndTime()
    {
        var h = History();
        h.Record("a", ClipSide.Laptop);
        h.Record("b", ClipSide.Laptop);
        now = now.AddMinutes(5);

        h.Record("a", ClipSide.Phone);

        Assert.Equal(["a", "b"], h.Items.Select(e => e.Text));
        Assert.Equal(ClipSide.Phone, h.Items[0].Side);
        Assert.Equal(now, h.Items[0].At);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t")]
    public void BlankIsIgnored(string text)
    {
        var h = History();

        Assert.False(h.Record(text, ClipSide.Laptop));
        Assert.Empty(h.Items);
    }

    [Fact]
    public void TheCapIsInBytes()
    {
        var h = History();

        Assert.True(h.Record(new string('x', ClipboardHistory.MaxBytes), ClipSide.Laptop));
        Assert.False(h.Record(new string('x', ClipboardHistory.MaxBytes + 1), ClipSide.Laptop));
        Assert.False(h.Record(new string('é', (ClipboardHistory.MaxBytes / 2) + 1), ClipSide.Laptop));
        Assert.Single(h.Items);
    }

    [Fact]
    public void RemoveAndClear()
    {
        var h = History();
        h.Record("a", ClipSide.Laptop);
        h.Record("b", ClipSide.Laptop);

        Assert.True(h.Remove("a"));
        Assert.False(h.Remove("a"));
        Assert.Single(h.Items);

        h.Clear();
        Assert.Empty(h.Items);
    }

    [Fact]
    public void ChangedFiresOncePerEffectiveChange()
    {
        var h = History();
        var count = 0;
        h.Changed += () => count++;

        h.Record("a", ClipSide.Laptop);
        h.Record(" ", ClipSide.Laptop);
        h.Remove("zzz");
        h.Remove("a");
        h.Clear();
        h.Record("b", ClipSide.Phone);
        h.Clear();

        Assert.Equal(4, count);
    }

    [Theory]
    [InlineData(59, AgeUnit.JustNow, 0)]
    [InlineData(60, AgeUnit.Minutes, 1)]
    [InlineData(59 * 60, AgeUnit.Minutes, 59)]
    [InlineData(60 * 60, AgeUnit.Hours, 1)]
    [InlineData(23 * 3600, AgeUnit.Hours, 23)]
    [InlineData(24 * 3600, AgeUnit.Days, 1)]
    [InlineData(-30, AgeUnit.JustNow, 0)]
    public void AgeBoundaries(int seconds, AgeUnit unit, int count)
    {
        Assert.Equal(new Age(unit, count), RelativeAge.Of(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void ConcurrentRecordsKeepTheCap()
    {
        var h = new ClipboardHistory();

        Parallel.For(0, 4, t =>
        {
            for (var i = 0; i < 200; i++)
            {
                h.Record($"{t}-{i}", ClipSide.Phone);
            }
        });

        Assert.True(h.Items.Count <= ClipboardHistory.MaxItems);
    }
}
