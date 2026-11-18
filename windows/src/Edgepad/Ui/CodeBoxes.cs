using System.Drawing.Drawing2D;
using Edgepad.Net;

namespace Edgepad.Ui;

/// <summary>
/// The pairing code as one box per digit. One control, not a row of TextBoxes, so the keyboard and the screen reader
/// see a single field: its name is the code's, its value the digits typed so far. Typing fills the next box, Backspace
/// and Delete clear, Left and Right move, and a paste of "482 913" or "482-913" fills them all. Digits are cut by
/// <see cref="DigitInput.Normalize"/>, so every script's digits work and nothing else gets in.
/// </summary>
internal sealed class CodeBoxes : Control
{
    private const int BoxWidth = 44;
    private const int BoxHeight = 52;
    private const int Gap = 8;

    private readonly Palette palette;
    private string digits = "";
    private int cursor;

    public CodeBoxes(Palette palette)
    {
        this.palette = palette;
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick,
            true);
        TabStop = true;
        BackColor = palette.Card;
        AccessibleName = Strings.Pair_CodeName;
        AccessibleRole = AccessibleRole.Text;
        Cursor = Cursors.IBeam;
    }

    /// <summary>The digits typed so far, at most <see cref="LinkCrypto.CodeDigits"/>.</summary>
    public string Value => digits;

    /// <summary>Raised whenever <see cref="Value"/> changes, by typing, deleting or pasting.</summary>
    public event EventHandler? ValueChanged;

    /// <summary>The size the boxes take at <paramref name="dpi"/>.</summary>
    public static Size SizeFor(int dpi) => new(
        (Theme.Scale(BoxWidth, dpi) * LinkCrypto.CodeDigits) + (Theme.Scale(Gap, dpi) * (LinkCrypto.CodeDigits - 1)),
        Theme.Scale(BoxHeight, dpi));

    private void Set(string value, int at)
    {
        var changed = value != digits;
        digits = value;
        cursor = Math.Clamp(at, 0, digits.Length);
        Invalidate();
        if (changed)
        {
            AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Insert(string typed)
    {
        var room = LinkCrypto.CodeDigits - digits.Length;
        var add = DigitInput.Normalize(typed, room);
        if (add.Length > 0)
        {
            Set(digits.Insert(cursor, add), cursor + add.Length);
        }
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & ~Keys.Shift) is Keys.Left or Keys.Right or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Back when cursor > 0:
                Set(digits.Remove(cursor - 1, 1), cursor - 1);
                break;
            case Keys.Delete when cursor < digits.Length:
                Set(digits.Remove(cursor, 1), cursor);
                break;
            case Keys.Left:
                cursor = Math.Max(0, cursor - 1);
                Invalidate();
                break;
            case Keys.Right:
                cursor = Math.Min(digits.Length, cursor + 1);
                Invalidate();
                break;
            case Keys.Home:
                cursor = 0;
                Invalidate();
                break;
            case Keys.End:
                cursor = digits.Length;
                Invalidate();
                break;
            case Keys.V when e.Control:
            case Keys.Insert when e.Shift:
                Insert(Clipboard.ContainsText() ? Clipboard.GetText() : "");
                break;
            default:
                return;
        }

        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        if (!char.IsControl(e.KeyChar))
        {
            Insert(e.KeyChar.ToString());
        }

        e.Handled = true;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        var step = Theme.Scale(BoxWidth + Gap, DeviceDpi);
        cursor = Math.Clamp(e.X / Math.Max(1, step), 0, digits.Length);
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(palette.Card);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var boxW = Theme.Scale(BoxWidth, DeviceDpi);
        var boxH = Theme.Scale(BoxHeight, DeviceDpi);
        var step = boxW + Theme.Scale(Gap, DeviceDpi);
        var corner = Theme.Scale(8f, DeviceDpi);
        var thin = palette.HighContrast ? Theme.Scale(2f, DeviceDpi) : 1f;
        var active = Focused ? Math.Min(cursor, LinkCrypto.CodeDigits - 1) : -1;
        using var font = Theme.Font(Theme.Scale(26f, DeviceDpi), Weight.Bold);
        var ink = Enabled ? palette.Ink : palette.Off;

        for (var i = 0; i < LinkCrypto.CodeDigits; i++)
        {
            var box = new RectangleF((i * step) + 1, 1, boxW - 2, boxH - 2);
            using var shape = Theme.Rounded(box, corner);
            using (var fill = new SolidBrush(palette.Card))
            {
                g.FillPath(fill, shape);
            }

            var on = i == active;

            // Off, not Line: an empty box must show its edge (3:1), as Framed does.
            using var edge = new Pen(on ? palette.Accent : palette.Off, on ? Theme.Scale(2f, DeviceDpi) : thin);
            g.DrawPath(edge, shape);
            if (i < digits.Length)
            {
                TextRenderer.DrawText(
                    g,
                    digits[i].ToString(),
                    font,
                    Rectangle.Round(box),
                    ink,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }
        }
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new CodeAccessibleObject(this);

    /// <summary>One text field to a screen reader: the code's name, the digits as its value, the boxes not announced.</summary>
    private sealed class CodeAccessibleObject(CodeBoxes owner) : ControlAccessibleObject(owner)
    {
        public override string? Value => owner.digits;

        public override AccessibleRole Role => AccessibleRole.Text;
    }
}
