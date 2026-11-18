using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;
using Edgepad.Blob;

namespace Edgepad.Ui;

/// <summary>What the panel needs from the tray: each callback runs on the UI thread, none is given the panel.</summary>
internal sealed record ClipPanelHost(
    bool SharingOn,
    Func<bool> PhoneConnected,
    Action<ClipEntry> Paste,
    Func<ClipEntry, Task<bool>> SendToPhone,
    Action<ClipEntry> Remove,
    Action ClearAll);

/// <summary>Where the panel opens. Pure, so a test needs no screen.</summary>
internal static class ClipPanelPlacement
{
    private const int Offset = 8;

    /// <summary>
    /// The panel's top-left: just right of and below <paramref name="cursor"/>, flipped to the left of or above it when it
    /// would run past <paramref name="work"/>, then held <paramref name="margin"/> inside it. A panel too big to fit
    /// sits at the work area's origin plus the margin.
    /// </summary>
    public static Point Place(Point cursor, Size panel, Rectangle work, int margin)
    {
        var x = cursor.X + Offset;
        if (x + panel.Width > work.Right - margin)
        {
            x = cursor.X - Offset - panel.Width;
        }

        var y = cursor.Y + Offset;
        if (y + panel.Height > work.Bottom - margin)
        {
            y = cursor.Y - Offset - panel.Height;
        }

        return new Point(
            Fit(x, work.Left + margin, work.Right - margin - panel.Width),
            Fit(y, work.Top + margin, work.Bottom - margin - panel.Height));
    }

    /// <summary>Clamps, and when the range is inverted the low end wins: the top-left corner stays reachable.</summary>
    private static int Fit(int value, int low, int high) => Math.Max(low, Math.Min(value, high));
}

/// <summary>
/// The clipboard history, opened at the cursor by Win+Alt+V or the tray row: newest copy first, Enter or a click picks
/// one, Ctrl+Enter or the phone button sends it to the phone, Delete forgets it, Esc or a click elsewhere closes. It
/// takes the keyboard, so it is a real window, unlike the level readout. The text it shows is never logged.
/// </summary>
internal sealed class ClipboardPanel : Form
{
    private const int Width96 = 360;
    private const int MaxHeight96 = 440;
    private const int HeaderHeight96 = 44;
    private const int Pad96 = 8;
    private const int Gap96 = 4;
    private const int EmptyHeight96 = 200;
    private const int WsExToolWindow = 0x00000080;

    private readonly Palette palette = Palette.Current;
    private readonly ClipPanelHost host;
    private readonly List<ClipEntry> items;
    private readonly List<ClipRow> rows = [];
    private readonly ToolTip tip = new();
    private readonly Label title;
    private readonly FlatButton clearAll;
    private readonly Panel body;
    private bool keyboard;
    private int selected;

    public ClipboardPanel(IReadOnlyList<ClipEntry> items, ClipPanelHost host)
    {
        this.host = host;
        this.items = [.. items];

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = palette.Card;
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.None;
        Text = Strings.Clip_Title;
        AccessibleName = Strings.Clip_Title;

        title = new Label
        {
            Text = Strings.Clip_Title,
            BackColor = palette.Card,
            ForeColor = palette.Ink,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            UseMnemonic = false,
            RightToLeft = Theme.Direction,
        };
        clearAll = new FlatButton(Strings.Clip_ClearAll, ButtonKind.Ghost, palette);
        clearAll.Click += (_, _) => ClearAll();
        body = new Panel
        {
            AutoScroll = true,
            BackColor = palette.Card,
            AccessibleRole = AccessibleRole.List,
            AccessibleName = Strings.Clip_Title,
        };
        Controls.AddRange([title, clearAll, body]);
        Build();
    }

    /// <summary>A tool window: out of Alt-Tab, and it does not leave a taskbar button when it takes focus.</summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExToolWindow;
            return parameters;
        }
    }

    /// <summary>Text cut to what a row shows: runs of blanks become one space, blank lines go, three lines of 240 characters at most.</summary>
    internal static string Preview(string text)
    {
        const int MaxChars = 240;
        var lines = new List<string>(3);
        var more = false;
        foreach (var raw in text.Replace("\r", string.Empty).Split('\n'))
        {
            var line = Collapse(raw);
            if (line.Length == 0)
            {
                continue;
            }

            if (lines.Count == 3)
            {
                more = true;
                break;
            }

            lines.Add(line);
        }

        var joined = string.Join('\n', lines);
        if (joined.Length > MaxChars)
        {
            var cut = MaxChars - 1;
            if (char.IsHighSurrogate(joined[cut - 1]))
            {
                cut--;
            }

            return joined[..cut].TrimEnd() + "…";
        }

        return more ? joined + "…" : joined;
    }

    private static string Collapse(string line)
    {
        var sb = new StringBuilder(line.Length);
        var space = false;
        foreach (var c in line)
        {
            if (char.IsWhiteSpace(c))
            {
                space = sb.Length > 0;
                continue;
            }

            if (space)
            {
                sb.Append(' ');
                space = false;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static readonly CompositeFormat MetaFormat = CompositeFormat.Parse(Strings.Clip_Meta);
    private static readonly CompositeFormat MinutesFormat = CompositeFormat.Parse(Strings.Clip_MinutesAgo);
    private static readonly CompositeFormat HoursFormat = CompositeFormat.Parse(Strings.Clip_HoursAgo);
    private static readonly CompositeFormat DaysFormat = CompositeFormat.Parse(Strings.Clip_DaysAgo);

    private static string Meta(ClipEntry entry)
    {
        var age = RelativeAge.Of(DateTimeOffset.Now - entry.At);
        var culture = CultureInfo.CurrentCulture;
        var when = age.Unit switch
        {
            AgeUnit.Minutes => string.Format(culture, MinutesFormat, age.Count),
            AgeUnit.Hours => string.Format(culture, HoursFormat, age.Count),
            AgeUnit.Days => string.Format(culture, DaysFormat, age.Count),
            _ => Strings.Clip_JustNow,
        };
        return string.Format(culture, MetaFormat, entry.Side == ClipSide.Laptop ? Strings.Clip_FromLaptop : Strings.Clip_FromPhone, when);
    }

    /// <summary>Lays the whole panel out for the current dpi and the items left: header, then the rows or the empty state.</summary>
    private void Build()
    {
        var dpi = DeviceDpi;
        var width = Theme.Scale(Width96, dpi);
        var headerH = Theme.Scale(HeaderHeight96, dpi);
        var pad = Theme.Scale(Pad96, dpi);
        var gap = Theme.Scale(Gap96, dpi);
        var maxBody = Theme.Scale(MaxHeight96, dpi) - headerH;

        SuspendLayout();
        foreach (var row in rows)
        {
            row.Dispose();
        }

        rows.Clear();
        body.Controls.Clear();
        body.AutoScrollPosition = Point.Empty;

        var empty = items.Count == 0;
        int bodyH;
        if (empty)
        {
            bodyH = Theme.Scale(EmptyHeight96, dpi);
            body.Controls.Add(new EmptyState(palette, host.SharingOn) { Bounds = new Rectangle(0, 0, width, bodyH) });
        }
        else
        {
            var connected = host.PhoneConnected();
            var fullWidth = width - (2 * pad);
            var rowWidth = fullWidth;
            int content;
            while (true)
            {
                foreach (var row in rows)
                {
                    row.Dispose();
                }

                rows.Clear();
                var y = pad;
                foreach (var entry in items)
                {
                    var row = new ClipRow(entry, Meta(entry), palette, connected, tip);
                    row.Picked += (_, _) => Pick(row);
                    row.SendRequested += (_, _) => Send(row);
                    row.Hovered += (_, _) =>
                    {
                        keyboard = false;
                        SelectRow(rows.IndexOf(row));
                    };
                    var height = row.Fit(rowWidth);
                    row.Bounds = new Rectangle(pad, y, rowWidth, height);
                    rows.Add(row);
                    y += height + gap;
                }

                content = y - gap + pad;
                if (content <= maxBody || rowWidth < fullWidth)
                {
                    break;
                }

                // A scrollbar will take its share of the width, which can wrap a line more: lay out again once.
                rowWidth = fullWidth - SystemInformation.VerticalScrollBarWidth;
            }

            bodyH = Math.Min(content, maxBody);
            body.Controls.AddRange([.. rows]);
            selected = Math.Clamp(selected, 0, rows.Count - 1);
            rows[selected].IsSelected = true;
        }

        clearAll.Visible = !empty;
        var button = new Size(clearAll.FitWidth(dpi), Theme.Scale(36, dpi));
        title.Font = Theme.Font(Theme.Scale(14f, dpi), Weight.Bold);
        var headerBox = new Rectangle(0, 0, width, headerH);
        title.Bounds = Theme.Flip(new Rectangle(pad + pad, 0, width - (4 * pad) - button.Width, headerH), headerBox);
        clearAll.Bounds = Theme.Flip(new Rectangle(width - (2 * pad) - button.Width, (headerH - button.Height) / 2, button.Width, button.Height), headerBox);
        body.Bounds = new Rectangle(0, headerH, width, bodyH);
        ClientSize = new Size(width, headerH + bodyH);
        ResumeLayout(true);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.RoundCorners(Handle, palette.Line);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        Reveal.Window(this);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Build();
    }

    /// <summary>A click elsewhere ends the panel, like a menu. A send already under way carries on without it.</summary>
    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (!IsDisposed && !Disposing)
        {
            Close();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            tip.Dispose();
        }

        base.Dispose(disposing);
    }

    private void SelectRow(int index)
    {
        if (rows.Count == 0 || index < 0 || index >= rows.Count)
        {
            return;
        }

        if (index != selected)
        {
            rows[selected].IsSelected = false;
            selected = index;
        }

        rows[selected].IsSelected = true;
        rows[selected].KeyboardRing = keyboard;
        if (keyboard)
        {
            body.ScrollControlIntoView(rows[selected]);
        }
    }

    private void Pick(ClipRow row)
    {
        var at = rows.IndexOf(row);
        if (at >= 0)
        {
            host.Paste(items[at]);
        }
    }

    private void Send(ClipRow row)
    {
        var at = rows.IndexOf(row);
        if (at >= 0)
        {
            _ = host.SendToPhone(items[at]);
        }
    }

    private void RemoveSelected()
    {
        if (rows.Count == 0)
        {
            return;
        }

        host.Remove(items[selected]);
        items.RemoveAt(selected);
        Build();
        keyboard = true;
        SelectRow(selected);
    }

    private void ClearAll()
    {
        host.ClearAll();
        items.Clear();
        Build();
    }

    /// <summary>Keys go to the panel whichever control has focus; Enter and Space stay with the Clear all button when it has it.</summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var onButton = ActiveControl is Button;
        switch (keyData)
        {
            case Keys.Escape:
                Close();
                return true;
            case Keys.Down or Keys.Up or Keys.Home or Keys.End when rows.Count > 0:
                keyboard = true;
                SelectRow(keyData switch
                {
                    Keys.Down => Math.Min(selected + 1, rows.Count - 1),
                    Keys.Up => Math.Max(selected - 1, 0),
                    Keys.Home => 0,
                    _ => rows.Count - 1,
                });
                return true;
            case Keys.Enter when rows.Count > 0 && !onButton:
                host.Paste(items[selected]);
                return true;
            case Keys.Control | Keys.Enter when rows.Count > 0:
                _ = host.SendToPhone(items[selected]);
                return true;
            case Keys.Delete when rows.Count > 0:
                RemoveSelected();
                return true;
            default:
                return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    /// <summary>The list's empty state: the clipboard icon, what is missing and, when sharing is off, how to turn it on.</summary>
    private sealed class EmptyState : Control
    {
        private readonly Palette palette;
        private readonly string hint;

        public EmptyState(Palette palette, bool sharingOn)
        {
            this.palette = palette;
            hint = sharingOn ? Strings.Clip_EmptyHint : Strings.Clip_EmptyOff;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = palette.Card;
            AccessibleRole = AccessibleRole.StaticText;
            AccessibleName = Strings.Clip_Empty + ". " + hint;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var dpi = DeviceDpi;
            var graphics = e.Graphics;
            graphics.Clear(palette.Card);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var icon = Theme.Scale(32, dpi);
            var top = Theme.Scale(32, dpi);
            Icons.Draw(graphics, Icons.Clipboard, new RectangleF((Width - icon) / 2f, top, icon, icon), palette.Dim);

            const TextFormatFlags centred = TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak;
            var headFont = Theme.Font(Theme.Scale(14f, dpi), Weight.Bold);
            var headY = top + icon + Theme.Scale(12, dpi);
            var head = new Rectangle(0, headY, Width, headFont.Height);
            TextRenderer.DrawText(graphics, Strings.Clip_Empty, headFont, head, palette.Ink, centred | Theme.Reading);

            var hintFont = Theme.Font(Theme.Scale(12f, dpi), Weight.Regular);
            var hintWidth = Math.Min(Theme.Scale(280, dpi), Width);
            var hintBox = new Rectangle((Width - hintWidth) / 2, head.Bottom + Theme.Scale(4, dpi), hintWidth, Height - head.Bottom);
            TextRenderer.DrawText(graphics, hint, hintFont, hintBox, palette.Dim, centred | Theme.Reading);
        }
    }

    /// <summary>
    /// One copied text: a preview of up to three lines, then where it came from and how long ago. Custom-drawn like
    /// <see cref="CodeBoxes"/>, but a real control so a screen reader meets a list item, not a picture. Never focused
    /// itself: the panel owns the keyboard and tells the row whether it is the selected one.
    /// </summary>
    private sealed class ClipRow : Control
    {
        private const int PadX = 12;
        private const int PadY = 12;
        private const int LineHeight = 18;
        private const int SendSize = 28;
        private const int SendGap = 8;

        private readonly ClipEntry entry;
        private readonly string preview;
        private readonly string meta;
        private readonly Palette palette;
        private readonly bool canSend;
        private readonly ToolTip tip;
        private int lines = 1;
        private bool selected;
        private bool ring;
        private bool overSend;

        public ClipRow(ClipEntry entry, string meta, Palette palette, bool canSend, ToolTip tip)
        {
            this.entry = entry;
            this.meta = meta;
            this.palette = palette;
            this.canSend = canSend;
            this.tip = tip;
            preview = Preview(entry.Text);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.StandardClick, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            BackColor = palette.Card;
            AccessibleRole = AccessibleRole.ListItem;
            AccessibleName = preview.Replace('\n', ' ') + ". " + meta;
        }

        public event EventHandler? Picked;

        public event EventHandler? SendRequested;

        public event EventHandler? Hovered;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsSelected
        {
            get => selected;
            set
            {
                if (selected == value)
                {
                    return;
                }

                selected = value;
                Invalidate();
                if (value)
                {
                    AccessibilityNotifyClients(AccessibleEvents.Focus, -1);
                }
            }
        }

        /// <summary>The focus ring, drawn only once the keyboard is what moved the selection.</summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool KeyboardRing
        {
            set
            {
                if (ring != value)
                {
                    ring = value;
                    Invalidate();
                }
            }
        }

        private int Pitch(int dpi) => Math.Max(Theme.Scale(LineHeight, dpi), PreviewFont(dpi).Height);

        private static Font PreviewFont(int dpi) => Theme.Font(Theme.Scale(13f, dpi), Weight.Regular);

        private static TextFormatFlags TextFlags => TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | Theme.Start;

        private int TextWidth(int width, int dpi) =>
            width - (2 * Theme.Scale(PadX, dpi)) - (canSend ? Theme.Scale(SendSize + SendGap, dpi) : 0);

        /// <summary>Sizes the row for <paramref name="width"/> and returns its height: the text's lines, its meta line, and the padding.</summary>
        public int Fit(int width)
        {
            var dpi = DeviceDpi;
            var font = PreviewFont(dpi);
            var measured = TextRenderer.MeasureText(preview, font, new Size(TextWidth(width, dpi), int.MaxValue), TextFlags).Height;
            lines = Math.Clamp((int)Math.Ceiling(measured / (double)font.Height), 1, 3);
            return (2 * Theme.Scale(PadY, dpi)) + (lines * Pitch(dpi)) + Theme.Scale(LineHeight, dpi);
        }

        private Rectangle SendRect(int dpi)
        {
            var size = Theme.Scale(SendSize, dpi);
            return Theme.Flip(new Rectangle(Width - Theme.Scale(PadX, dpi) - size, (Height - size) / 2, size, size), ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var dpi = DeviceDpi;
            var graphics = e.Graphics;
            graphics.Clear(palette.Card);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var radius = Theme.Scale(8f, dpi);
            var shape = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (var path = Theme.Rounded(shape, radius))
            {
                if (selected && !palette.HighContrast)
                {
                    using var fill = new SolidBrush(palette.Faint);
                    graphics.FillPath(fill, path);
                }

                // A contrast theme tells the selected row apart by its outline, not a tint (as the tray menu does).
                if (selected && (ring || palette.HighContrast))
                {
                    using var pen = new Pen(palette.Accent, Theme.Scale(2f, dpi));
                    using var inner = Theme.Rounded(RectangleF.Inflate(shape, -1, -1), Theme.Inner(radius, 1f));
                    graphics.DrawPath(pen, inner);
                }
            }

            var padX = Theme.Scale(PadX, dpi);
            var padY = Theme.Scale(PadY, dpi);
            var pitch = Pitch(dpi);
            var width = TextWidth(Width, dpi);
            var textBox = new Rectangle(padX, padY, width, lines * pitch);
            TextRenderer.DrawText(graphics, preview, PreviewFont(dpi), Theme.Flip(textBox, ClientRectangle), palette.Ink, TextFlags);

            var metaFont = Theme.Font(Theme.Scale(11f, dpi), Weight.Regular);
            var metaBox = new Rectangle(padX, textBox.Bottom, width, Theme.Scale(LineHeight, dpi));
            TextRenderer.DrawText(
                graphics, meta, metaFont, Theme.Flip(metaBox, ClientRectangle), palette.Dim,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | Theme.Start);

            if (selected && canSend)
            {
                var button = SendRect(dpi);
                if (overSend)
                {
                    using var path = Theme.Rounded(button, radius);
                    using var fill = new SolidBrush(Theme.Mix(palette.Faint, palette.Ink, 0.12f));
                    graphics.FillPath(fill, path);
                }

                var icon = Theme.Scale(16, dpi);
                Icons.Draw(
                    graphics, Icons.Smartphone,
                    new RectangleF(button.Left + ((button.Width - icon) / 2f), button.Top + ((button.Height - icon) / 2f), icon, icon),
                    overSend ? palette.Ink : palette.Dim);
            }
        }

        private void SetOverSend(bool value)
        {
            if (overSend == value)
            {
                return;
            }

            overSend = value;
            Cursor = value ? Cursors.Hand : Cursors.Default;
            Invalidate();
            if (value)
            {
                var button = SendRect(DeviceDpi);
                tip.Show(Strings.Clip_SendToPhone, this, button.Left, button.Bottom + 2);
            }
            else
            {
                tip.Hide(this);
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            Hovered?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            SetOverSend(selected && canSend && SendRect(DeviceDpi).Contains(e.Location));
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            SetOverSend(false);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || !ClientRectangle.Contains(e.Location))
            {
                return;
            }

            if (selected && canSend && SendRect(DeviceDpi).Contains(e.Location))
            {
                SendRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                Picked?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
