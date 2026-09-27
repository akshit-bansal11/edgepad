using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Edgepad.Ui;

/// <summary>
/// The tray menu, drawn as the design's dropdown: a status header, then rows with a muted icon, a label and a
/// hint, hairline separators and a destructive item. It stays a ContextMenuStrip underneath, so keyboard
/// navigation, focus, the accessibility tree and the shell's own show-and-dismiss handling are all Windows'
/// rather than reimplemented; only the painting and the measurements are this class's.
///
/// Every row sizes itself here rather than through the menu's own layout. ToolStripDropDownMenu measures its
/// items from their text alone and gives them all one height, which a 62 px header and 34 px rows cannot share,
/// so each item is set to a fixed size and the menu reports its own.
/// </summary>
internal sealed class TrayMenu : ContextMenuStrip
{
    // The design's measurements at 96 dpi.
    private const int Width96 = 300;
    private const int Inset = 4;
    private const int RowHeight = 34;
    private const int HeaderHeight = 62;
    private const int SeparatorHeight = 9;

    private bool rounded;

    public TrayMenu()
    {
        ShowImageMargin = false;
        ShowCheckMargin = false;
        Renderer = new MenuRenderer();
        Items.Add(Header);
    }

    public HeaderRow Header { get; } = new();

    /// <summary>The colours of the last open. Read by the rows as they paint.</summary>
    public Palette Palette { get; private set; } = Palette.Current;

    /// <summary>
    /// The one-pixel border plus the design's four-pixel inset. The base class resets Padding to this on every
    /// layout, which is why it is an override and not an assignment.
    /// </summary>
    protected override Padding DefaultPadding => new(Theme.Scale(Inset + 1, DeviceDpi));

    /// <summary>Picks up the current theme and sizes every row for the monitor. Called as the menu opens.</summary>
    public void Restyle()
    {
        Palette = Palette.Current;
        if (rounded)
        {
            Theme.RoundCorners(Handle, Palette.Line);
        }

        var width = Theme.Scale(Width96 - ((Inset + 1) * 2), DeviceDpi);
        foreach (ToolStripItem item in Items)
        {
            item.AutoSize = false;
            item.Margin = Padding.Empty;
            item.Size = new Size(width, Theme.Scale(
                item switch
                {
                    HeaderRow => HeaderHeight,
                    ToolStripSeparator => SeparatorHeight,
                    _ => RowHeight,
                },
                DeviceDpi));
        }

        Padding = DefaultPadding;
        PerformLayout();
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var height = Padding.Vertical;
        foreach (ToolStripItem item in Items)
        {
            if (item.Available)
            {
                height += item.Height + item.Margin.Vertical;
            }
        }

        return new Size(Theme.Scale(Width96, DeviceDpi), height);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        rounded = Theme.RoundCorners(Handle, Palette.Line);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Header.Animate(true);
    }

    protected override void OnClosed(ToolStripDropDownClosedEventArgs e)
    {
        // Nothing is on screen to animate, so the timer stops rather than ticking behind a closed menu.
        Header.Animate(false);
        base.OnClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Header.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// The card, its border and the separators. Separators are drawn from here rather than by themselves
    /// because the design runs them to the border, through the inset an item cannot paint outside of.
    /// </summary>
    private sealed class MenuRenderer : ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is not TrayMenu menu)
            {
                return;
            }

            using var card = new SolidBrush(menu.Palette.Card);
            e.Graphics.FillRectangle(card, new Rectangle(Point.Empty, menu.Size));

            using var line = new Pen(menu.Palette.Line);
            foreach (ToolStripItem item in menu.Items)
            {
                if (item is ToolStripSeparator && item.Available)
                {
                    var y = item.Bounds.Top + (item.Height / 2);
                    e.Graphics.DrawLine(line, 0, y, menu.Width, y);
                }
            }
        }

        /// <summary>
        /// Square, and drawn on Windows 11 too: there the system's rounded border in the same colour lies over
        /// its straight runs, and the corners it cuts away were never visible.
        /// </summary>
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is TrayMenu menu)
            {
                using var line = new Pen(menu.Palette.Line);
                e.Graphics.DrawRectangle(line, 0, 0, menu.Width - 1, menu.Height - 1);
            }
        }
    }
}

/// <summary>A separator that paints nothing itself: <see cref="TrayMenu"/> draws the line across the whole card.</summary>
internal sealed class MenuSeparator : ToolStripSeparator
{
    protected override void OnPaint(PaintEventArgs e)
    {
    }
}

/// <summary>
/// One row of the tray menu: a 16 px muted icon, the label, and on the right either a dim hint or a switch.
/// Still a ToolStripMenuItem, so it is a menu item to the keyboard and to a screen reader — the switch row
/// reports checked and unchecked through <see cref="ToolStripMenuItem.Checked"/> like any checkable item.
/// </summary>
internal sealed class MenuRow(string text, string glyph) : ToolStripMenuItem(text)
{
    /// <summary>How far a disabled row fades toward the card, as the design's 45% opacity does.</summary>
    private const float Disabled = 0.55f;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? Hint { get; set; }

    /// <summary>Drawn in the danger colour: the one row that throws something away.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Danger { get; init; }

    /// <summary>Drawn as a switch showing <see cref="ToolStripMenuItem.Checked"/> in place of a hint.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Switch { get; init; }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Owner is not TrayMenu menu)
        {
            base.OnPaint(e);
            return;
        }

        var palette = menu.Palette;
        var dpi = menu.DeviceDpi;
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        // Selected is both the hover and the keyboard's position, so the one highlight serves as the focus cue.
        if (Selected && Enabled)
        {
            using var faint = new SolidBrush(palette.Faint);
            using var highlight = Theme.Rounded(new RectangleF(0, 0, Width, Height), Theme.Scale(6f, dpi));
            graphics.FillPath(faint, highlight);
        }

        var ink = Danger ? palette.Danger : palette.Ink;
        var muted = Danger ? palette.Danger : palette.Dim;
        if (!Enabled)
        {
            ink = Theme.Mix(ink, palette.Card, Disabled);
            muted = Theme.Mix(muted, palette.Card, Disabled);
        }

        var pad = Theme.Scale(8, dpi);
        var icon = Theme.Scale(16, dpi);
        Icons.Draw(graphics, glyph, new RectangleF(pad, (Height - icon) / 2f, icon, icon), muted);

        var left = pad + icon + Theme.Scale(10, dpi);
        var right = Width - pad;
        if (Switch)
        {
            right -= DrawSwitch(graphics, palette, dpi, right) + pad;
        }
        else if (!string.IsNullOrEmpty(Hint))
        {
            var hintFont = Theme.Font(Theme.Scale(12f, dpi), Weight.Regular);
            var hintWidth = TextRenderer.MeasureText(graphics, Hint, hintFont, Size.Empty, TextFormatFlags.NoPadding).Width;
            TextRenderer.DrawText(
                graphics, Hint, hintFont, new Rectangle(right - hintWidth, 0, hintWidth, Height), muted, Line | TextFormatFlags.Right);
            right -= hintWidth + pad;
        }

        TextRenderer.DrawText(
            graphics, Text, Theme.Font(Theme.Scale(14f, dpi), Weight.Regular), new Rectangle(left, 0, right - left, Height), ink, Line);
    }

    private const TextFormatFlags Line =
        TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

    /// <summary>The design's 32 by 18 switch, right-aligned at <paramref name="right"/>. Returns its width.</summary>
    private int DrawSwitch(Graphics graphics, Palette palette, int dpi, int right)
    {
        var width = Theme.Scale(32f, dpi);
        var height = Theme.Scale(18f, dpi);
        var track = new RectangleF(right - width, (Height - height) / 2f, width, height);
        using (var fill = new SolidBrush(Checked ? palette.Accent : palette.Off))
        using (var shape = Theme.Rounded(track, height / 2))
        {
            graphics.FillPath(fill, shape);
        }

        var knob = Theme.Scale(16f, dpi);
        var inset = Theme.Scale(1f, dpi);
        var x = Checked ? track.Right - inset - knob : track.Left + inset;
        using (var shadow = new SolidBrush(Color.FromArgb(64, 0, 0, 0)))
        {
            graphics.FillEllipse(shadow, x, track.Top + inset + inset, knob, knob);
        }

        graphics.FillEllipse(Brushes.White, x, track.Top + inset, knob, knob);
        return (int)Math.Ceiling(width);
    }
}

/// <summary>
/// The header: a phone tile, a headline with a Trusted or Waiting badge, and the status line under it. While
/// the laptop waits for a phone, rings ripple out from the tile — the design's one decorative animation, run
/// only while the menu is open and only when Windows' animations are on.
/// </summary>
internal sealed class HeaderRow : ToolStripMenuItem
{
    /// <summary>One ring's journey outward, and the gap between rings, as the design's CSS keyframes have them.</summary>
    private const double RipplePeriodMs = 2400;
    private const double RippleStaggerMs = 800;
    private const int Rings = 3;

    private readonly System.Windows.Forms.Timer ripple = new() { Interval = 33 };
    private long startedAt;

    public HeaderRow()
    {
        // Not a command, so the keyboard passes over it; a screen reader still reads its name and description.
        Enabled = false;
        ripple.Tick += (_, _) => Invalidate();
        SetStatus("Starting…");
    }

    public TrayStatus Status { get; private set; } = TrayStatus.Describe(string.Empty);

    /// <summary>Takes a status line from the Bluetooth side, already marshalled onto the UI thread.</summary>
    public void SetStatus(string status)
    {
        Status = TrayStatus.Describe(status);
        Text = Status.Headline;
        AccessibleDescription = $"{Status.Badge}. {Status.Line}";
        if (!Status.Waiting)
        {
            ripple.Stop();
        }
        else if (Owner is { Visible: true })
        {
            Animate(true);
        }

        Invalidate();
    }

    /// <summary>Starts the ripple when it should run — waiting, animations on — and stops it otherwise.</summary>
    public void Animate(bool open)
    {
        if (open && Status.Waiting && Theme.AnimationsEnabled)
        {
            if (!ripple.Enabled)
            {
                startedAt = Environment.TickCount64;
                ripple.Start();
            }
        }
        else
        {
            ripple.Stop();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Owner is not TrayMenu menu)
        {
            base.OnPaint(e);
            return;
        }

        var palette = menu.Palette;
        var dpi = menu.DeviceDpi;
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var size = Theme.Scale(40f, dpi);
        var tile = new RectangleF(Theme.Scale(8f, dpi), Theme.Scale(10f, dpi), size, size);
        if (ripple.Enabled)
        {
            DrawRipple(graphics, palette, dpi, tile);
        }

        var waiting = Status.Waiting;
        using (var fill = new SolidBrush(waiting ? palette.AccentSoft : palette.Accent))
        using (var shape = Theme.Rounded(tile, Theme.Scale(10f, dpi)))
        {
            graphics.FillPath(fill, shape);
        }

        var icon = Theme.Scale(20f, dpi);
        Icons.Draw(
            graphics,
            Icons.Smartphone,
            new RectangleF(tile.X + ((size - icon) / 2), tile.Y + ((size - icon) / 2), icon, icon),
            waiting ? palette.Accent : palette.OnAccent);

        var left = (int)(tile.Right + Theme.Scale(12f, dpi));
        var right = Width - Theme.Scale(8, dpi);
        var headlineFont = Theme.Font(Theme.Scale(15f, dpi), Weight.Bold);
        var statusFont = Theme.Font(Theme.Scale(13f, dpi), Weight.Regular);
        var gap = Theme.Scale(3, dpi);
        var top = (int)(tile.Top + ((size - headlineFont.Height - gap - statusFont.Height) / 2));

        const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        var headline = TextRenderer.MeasureText(graphics, Status.Headline, headlineFont, Size.Empty, flags);
        TextRenderer.DrawText(graphics, Status.Headline, headlineFont, new Point(left, top), palette.Ink, flags);

        // The badge: 20 px tall, 7 px either side of its word, 8 px after the headline.
        var badgeFont = Theme.Font(Theme.Scale(12f, dpi), Weight.Bold);
        var word = TextRenderer.MeasureText(graphics, Status.Badge, badgeFont, Size.Empty, flags);
        var badgeHeight = Theme.Scale(20f, dpi);
        var badge = new RectangleF(
            left + headline.Width + Theme.Scale(8f, dpi),
            top + ((headline.Height - badgeHeight) / 2),
            word.Width + Theme.Scale(14f, dpi),
            badgeHeight);
        using (var fill = new SolidBrush(waiting ? palette.Faint : palette.AccentSoft))
        using (var shape = Theme.Rounded(badge, Theme.Scale(6f, dpi)))
        {
            graphics.FillPath(fill, shape);
        }

        TextRenderer.DrawText(
            graphics,
            Status.Badge,
            badgeFont,
            Rectangle.Round(badge),
            waiting ? palette.Dim : palette.Accent,
            flags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        TextRenderer.DrawText(
            graphics,
            Status.Line,
            statusFont,
            new Rectangle(left, top + headlineFont.Height + gap, right - left, statusFont.Height),
            palette.Dim,
            flags | TextFormatFlags.EndEllipsis);
    }

    /// <summary>
    /// Three rings, each growing from 0.6 to 1.9 times a 44 px circle and fading as it goes, a third of a period
    /// apart. The rings are clipped to this row where the design lets them spill over the menu's edge; by the
    /// time one reaches the edge it is nearly transparent, so little of it is lost.
    /// </summary>
    // ponytail: a cubic ease-out stands in for the design's cubic-bezier(.2,.6,.3,1); they differ by a few pixels.
    private void DrawRipple(Graphics graphics, Palette palette, int dpi, RectangleF tile)
    {
        var elapsed = Environment.TickCount64 - startedAt;
        var centre = new PointF(tile.X + (tile.Width / 2), tile.Y + (tile.Height / 2));
        using var pen = new Pen(palette.Accent, Theme.Scale(1.5f, dpi));
        for (var ring = 0; ring < Rings; ring++)
        {
            var since = elapsed - (ring * RippleStaggerMs);
            if (since < 0)
            {
                continue;
            }

            var t = since % RipplePeriodMs / RipplePeriodMs;
            var eased = 1 - Math.Pow(1 - t, 3);
            var radius = Theme.Scale(22f, dpi) * (float)(0.6 + (1.3 * eased));
            pen.Color = Color.FromArgb((int)(255 * 0.55 * (1 - eased)), palette.Accent);
            graphics.DrawEllipse(pen, centre.X - radius, centre.Y - radius, radius * 2, radius * 2);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ripple.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>What the header says for a status line from the Bluetooth side.</summary>
internal sealed record TrayStatus(string Headline, string Badge, string Line, bool Waiting)
{
    /// <summary>
    /// The one status that means a phone is here, as Session words it. A connected phone has passed the trust
    /// check to get that far, which is what lets the badge say Trusted rather than merely Connected.
    /// </summary>
    private const string ConnectedPrefix = "Connected to ";

    public static TrayStatus Describe(string status) =>
        status.StartsWith(ConnectedPrefix, StringComparison.Ordinal)
            ? new TrayStatus("Phone connected", "Trusted", $"{status[ConnectedPrefix.Length..]} · over Bluetooth", Waiting: false)
            : new TrayStatus("No phone yet", "Waiting", status, Waiting: true);
}
