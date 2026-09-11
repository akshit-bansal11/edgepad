using System.Globalization;
using Edgepad.Protocol;
using Xunit;

namespace Edgepad.Tests;

/// <summary>Runs protocol/frames.txt — the same golden frames the Android suite runs.</summary>
public sealed class FrameFixtureTests
{
    public static TheoryData<string> Lines()
    {
        var data = new TheoryData<string>();
        foreach (var line in File.ReadAllLines(FixturePath()))
        {
            if (line.Length > 0 && !line.StartsWith('#'))
            {
                data.Add(line);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Lines))]
    public void EncodesToTheGoldenBytes(string line)
    {
        var (frame, bytes) = Parse(line);
        Assert.Equal(bytes, FrameCodec.Encode(frame));
    }

    [Theory]
    [MemberData(nameof(Lines))]
    public void DecodesTheGoldenBytes(string line)
    {
        var (frame, bytes) = Parse(line);
        Assert.Equal(frame, FrameCodec.Decode(bytes[0], bytes.AsSpan(1)));
    }

    [Fact]
    public void RejectsAnUnknownType() =>
        Assert.Throws<InvalidDataException>(() => FrameCodec.Decode(0x7f, []));

    [Fact]
    public void RejectsHelloWithoutTheMagic() =>
        Assert.Throws<InvalidDataException>(() => FrameCodec.Decode(0x01, [0, 0, 0, 0, 1]));

    [Fact]
    public void RejectsAButtonFlagOtherThanZeroOrOne() =>
        Assert.Throws<InvalidDataException>(() => FrameCodec.Decode(0x11, [0, 2]));

    [Fact]
    public void RejectsAShortPayload() =>
        Assert.Throws<InvalidDataException>(() => FrameCodec.Decode(0x10, [1, 2]));

    private static (Frame Frame, byte[] Bytes) Parse(string line)
    {
        var halves = line.Split('=');
        var fields = halves[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var bytes = Convert.FromHexString(halves[1].Replace(" ", "", StringComparison.Ordinal));

        long Field(int i) => long.Parse(fields[i], CultureInfo.InvariantCulture);

        Frame frame = fields[0] switch
        {
            "HELLO" => new Hello((byte)Field(1)),
            "HELLO_ACK" => new HelloAck((byte)Field(1)),
            "MOVE" => new Move((short)Field(1), (short)Field(2)),
            "BUTTON" => new PointerButton((byte)Field(1), Field(2) == 1),
            "SCROLL" => new Scroll((short)Field(1), (short)Field(2)),
            "ZOOM" => new Zoom((short)Field(1)),
            "ACTION" => new RunAction((byte)Field(1)),
            "SET" => new SetValue((byte)Field(1), (byte)Field(2)),
            "PING" => new Ping(Field(1)),
            "PONG" => new Pong(Field(1)),
            "STATE" => new StateReport((byte)Field(1), (byte)Field(2), (byte)Field(3)),
            _ => throw new InvalidDataException($"Unknown fixture frame {fields[0]}"),
        };

        return (frame, bytes);
    }

    private static string FixturePath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "protocol", "frames.txt");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("protocol/frames.txt was not found above the test output directory");
    }
}
