using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Edgepad.Ui;

/// <summary>The design's three kinds of button.</summary>
internal enum ButtonKind
{
    /// <summary>Bordered, on the card: Cancel, Browse, Add macro.</summary>
    Outline,

    /// <summary>Filled in the accent: Save, and the empty state's Add macro.</summary>
    Primary,

    /// <summary>No border or fill until hovered: Remove, and the icon field's secondary action.</summary>
    Ghost,
}

/// <summary>
/// A shadcn-style button: 36 px tall, 8 px corners, bold 14 px text with an optional 16 px icon before it.
/// Still a Button, so Enter and Space press it, it can be the dialog's accept or cancel button, and a screen
/// reader meets a button; only its painting is replaced.
/// </summary>
internal sealed class FlatButton : Button
{
    private readonly Palette palette;
    private readonly ButtonKind kind;
    private readonly string? glyph;

    public FlatButton(string text, ButtonKind kind, Palette palette, string? glyph = null)
    {
        this.palette = palette;
        this.kind = kind;
        this.glyph = glyph;
        Text = text;
        BackColor = palette.Card;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    /// <summary>Drawn in the danger colour, for the one button that throws a macro away.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Danger { get; init; }

    /// <summary>The width that fits the text and icon with the design's padding, at <paramref name="dpi"/>.</summary>
    public int FitWidth(int dpi)
    {
        var text = TextRenderer.MeasureText(Text, Theme.Font(Theme.Scale(14f, dpi), Weight.Bold), Size.Empty, TextFormatFlags.NoPadding).Width;
        var icon = glyph is null ? 0 : Theme.Scale(16 + 6, dpi);
        return text + icon + Theme.Scale(kind == ButtonKind.Primary ? 36 : 24, dpi);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var dpi = DeviceDpi;
        var graphics = e.Graphics;
        graphics.Clear(palette.Card);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var hovered = Enabled && ClientRectangle.Contains(PointToClient(Cursor.Position));
        var radius = Theme.Scale(8f, dpi);
        var shape = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);

        var ink = kind == ButtonKind.Primary ? palette.OnAccent : Danger ? palette.Danger : palette.Ink;
        Color? fill = kind switch
        {
            ButtonKind.Primary => hovered ? Theme.Mix(palette.Accent, palette.Card, 0.12f) : palette.Accent,
            _ when hovered => palette.Faint,
            _ => null,
        };

        using (var path = Theme.Rounded(shape, radius))
        {
            if (fill is { } colour)
            {
                using var brush = new SolidBrush(colour);
                graphics.FillPath(brush, path);
            }

            if (kind == ButtonKind.Outline)
            {
                using var line = new Pen(palette.Line);
                graphics.DrawPath(line, path);
            }

            // The keyboard's position, only when the keyboard is what moved it: a mouse click needs no ring.
            if (Focused && ShowFocusCues)
            {
                using var ring = new Pen(kind == ButtonKind.Primary ? palette.Ink : palette.Accent, Theme.Scale(2f, dpi));
                using var inner = Theme.Rounded(RectangleF.Inflate(shape, -1, -1), radius);
                graphics.DrawPath(ring, inner);
            }
        }

        if (!Enabled)
        {
            ink = Theme.Mix(ink, fill ?? palette.Card, 0.55f);
        }

        var font = Theme.Font(Theme.Scale(14f, dpi), Weight.Bold);
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        var text = TextRenderer.MeasureText(graphics, Text, font, Size.Empty, flags);
        var icon = glyph is null ? 0 : Theme.Scale(16, dpi);
        var gap = glyph is null ? 0 : Theme.Scale(6, dpi);
        var x = (Width - icon - gap - text.Width) / 2;
        if (glyph is not null)
        {
            Icons.Draw(graphics, glyph, new RectangleF(x, (Height - icon) / 2f, icon, icon), ink);
        }

        TextRenderer.DrawText(graphics, Text, font, new Point(x + icon + gap, (Height - text.Height) / 2), ink, flags);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        Invalidate();
    }
}

/// <summary>
/// The design's bordered box round a native control: a rounded hairline, and a soft accent ring outside it while
/// the control has focus. The control inside is a borderless TextBox or ListBox, so typing, selection, the
/// caret, the clipboard and the accessibility tree are all still Windows' own.
///
/// The ring is drawn in a margin the frame keeps round the border, so a frame is <see cref="Ring"/> larger on
/// every side than the box the design draws; <see cref="Place"/> takes the design's box and allows for it.
/// </summary>
internal sealed class Framed : Panel
{
    private readonly Palette palette;
    private readonly int radius;
    private readonly int inset;

    public Framed(Control inner, Palette palette, int radius, int inset)
    {
        this.palette = palette;
        this.radius = radius;
        this.inset = inset;
        Inner = inner;
        BackColor = palette.Card;
        inner.BackColor = palette.Card;
        inner.ForeColor = palette.Ink;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        inner.GotFocus += (_, _) => Invalidate();
        inner.LostFocus += (_, _) => Invalidate();
        Controls.Add(inner);
    }

    public Control Inner { get; }

    /// <summary>The focus ring's width, and so the margin kept round the border for it.</summary>
    private int Ring => Theme.Scale(3, DeviceDpi);

    /// <summary>Puts the design's box at <paramref name="box"/>, with the inner control inset inside the border.</summary>
    public void Place(Rectangle box, int dpi)
    {
        var ring = Theme.Scale(3, dpi);
        Bounds = Rectangle.Inflate(box, ring, ring);

        var pad = Theme.Scale(inset, dpi);
        if (Inner is TextBox text)
        {
            // A single-line TextBox is as tall as its font says, whatever it is told, so it is centred instead.
            text.Font = Theme.Font(Theme.Scale(14f, dpi), Weight.Regular);
            Inner.SetBounds(ring + pad, ring + ((box.Height - text.PreferredHeight) / 2), box.Width - (pad * 2), text.PreferredHeight);
        }
        else
        {
            var edge = ring + pad;
            Inner.SetBounds(edge, edge, box.Width - (pad * 2), box.Height - (pad * 2));
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.Clear(palette.Card);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var ring = Ring;
        var focused = Inner.ContainsFocus;
        var border = new RectangleF(ring + 0.5f, ring + 0.5f, Width - (ring * 2) - 1, Height - (ring * 2) - 1);
        var corner = Theme.Scale((float)radius, DeviceDpi);
        if (focused)
        {
            using var glow = new SolidBrush(Color.FromArgb(palette.IsDark ? 64 : 46, palette.Accent));
            using var outer = Theme.Rounded(RectangleF.Inflate(border, ring, ring), corner + ring);
            graphics.FillPath(glow, outer);
        }

        using var shape = Theme.Rounded(border, corner);
        using (var card = new SolidBrush(palette.Card))
        {
            graphics.FillPath(card, shape);
        }

        using var line = new Pen(focused ? palette.Accent : palette.Line);
        graphics.DrawPath(line, shape);
    }

    /// <summary>A click on the padding round the box still means the box.</summary>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Inner.Focus();
    }
}

/// <summary>A faint rounded tile holding a picture: the icon field's preview, and the empty state's badge.</summary>
internal sealed class Tile : Control
{
    private readonly Palette palette;

    public Tile(Palette palette)
    {
        this.palette = palette;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
    }

    /// <summary>A picture to show, which wins over <see cref="Glyph"/>. Not owned: the caller disposes it.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Image? Picture
    {
        get;
        set
        {
            field = value;
            Invalidate();
        }
    }

    /// <summary>A line icon to show when there is no picture.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? Glyph { get; init; }

    /// <summary>The picture's share of the tile: 24 of 36 in the icon field, 22 of 48 in the empty state.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float Share { get; init; } = 24f / 36f;

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.Clear(palette.Card);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

        using (var faint = new SolidBrush(palette.Faint))
        using (var shape = Theme.Rounded(new RectangleF(0, 0, Width, Height), Theme.Scale(Width > Theme.Scale(40, DeviceDpi) ? 10f : 8f, DeviceDpi)))
        {
            graphics.FillPath(faint, shape);
        }

        var size = Width * Share;
        var box = new RectangleF((Width - size) / 2, (Height - size) / 2, size, size);
        if (Picture is not null)
        {
            graphics.DrawImage(Picture, box);
        }
        else if (Glyph is not null)
        {
            Icons.Draw(graphics, Glyph, box, palette.Ink);
        }
    }
}
