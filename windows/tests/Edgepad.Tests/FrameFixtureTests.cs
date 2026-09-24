using System.Globalization;
using Edgepad.Protocol;
using Xunit;

namespace Edgepad.Tests;

/// <summary>Runs protocol/frames.txt — the same golden frames the Android suite runs.</summary>
public sealed class FrameFixtureTests
{
    public static TheoryData<string> Lines() => [.. Fixtures.Lines("frames.txt")];

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

    [Fact]
    public void RejectsTextWhoseLengthDoesNotMatchItsHeader() =>
        Assert.Throws<InvalidDataException>(() => FrameCodec.Decode(0x41, [0, 3, 0x41]));

    [Fact]
    public void LongTextIsCutToTheLimitWithoutSplittingACharacter()
    {
        var text = new Text(0, new string('a', 254) + "é");
        var bytes = FrameCodec.Encode(text);
        Assert.Equal(1 + FrameCodec.TextHeaderLength + 254, bytes.Length);
        Assert.Equal(new Text(0, new string('a', 254)), FrameCodec.Decode(bytes[0], bytes.AsSpan(1)));
    }

    [Fact]
    public void PadStateKeepsSignedSticksSignedAndUnsignedFieldsUnsigned()
    {
        // The three mistakes that are invisible in hex: a stick read as a ushort comes back 32768 and not
        // -32768, a trigger read as an sbyte comes back -1 and not 255, and the button mask read as a short
        // turns Y, the top bit, negative. One frame holding all three at once, through both directions.
        var frame = new PadState(0xF7FF, 255, 255, short.MinValue, short.MaxValue, -1, 1);
        var bytes = FrameCodec.Encode(frame);
        Assert.Equal(1 + 12, bytes.Length);
        Assert.Equal(frame, FrameCodec.Decode(bytes[0], bytes.AsSpan(1)));
    }

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
            "TEXT" => new Text((byte)Field(1), fields.Length > 2 ? fields[2] : ""),
            "KEY" => new Key((ushort)Field(1), Field(2) == 1),
            "PAD_STATE" => new PadState(
                (ushort)Field(1),
                (byte)Field(2),
                (byte)Field(3),
                (short)Field(4),
                (short)Field(5),
                (short)Field(6),
                (short)Field(7)),
            _ => throw new InvalidDataException($"Unknown fixture frame {fields[0]}"),
        };

        return (frame, bytes);
    }
}
