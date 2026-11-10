using System.Text;
using Edgepad.Power;
using Edgepad.Protocol;
using Xunit;

namespace Edgepad.Tests;

public sealed class PowerTextTests
{
    private static readonly TimeSpan Never = TimeSpan.FromHours(1);

    [Fact]
    public void GoldenTextFramesCarryTheirPayloadBytes()
    {
        // frames.txt: "TEXT <kind> <payload> = hex"; the payload has no spaces there and may be absent.
        var lines = Fixtures.Lines("frames.txt").Where(l => l.StartsWith("TEXT 19") || l.StartsWith("TEXT 20")).ToArray();
        Assert.NotEmpty(lines);
        foreach (var line in lines)
        {
            var halves = line.Split(" = ");
            var head = halves[0].Split(' ', 3);
            var payload = head.Length > 2 ? head[2] : string.Empty;
            var hex = halves[1].Split(' ').Select(h => Convert.ToByte(h, 16)).ToArray();
            Assert.Equal(0x41, hex[0]);
            Assert.Equal(byte.Parse(head[1]), hex[1]);
            Assert.Equal(hex.Length - 3, hex[2]);
            Assert.Equal(payload, Encoding.UTF8.GetString(hex, 3, hex[2]));
        }
    }

    [Fact]
    public void EncodeReproducesTheGoldenPayloads()
    {
        Assert.Equal("laptop:87:1:Desk|headphones:60:0:AirPods",
            BatteryText.Encode([new("laptop", 87, true, "Desk"), new("headphones", 60, false, "AirPods")]));
        Assert.Equal(string.Empty, BatteryText.Encode([]));
        Assert.Equal("laptop:50:0:Café", BatteryText.Encode([new("laptop", 50, false, "Café")]));
        Assert.Equal("headphones/AirPods", OutputText.Encode("headphones", "AirPods"));
        Assert.Equal("speaker/Speakers", OutputText.Encode("speaker", "Speakers"));
    }

    [Fact]
    public void EncodeClampsAndFillsEmptyNamesAndKinds()
    {
        Assert.Equal("laptop:100:1:Desk|other:0:0:other",
            BatteryText.Encode([new("laptop", 150, true, "Desk"), new("bogus", -4, false, "")]));
    }

    [Fact]
    public void NamesAreSanitisedAndCut()
    {
        Assert.Equal("a b c", BatteryText.Encode([new("mouse", 5, false, "a|b\nc")]).Split(':', 4)[3]);
        var emoji = string.Concat(Enumerable.Repeat("\U0001F600", 30));
        var name = BatteryText.Encode([new("mouse", 5, false, emoji)]).Split(':', 4)[3];
        Assert.Equal(24, name.EnumerateRunes().Count());
        Assert.Equal(40, OutputText.Encode("headphones", new string('x', 60)).Split('/', 2)[1].Length);
        Assert.StartsWith("other/", OutputText.Encode("?", "x"));
    }

    [Fact]
    public void EntriesDropFromTheEndAndTheLaptopStays()
    {
        var entries = new List<BatteryEntry> { new("laptop", 80, false, "L") };
        for (var i = 0; i < 20; i++)
        {
            entries.Add(new("headphones", 50, false, new string('n', 24)));
        }

        var text = BatteryText.Encode(entries);
        Assert.True(Encoding.UTF8.GetByteCount(text) <= 255);
        Assert.StartsWith("laptop:80:0:L|", text);
        Assert.All(text.Split('|'), p => Assert.Equal(4, p.Split(':').Length));
        Assert.True(text.Split('|').Length < 21);
    }

    [Fact]
    public void ReporterSendsOnceThenOnlyOnChange()
    {
        var level = 50;
        var sent = new List<Text>();
        using var r = new PowerReporter(
            () => [new("laptop", level, false, "L")], () => ("speaker", "Speakers"), sent.Add, Never, Never);

        r.Poke();
        r.Poke();
        Assert.Equal([(byte)TextKind.Batteries, (byte)TextKind.Output], sent.Select(t => t.Kind));

        level = 49;
        r.Poke();
        Assert.Equal(3, sent.Count);
        Assert.Equal("laptop:49:0:L", sent[2].Value);
    }

    [Fact]
    public void ThrowingReadersAndSendsNeverEscape()
    {
        using var r = new PowerReporter(
            () => throw new InvalidOperationException(), () => throw new InvalidOperationException(),
            _ => throw new IOException(), Never, Never);
        r.Poke();
    }

    [Fact]
    public void StartSendsBothImmediately()
    {
        using var both = new CountdownEvent(2);
        using var r = new PowerReporter(() => [], () => null, _ => both.Signal(), Never, Never);
        r.Start();
        Assert.True(both.Wait(TimeSpan.FromSeconds(10)));
    }
}
