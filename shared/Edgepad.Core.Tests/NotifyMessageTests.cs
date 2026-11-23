using System.Linq;
using System.Text;
using Edgepad.Notify;
using Xunit;

namespace Edgepad.Tests;

public sealed class NotifyMessageTests
{
    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    [Fact]
    public void JoinsTheThreeFieldsWithNewlines()
    {
        Assert.Equal("App\nTitle\nBody", Text(NotifyMessage.Encode("App", "Title", "Body")!));
    }

    [Fact]
    public void ControlCharactersBecomeSpaces()
    {
        var bytes = NotifyMessage.Encode("A\nB", "T\tx", "b\r\u0001c");

        Assert.Equal("A B\nT x\nb  c", Text(bytes!));
    }

    [Fact]
    public void AppIsCutToFortyCodePoints()
    {
        var text = Text(NotifyMessage.Encode(new string('a', 50), "t", "b")!);

        Assert.Equal(new string('a', 40), text.Split('\n')[0]);
    }

    [Fact]
    public void SurrogatePairIsNeverSplit()
    {
        // 39 letters, then one emoji (two UTF-16 units, one code point), then a letter past the cap.
        var app = new string('a', 39) + "\U0001F600";
        var text = Text(NotifyMessage.Encode(app + "x", "t", "b")!);

        Assert.Equal(app, text.Split('\n')[0]);
    }

    [Fact]
    public void TotalStaysUnderMaxBytesWithFourByteText()
    {
        var emoji = "\U0001F600";
        var bytes = NotifyMessage.Encode(
            string.Concat(Enumerable.Repeat(emoji, 40)),
            string.Concat(Enumerable.Repeat(emoji, 60)),
            string.Concat(Enumerable.Repeat(emoji, 200)))!;

        Assert.True(bytes.Length < NotifyMessage.MaxBytes);
        Assert.DoesNotContain('�', Text(bytes));
    }

    [Fact]
    public void EmptyTitleAndBodyGiveNull()
    {
        Assert.Null(NotifyMessage.Encode("App", "", ""));
        Assert.NotNull(NotifyMessage.Encode("App", "", "x"));
    }
}
