using System.Drawing;
using Edgepad.Ui;
using Xunit;

namespace Edgepad.Tests;

public sealed class CodeBoxesTests
{
    /// <summary>
    /// 5.0.0 draft 77 drew the pairing code as a red X: the boxes disposed the font <see cref="Theme.Font"/> shares, so
    /// every paint after the first threw, and every other user of that size broke with it.
    /// </summary>
    [Fact]
    public void PaintingTwiceLeavesTheSharedFontAlive()
    {
        using var boxes = new CodeBoxes(Palette.Light) { Size = CodeBoxes.SizeFor(96) };
        using var bitmap = new Bitmap(boxes.Width, boxes.Height);
        var area = new Rectangle(Point.Empty, boxes.Size);
        boxes.DrawToBitmap(bitmap, area);
        boxes.DrawToBitmap(bitmap, area);

        var font = Theme.Font(Theme.Scale(26f, boxes.DeviceDpi), Weight.Bold);
        Assert.True(font.GetHeight() > 0);
    }
}
