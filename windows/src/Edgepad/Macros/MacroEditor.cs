using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Edgepad.Ui;

namespace Edgepad.Macros;

/// <summary>
/// The tray menu's way into <see cref="MacroStore"/>. This dialog is the only place a macro is ever created,
/// which is the point: see the note at the top of <see cref="MacroStore"/> for why the owner typing a target
/// here, on the laptop, is what makes the phone's buttons safe to press.
/// </summary>
internal static class MacroEditor
{
    /// <summary>Opens the editor modally and saves on Save. Does nothing on Cancel.</summary>
    public static void Show(MacroStore store)
    {
        using var window = new MacroWindow(store);
        window.ShowDialog();
    }
}

/// <summary>
/// The design's dialog: a list on the left, the selected macro's fields on the right, Remove, Cancel and Save
/// along the bottom, and an empty state in place of both columns when there is nothing yet. One window rather
/// than a list plus an Add/Edit dialog on top of it — a second modal to fill in three text boxes is the kind of
/// ceremony that makes a setting feel expensive to change.
///
/// The working copy is a plain list that only reaches the store on Save, so Cancel needs to undo nothing.
///
/// Every measurement is the design's at 96 dpi, turned into pixels by <see cref="Relayout"/> for the monitor the
/// dialog is on, and again when it moves to another; WinForms' own scaling is off so the two do not compound.
/// </summary>
internal sealed class MacroWindow : Form
{
    // The design's measurements at 96 dpi.
    private const int DialogWidth = 680;
    private const int Pad = 24;
    private const int ListWidth = 220;
    private const int ColumnGap = 20;
    private const int BodyHeight = 300;
    private const int Box = 36;
    private const int RowHeight = 42;

    /// <summary>
    /// Says what is true rather than what is reassuring, and says it about the macro buttons only. The phone can
    /// also type and press keys through the keyboard screen, so a claim that it can never run anything else
    /// would be false; see <see cref="MacroStore"/>.
    /// </summary>
    private const string Description = "Buttons on your phone's macro screen, up to 15. The phone sends a button's "
        + "number, never a program, so it can only run what you add here.";

    private readonly List<Macro> working;
    private readonly Palette palette = Palette.Current;

    /// <summary>
    /// Each macro's picture, keyed by where it comes from. Filled when a target or icon is settled — on leaving
    /// the box, or after a browse — and never per keystroke, because reading a half-typed network path can
    /// block the dialog for as long as Windows takes to give up on the server.
    /// </summary>
    private readonly Dictionary<string, Image?> pictures = [];

    private readonly ListBox list = new() { DrawMode = DrawMode.OwnerDrawFixed, BorderStyle = BorderStyle.None, IntegralHeight = false };
    private readonly TextBox name = new() { BorderStyle = BorderStyle.None, AccessibleName = "Name" };
    private readonly TextBox target = new() { BorderStyle = BorderStyle.None, AccessibleName = "Opens" };
    private readonly TextBox arguments = new() { BorderStyle = BorderStyle.None, AccessibleName = "Arguments", PlaceholderText = "None" };
    private readonly Framed listFrame;
    private readonly Framed nameFrame;
    private readonly Framed targetFrame;
    private readonly Framed argumentsFrame;
    private readonly Tile preview;
    private readonly FlatButton add;
    private readonly FlatButton browse;
    private readonly FlatButton chooseIcon;
    private readonly FlatButton ownIcon;
    private readonly FlatButton remove;
    private readonly FlatButton cancel;
    private readonly FlatButton save;
    private readonly EmptyState empty;

    /// <summary>What only shows while there are macros: both columns, and Remove.</summary>
    private readonly System.Windows.Forms.Control[] full;

    /// <summary>Where the painted captions go, set by <see cref="Relayout"/>: the field name, then any dim note after it.</summary>
    private readonly List<(Point At, string Text, string? Note)> captions = [];

    private Rectangle title;
    private Rectangle description;

    /// <summary>Set while the controls are being written to from the list, so the writes do not echo back.</summary>
    private bool filling;

    /// <summary>The row a mouse drag started on, or -1 when nothing is being dragged.</summary>
    private int dragging = -1;

    public MacroWindow(MacroStore store)
    {
        working = [.. store.Macros];

        Text = "Macros";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = palette.Card;
        DoubleBuffered = true;

        listFrame = new Framed(list, palette, radius: 10, inset: 4);
        nameFrame = new Framed(name, palette, radius: 8, inset: 12);
        targetFrame = new Framed(target, palette, radius: 8, inset: 12);
        argumentsFrame = new Framed(arguments, palette, radius: 8, inset: 12);
        preview = new Tile(palette);
        add = new FlatButton("Add macro", ButtonKind.Outline, palette, Icons.Plus);
        browse = new FlatButton("Browse", ButtonKind.Outline, palette, Icons.FolderOpen);
        chooseIcon = new FlatButton("Choose…", ButtonKind.Outline, palette);
        ownIcon = new FlatButton("Use the program's own", ButtonKind.Ghost, palette);
        remove = new FlatButton("Remove", ButtonKind.Ghost, palette, Icons.Trash) { Danger = true };
        cancel = new FlatButton("Cancel", ButtonKind.Outline, palette) { DialogResult = DialogResult.Cancel };
        save = new FlatButton("Save", ButtonKind.Primary, palette) { DialogResult = DialogResult.OK };
        empty = new EmptyState(palette);
        full = [listFrame, add, nameFrame, targetFrame, browse, argumentsFrame, preview, chooseIcon, ownIcon, remove];

        list.DrawItem += DrawRow;
        list.SelectedIndexChanged += (_, _) => Fill();
        list.MouseDown += (_, e) => dragging = e.Button == MouseButtons.Left ? list.IndexFromPoint(e.Location) : -1;
        list.MouseMove += (_, e) => Drag(e);
        list.MouseUp += (_, _) => dragging = -1;
        list.HandleCreated += (_, _) =>
        {
            if (palette.IsDark)
            {
                Theme.DarkScrollbars(list.Handle);
            }
        };

        // Only so much of a name survives the trip to the phone, so the box stops there rather than letting
        // the store quietly cut a name the owner watched themselves type.
        // A byte budget cannot be spelled as a character count, and MaxLength is the only thing a TextBox
        // understands. This keeps typing roughly inside the limit; MacroStore does the real cut on save.
        name.MaxLength = MacroStore.MaxNameBytes;
        name.TextChanged += (_, _) => Edited();
        target.TextChanged += (_, _) => Edited();
        arguments.TextChanged += (_, _) => Edited();

        // The row's picture follows the target once the typing stops, not on every keystroke; see pictures.
        name.Leave += (_, _) => Relabel();
        target.Leave += (_, _) => Relabel();

        add.Click += (_, _) => Add();
        empty.Add.Click += (_, _) => Add();
        remove.Click += (_, _) => Remove();
        browse.Click += (_, _) => Browse();
        chooseIcon.Click += (_, _) => BrowseIcon();
        ownIcon.Click += (_, _) => SetIcon(null);
        save.Click += (_, _) => store.Save(working);
        AcceptButton = save;
        CancelButton = cancel;

        // Added in tab order: the list, then down the form, then the footer from left to right.
        Controls.AddRange([listFrame, add, nameFrame, targetFrame, browse, argumentsFrame, chooseIcon, ownIcon, preview, empty, remove, cancel, save]);

        foreach (var macro in working)
        {
            ReadPicture(macro);
        }

        Relayout();
        Rebuild(working.Count > 0 ? 0 : -1);
    }

    /// <summary>What a row reads as. A macro being filled in has neither yet, and a blank row looks broken.</summary>
    private static string Label(Macro macro) =>
        macro.Name.Length > 0 ? macro.Name : macro.Target.Length > 0 ? macro.Target : "New macro";

    private static string Key(Macro macro) => $"{macro.Icon}|{macro.Target}";

    private int S(int value) => Theme.Scale(value, DeviceDpi);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(Handle, palette.IsDark);

        // The dialog can open on a monitor other than the one the constructor measured for.
        Relayout();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Relayout();
    }

    /// <summary>Alt+Up and Alt+Down move the selected macro, for anyone reordering without a mouse.</summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Alt | Keys.Up:
                Reorder(list.SelectedIndex, list.SelectedIndex - 1);
                return true;
            case Keys.Alt | Keys.Down:
                Reorder(list.SelectedIndex, list.SelectedIndex + 1);
                return true;
            default:
                return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    /// <summary>Places everything for the current dpi, from the design's 96 dpi measurements.</summary>
    private void Relayout()
    {
        var width = S(DialogWidth - (Pad * 2));
        var left = S(Pad);

        var titleFont = Theme.Font(Theme.Scale(18f, DeviceDpi), Weight.Black);
        title = new Rectangle(left, S(Pad), width, titleFont.Height);
        var body14 = Theme.Font(Theme.Scale(14f, DeviceDpi), Weight.Regular);
        var wrapped = TextRenderer.MeasureText(Description, body14, new Size(width, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        description = new Rectangle(left, title.Bottom + S(6), width, wrapped.Height);

        var top = description.Bottom + S(18);
        listFrame.Place(new Rectangle(left, top, S(ListWidth), S(BodyHeight - Box - 8)), DeviceDpi);
        add.SetBounds(left, top + S(BodyHeight - Box), S(ListWidth), S(Box));
        list.ItemHeight = Math.Min(255, S(RowHeight));

        // The right column: three labelled boxes, then the icon row.
        var x = left + S(ListWidth + ColumnGap);
        var column = S(Pad) + width - x;
        var caption = Theme.Font(Theme.Scale(14f, DeviceDpi), Weight.Bold).Height;
        var step = caption + S(6 + Box + 14);
        captions.Clear();

        captions.Add((new Point(x, top), "Name", null));
        nameFrame.Place(new Rectangle(x, top + caption + S(6), column, S(Box)), DeviceDpi);

        captions.Add((new Point(x, top + step), "Opens", null));
        browse.Width = browse.FitWidth(DeviceDpi);
        var opens = top + step + caption + S(6);
        targetFrame.Place(new Rectangle(x, opens, column - browse.Width - S(8), S(Box)), DeviceDpi);
        browse.SetBounds(x + column - browse.Width, opens, browse.Width, S(Box));

        captions.Add((new Point(x, top + (step * 2)), "Arguments", "optional"));
        argumentsFrame.Place(new Rectangle(x, top + (step * 2) + caption + S(6), column, S(Box)), DeviceDpi);

        captions.Add((new Point(x, top + (step * 3)), "Icon", null));
        var icons = top + (step * 3) + caption + S(6);
        preview.SetBounds(x, icons, S(Box), S(Box));
        chooseIcon.SetBounds(preview.Right + S(10), icons, chooseIcon.FitWidth(DeviceDpi), S(Box));
        ownIcon.SetBounds(chooseIcon.Right + S(4), icons, ownIcon.FitWidth(DeviceDpi), S(Box));

        empty.SetBounds(left, top, width, S(BodyHeight));

        // The footer: Remove on the left, Cancel and Save on the right.
        var footer = top + S(BodyHeight + 18);
        remove.SetBounds(left, footer, remove.FitWidth(DeviceDpi), S(Box));
        save.SetBounds(left + width - save.FitWidth(DeviceDpi), footer, save.FitWidth(DeviceDpi), S(Box));
        cancel.SetBounds(save.Left - S(8) - cancel.FitWidth(DeviceDpi), footer, cancel.FitWidth(DeviceDpi), S(Box));

        ClientSize = new Size(S(DialogWidth), footer + S(Box + Pad));
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

        // GDI+ rather than TextRenderer for the one Black line; Theme says why.
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using (var ink = new SolidBrush(palette.Ink))
        using (var typographic = StringFormat.GenericTypographic)
        {
            graphics.DrawString(Text, Theme.Font(Theme.Scale(18f, DeviceDpi), Weight.Black), ink, title.Location, typographic);
        }

        TextRenderer.DrawText(
            graphics, Description, Theme.Font(Theme.Scale(14f, DeviceDpi), Weight.Regular), description, palette.Dim, flags | TextFormatFlags.WordBreak);

        if (working.Count == 0)
        {
            return;
        }

        var bold = Theme.Font(Theme.Scale(14f, DeviceDpi), Weight.Bold);
        var regular = Theme.Font(Theme.Scale(14f, DeviceDpi), Weight.Regular);
        foreach (var (at, text, note) in captions)
        {
            TextRenderer.DrawText(graphics, text, bold, at, palette.Ink, flags);
            if (note is not null)
            {
                var after = TextRenderer.MeasureText(graphics, text + " ", bold, Size.Empty, flags).Width;
                TextRenderer.DrawText(graphics, note, regular, at with { X = at.X + after }, palette.Dim, flags);
            }
        }
    }

    /// <summary>One list row: grip, picture, name, and the slot number the phone shows it under.</summary>
    private void DrawRow(object? sender, DrawItemEventArgs e)
    {
        var graphics = e.Graphics;
        using (var card = new SolidBrush(palette.Card))
        {
            graphics.FillRectangle(card, e.Bounds);
        }

        if (e.Index < 0 || e.Index >= working.Count)
        {
            return;
        }

        var dpi = DeviceDpi;
        var macro = working[e.Index];
        var selected = (e.State & DrawItemState.Selected) != 0;
        var row = Rectangle.Inflate(e.Bounds, 0, -Theme.Scale(1, dpi));
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        if (selected)
        {
            using var faint = new SolidBrush(palette.Faint);
            using var shape = Theme.Rounded(row, Theme.Scale(6f, dpi));
            graphics.FillPath(faint, shape);
        }

        var x = row.X + Theme.Scale(8, dpi);
        var grip = Theme.Scale(14, dpi);
        Icons.Draw(graphics, Icons.Grip, new RectangleF(x, row.Y + ((row.Height - grip) / 2f), grip, grip), palette.Dim, 3.2f);
        x += grip + Theme.Scale(10, dpi);

        var size = Theme.Scale(22, dpi);
        var picture = new Rectangle(x, row.Y + ((row.Height - size) / 2), size, size);
        if (pictures.GetValueOrDefault(Key(macro)) is { } image)
        {
            graphics.DrawImage(image, picture);
        }
        else
        {
            using var faint = new SolidBrush(selected ? palette.Line : palette.Faint);
            using var shape = Theme.Rounded(picture, Theme.Scale(5f, dpi));
            graphics.FillPath(faint, shape);
        }

        x += size + Theme.Scale(10, dpi);

        // The slot badge: the number the button has on the phone, counted from one as a person counts.
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        var slot = (e.Index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var badgeFont = Theme.Font(Theme.Scale(12f, dpi), Weight.Bold);
        var badgeWidth = Math.Max(Theme.Scale(20, dpi), TextRenderer.MeasureText(graphics, slot, badgeFont, Size.Empty, flags).Width + Theme.Scale(8, dpi));
        var badge = new Rectangle(row.Right - Theme.Scale(8, dpi) - badgeWidth, row.Y + ((row.Height - Theme.Scale(20, dpi)) / 2), badgeWidth, Theme.Scale(20, dpi));
        using (var faint = new SolidBrush(selected ? palette.Card : palette.Faint))
        using (var shape = Theme.Rounded(badge, Theme.Scale(5f, dpi)))
        {
            graphics.FillPath(faint, shape);
        }

        TextRenderer.DrawText(graphics, slot, badgeFont, badge, palette.Dim, flags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        var font = Theme.Font(Theme.Scale(14f, dpi), selected ? Weight.Bold : Weight.Regular);
        TextRenderer.DrawText(
            graphics,
            Label(macro),
            font,
            new Rectangle(x, row.Y, badge.Left - Theme.Scale(8, dpi) - x, row.Height),
            palette.Ink,
            flags | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    /// <summary>Rebuilds the list and selects <paramref name="select"/>, clamped to what is left of it.</summary>
    private void Rebuild(int select)
    {
        filling = true;
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var macro in working)
        {
            // The text is what a screen reader reads out for the row; the painting does not use it.
            list.Items.Add(Label(macro));
        }

        list.EndUpdate();
        filling = false;

        // Assigning the selection outside the guard so the fields follow it; -1 is a valid answer and clears them.
        list.SelectedIndex = working.Count == 0 ? -1 : Math.Clamp(select, 0, working.Count - 1);
        Fill();
    }

    /// <summary>Writes the selected macro into the fields, and settles what shows and what can be clicked.</summary>
    private void Fill()
    {
        if (filling)
        {
            return;
        }

        var index = list.SelectedIndex;
        var macro = index >= 0 ? working[index] : null;

        filling = true;
        name.Text = macro?.Name ?? string.Empty;
        target.Text = macro?.Target ?? string.Empty;
        arguments.Text = macro?.Arguments ?? string.Empty;
        filling = false;

        var none = working.Count == 0;
        foreach (var control in full)
        {
            control.Visible = !none;
        }

        empty.Visible = none;
        add.Enabled = working.Count < MacroStore.MaxMacros;
        ownIcon.Enabled = macro?.Icon is not null;
        preview.Picture = macro is null ? null : pictures.GetValueOrDefault(Key(macro));

        // The captions are painted by the form and come and go with the columns.
        Invalidate();
    }

    private void Edited()
    {
        var index = list.SelectedIndex;
        if (filling || index < 0)
        {
            return;
        }

        working[index] = working[index] with
        {
            Name = name.Text,
            Target = target.Text,
            Arguments = arguments.Text.Length > 0 ? arguments.Text : null,
        };

        // Owner-drawn, so the row's name follows the typing at the cost of one repaint.
        list.Invalidate();
    }

    /// <summary>Settles the selected row once its typing stops: its text for a screen reader, and its picture.</summary>
    private void Relabel()
    {
        var index = list.SelectedIndex;
        if (index < 0)
        {
            return;
        }

        var macro = working[index];
        ReadPicture(macro);
        filling = true;
        list.Items[index] = Label(macro);
        list.SelectedIndex = index;
        filling = false;
        preview.Picture = pictures.GetValueOrDefault(Key(macro));
        ownIcon.Enabled = macro.Icon is not null;
        list.Invalidate();
    }

    /// <summary>Reads the picture for <paramref name="macro"/> if it has not been read already.</summary>
    private void ReadPicture(Macro macro)
    {
        var key = Key(macro);
        if (pictures.ContainsKey(key))
        {
            return;
        }

        Image? image = null;
        if (MacroIcons.Png(macro) is { } png)
        {
            // Copied out of the stream: a GDI+ image made from a stream reads it lazily and needs it kept open.
            using var stream = new MemoryStream(png);
            using var decoded = Image.FromStream(stream);
            image = new Bitmap(decoded);
        }

        pictures[key] = image;
    }

    private void Add()
    {
        if (working.Count >= MacroStore.MaxMacros)
        {
            return;
        }

        working.Add(new Macro(string.Empty, string.Empty, null));
        Rebuild(working.Count - 1);
        name.Focus();
    }

    private void Remove()
    {
        var index = list.SelectedIndex;
        if (index < 0)
        {
            return;
        }

        working.RemoveAt(index);
        Rebuild(index);
        if (working.Count == 0)
        {
            empty.Add.Focus();
        }
    }

    /// <summary>
    /// Follows a mouse drag down or up the list, moving the dragged macro into each row the pointer reaches.
    /// The list itself moves its selection to the row under the pointer as it goes, which is the same row this
    /// moves the macro into, so the selection and the dragged macro stay together.
    /// </summary>
    private void Drag(MouseEventArgs e)
    {
        if (dragging < 0 || e.Button != MouseButtons.Left)
        {
            return;
        }

        var over = list.IndexFromPoint(e.Location);
        if (over >= 0 && over != dragging)
        {
            Reorder(dragging, over);
            dragging = over;
        }
    }

    /// <summary>
    /// Moves one macro to a new place, and the selection with it: the row is what is being moved. Named Reorder
    /// rather than Move because Control already has a Move event, and a method hiding it is an error under
    /// warnings-as-errors rather than the harmless shadowing it looks like.
    /// </summary>
    private void Reorder(int from, int to)
    {
        if (from < 0 || to < 0 || from >= working.Count || to >= working.Count || from == to)
        {
            return;
        }

        var macro = working[from];
        working.RemoveAt(from);
        working.Insert(to, macro);
        Rebuild(to);
    }

    private void Browse()
    {
        if (Pick("Choose what this button opens", "Programs and documents (*.*)|*.*") is not { } chosen)
        {
            return;
        }

        target.Text = chosen;
        if (name.Text.Length == 0)
        {
            // The file's own name is nearly always the right button label, and is far less work than typing it.
            name.Text = Path.GetFileNameWithoutExtension(chosen);
        }

        Relabel();
    }

    /// <summary>
    /// Overrides the picture the phone shows. Left alone for nearly every macro, because the program the
    /// button opens already carries its own icon; this is for the ones that do not — a folder, a URL, a
    /// script — and for the ones whose own icon is not what the owner wants to see on the grid.
    /// </summary>
    private void BrowseIcon()
    {
        const string filter = "Icons, programs and pictures (*.ico;*.exe;*.dll;*.png;*.bmp;*.jpg)"
            + "|*.ico;*.exe;*.dll;*.png;*.bmp;*.jpg;*.jpeg;*.gif|All files (*.*)|*.*";
        if (Pick("Choose the picture for this button", filter) is { } chosen)
        {
            SetIcon(chosen);
        }
    }

    /// <summary>Sets or, given null, clears the selected macro's picture override.</summary>
    private void SetIcon(string? path)
    {
        var index = list.SelectedIndex;
        if (index < 0)
        {
            return;
        }

        working[index] = working[index] with { Icon = path };
        Relabel();
    }

    /// <summary>One file from the owner, or null when the dialog was cancelled.</summary>
    private string? Pick(string caption, string filter)
    {
        if (list.SelectedIndex < 0)
        {
            return null;
        }

        using var picker = new OpenFileDialog
        {
            Title = caption,
            Filter = filter,
            CheckFileExists = true,
        };

        return picker.ShowDialog(this) == DialogResult.OK ? picker.FileName : null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var image in pictures.Values)
            {
                image?.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// What the dialog shows before there is a first macro: a dashed box with a word on what a macro can be, and
    /// the one thing to do next. Painted rather than built from labels, apart from its button.
    /// </summary>
    private sealed class EmptyState : System.Windows.Forms.Control
    {
        private const string Headline = "No macros yet";
        private const string Detail = "Add an app, a document, a folder or a web address. Each one becomes a button on the phone.";

        private readonly Palette palette;

        public EmptyState(Palette palette)
        {
            this.palette = palette;
            Add = new FlatButton("Add macro", ButtonKind.Primary, palette, Icons.Plus);
            AccessibleName = Headline;
            AccessibleDescription = Detail;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Controls.Add(Add);
        }

        public FlatButton Add { get; }

        private int S(int value) => Theme.Scale(value, DeviceDpi);

        /// <summary>The stack's parts, top to bottom, centred as one block in the box.</summary>
        private (Rectangle Tile, Rectangle Headline, Rectangle Detail, int Button) Arrange()
        {
            var headline = Theme.Font(Theme.Scale(16f, DeviceDpi), Weight.Bold);
            var detailFont = Theme.Font(Theme.Scale(14f, DeviceDpi), Weight.Regular);
            var detail = TextRenderer.MeasureText(
                Detail, detailFont, new Size(S(340), 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.HorizontalCenter);
            var height = S(48 + 14) + headline.Height + S(10) + detail.Height + S(16 + 36);
            var top = (Height - height) / 2;

            var tile = new Rectangle((Width - S(48)) / 2, top, S(48), S(48));
            var words = new Rectangle(0, tile.Bottom + S(14), Width, headline.Height);
            var below = new Rectangle((Width - S(340)) / 2, words.Bottom + S(10), S(340), detail.Height);
            return (tile, words, below, below.Bottom + S(16));
        }

        /// <summary>Laid out again once it has a window, which is when it learns the dpi of the monitor it is on.</summary>
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            PerformLayout();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            var width = Add.FitWidth(DeviceDpi);
            Add.SetBounds((Width - width) / 2, Arrange().Button, width, S(36));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            graphics.Clear(palette.Card);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using (var dashed = new Pen(palette.Line) { DashStyle = DashStyle.Dash })
            using (var box = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Theme.Scale(10f, DeviceDpi)))
            {
                graphics.DrawPath(dashed, box);
            }

            var (tile, headline, detail, _) = Arrange();
            using (var faint = new SolidBrush(palette.Faint))
            using (var shape = Theme.Rounded(tile, Theme.Scale(10f, DeviceDpi)))
            {
                graphics.FillPath(faint, shape);
            }

            var icon = S(22);
            Icons.Draw(graphics, Icons.Macro, new RectangleF(tile.X + ((tile.Width - icon) / 2f), tile.Y + ((tile.Height - icon) / 2f), icon, icon), palette.Ink);

            const TextFormatFlags centred = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter;
            TextRenderer.DrawText(graphics, Headline, Theme.Font(Theme.Scale(16f, DeviceDpi), Weight.Bold), headline, palette.Ink, centred);
            TextRenderer.DrawText(
                graphics, Detail, Theme.Font(Theme.Scale(14f, DeviceDpi), Weight.Regular), detail, palette.Dim, centred | TextFormatFlags.WordBreak);
        }
    }
}
