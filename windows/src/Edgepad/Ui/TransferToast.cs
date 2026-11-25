using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Edgepad.Controls;
using Edgepad.Transfer;

namespace Edgepad.Ui;

/// <summary>
/// The words of a transfer card, with no window behind them, so a table can pin every phase and every failure. Everything
/// comes from <see cref="Strings"/>; nothing here is a literal a translator could not reach.
/// </summary>
internal static class ToastText
{
    private const int NameMax = 32;

    /// <summary>The share of the current file moved so far, 0-100.</summary>
    public static int Percent(TransferView view) =>
        view.Size == 0 ? 0 : (int)Math.Min(100UL, view.Sent * 100 / view.Size);

    /// <summary>"Sending to Pixel 8" while it runs; what a finished batch came to otherwise.</summary>
    public static string Title(TransferView view, string phone)
    {
        if (view.Phase == TransferPhase.Running)
        {
            return view.Cancelling
                ? Strings.Xfer_Cancelling
                : (view.Sending ? Strings.Xfer_PanelSending : Strings.Xfer_PanelReceiving).Fill(phone);
        }

        var failed = view.Failed.Count;
        if (failed == 0)
        {
            if (view.Done == 1)
            {
                return (view.Sending ? Strings.Xfer_SentOne : Strings.Balloon_FileReceived).Fill(FileNames.Middle(view.Name, NameMax));
            }

            return (view.Sending ? Strings.Xfer_SentMany : Strings.Xfer_ReceivedMany).Fill(view.Done);
        }

        if (view.Done > 0)
        {
            return (view.Sending ? Strings.Xfer_SentSome : Strings.Xfer_ReceivedSome).Fill(view.Done, view.Done + failed);
        }

        return failed == 1
            ? Strings.Xfer_FailedOne.Fill(FileNames.Middle(view.Failed[0].Name, NameMax))
            : Strings.Xfer_FailedMany.Fill(failed);
    }

    /// <summary>
    /// "file · 2 of 5 · 4.2 MB/s" while it runs: the count only when the batch has more than one file (receiving has no total,
    /// so its running index stands in), the speed only once the meter has one. A hold says so instead. A failure says "Not sent: why".
    /// </summary>
    public static string Subtitle(TransferView view)
    {
        if (view.Phase == TransferPhase.Running)
        {
            var file = FileNames.Middle(view.Name, NameMax);
            if (view.Paused)
            {
                return Strings.Xfer_PanelHeld.Fill(file);
            }

            var total = Math.Max(view.Total, view.Index);
            var speed = view.BytesPerSecond > 0 ? ByteSize.Format((ulong)view.BytesPerSecond) : null;
            return (total > 1, speed) switch
            {
                (true, { } rate) => Strings.Xfer_PanelDetail.Fill(file, view.Index, total, rate),
                (true, null) => Strings.Xfer_PanelCount.Fill(file, view.Index, total),
                (false, { } rate) => Strings.Xfer_PanelSpeed.Fill(file, rate),
                _ => file,
            };
        }

        if (view.Failed.Count > 0)
        {
            return (view.Sending ? Strings.Xfer_NotSent : Strings.Xfer_NotReceived).Fill(Reason(view.Failed[^1].Why));
        }

        return view.Skipped > 0 ? Strings.Xfer_SkippedNote.Fill(view.Skipped) : string.Empty;
    }

    public static string Reason(TransferFailure why) => why switch
    {
        TransferFailure.Refused => Strings.Xfer_WhyRefused,
        TransferFailure.Damaged => Strings.Xfer_WhyDamaged,
        TransferFailure.WriteFailed => Strings.Xfer_WhyWrite,
        TransferFailure.Busy => Strings.Xfer_WhyBusy,
        TransferFailure.TimedOut => Strings.Xfer_WhyTimeout,
        TransferFailure.LinkLost => Strings.Xfer_WhyLost,
        TransferFailure.Unreadable => Strings.Xfer_WhyUnreadable,
        TransferFailure.Cancelled => Strings.Xfer_WhyCancelled,
        _ => Strings.Xfer_WhySkipped,
    };
}

/// <summary>Which of the card's three buttons a view offers. Close is always there; Pause and Stop only while a batch runs and is not already stopping.</summary>
internal readonly record struct ToastButtons(bool Pause, bool Stop, bool Close)
{
    public static ToastButtons Of(TransferView view)
    {
        var live = view.Phase == TransferPhase.Running && !view.Cancelling;
        return new ToastButtons(live, live, view.Phase != TransferPhase.Idle);
    }
}

/// <summary>
/// The progress and result of a batch of transfers, as a card in the corner of the screen: app mark, "Sending to Pixel 8", the
/// file line, a 4 px bar, and three icon buttons (Pause or Resume, Stop, Close). It does not take focus from what the owner is
/// doing, is not in Alt-Tab or the taskbar, and sits above the taskbar. Close only hides it while a batch runs; the tray's
/// Transfers row brings it back (<see cref="Reveal"/>). Same lifecycle as <see cref="LevelOverlay"/>: <see cref="Show"/> may be
/// called from any thread (it only posts, so it is safe from inside the feed's event), the window is built once on the UI
/// thread, repainted, rebuilt after a throw, and given up on after three throws in a row.
/// </summary>
internal sealed class TransferToast : IDisposable
{
    // Measurements at 96 dpi.
    internal const int CardWidth = 380;
    internal const int RunningHeight = 76;
    internal const int DoneHeight = 64;
    internal const int FailedHeight = 84;

    private const int MaxFailures = 3;

    private readonly SynchronizationContext ui;
    private ToastWindow? window;
    private int failures;
    private volatile bool stopped;
    private TransferView? latest;
    private int posted;

    public TransferToast() => ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

    /// <summary>Pause was pressed; the argument is whether the batch is a send. Set before the first <see cref="Show"/>; read on the UI thread.</summary>
    public Action<bool>? OnPause { get; set; }

    public Action<bool>? OnResume { get; set; }

    /// <summary>Stop was pressed, for a send or a receive alike.</summary>
    public Action<bool>? OnStop { get; set; }

    public Action? OnRetry { get; set; }

    public Action? OnClose { get; set; }

    /// <summary>The phone's name as the owner knows it, for the title. UI thread.</summary>
    public Func<string> PhoneName { get; set; } = static () => string.Empty;

    /// <summary>True once the toast has given up; the caller then falls back to a balloon.</summary>
    public bool Stopped => stopped;

    /// <summary>Newest view wins; any thread. Does nothing but post, so it may be called under the feed's lock.</summary>
    public void Show(TransferView view)
    {
        if (stopped)
        {
            return;
        }

        Volatile.Write(ref latest, view);
        if (Interlocked.Exchange(ref posted, 1) == 0)
        {
            ui.Post(_ => Drain(), null);
        }
    }

    /// <summary>Brings back a card the owner closed while its batch still runs. UI thread.</summary>
    public void Reveal() => window?.Reveal();

    private void Drain()
    {
        Interlocked.Exchange(ref posted, 0);
        if (Volatile.Read(ref latest) is { } view)
        {
            Present(view);
        }
    }

    private void Present(TransferView view)
    {
        if (stopped)
        {
            return;
        }

        try
        {
            window ??= new ToastWindow(this);
            window.Present(view);
            failures = 0;
        }
        catch (Exception e)
        {
            var broken = window;
            window = null;
            broken?.Dispose();
            if (++failures < MaxFailures)
            {
                Log.Write($"Transfer toast failed, rebuilding it: {e.Message}");
            }
            else
            {
                Log.Write($"Transfer toast disabled after {MaxFailures} failures in a row: {e}");
                stopped = true;
            }
        }
    }

    public void Dispose()
    {
        stopped = true;
        if (window is null)
        {
            return;
        }

        if (window.InvokeRequired)
        {
            var open = window;
            open.BeginInvoke(() => open.Dispose());
        }
        else
        {
            window.Dispose();
        }

        window = null;
    }

    private enum Mode { Running, Done, Failed }

    private enum Button { Pause, Stop, Close }

    private sealed class ToastWindow : Form
    {
        private const int WsExToolWindow = 0x00000080;
        private const int WsExNoActivate = 0x08000000;
        private const int Corner = 12;
        private const int EdgeMargin = 16;
        private const int Pad = 14;
        private const int LogoLeft = 16;
        private const int LogoSize = 32;
        private const int TextLeft = 60;
        private const int ButtonSize = 32;
        private const int ButtonGap = 2;
        private const int ButtonRight = 10;
        private const int ButtonRadius = 6;
        private const int BarHeight = 4;
        private const int HideAfterMs = 4000;
        private const int HideAfterFailureMs = 8000;

        // 14 x 14 glyphs, filled: the transport icons the Linux card and the Mac panel draw.
        private const string PauseGlyph = "M3,2 H6 V12 H3 Z M8,2 H11 V12 H8 Z";
        private const string PlayGlyph = "M3,1.5 L12,7 L3,12.5 Z";
        private const string StopGlyph = "M3,3 H11 V11 H3 Z";
        private const float GlyphGrid = 14f;

        private const TextFormatFlags Line =
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter;

        // The exe's own icon, the app mark; null (the file glyph stands in) where the shell cannot give one.
        private static readonly Lazy<Bitmap?> Mark = new(LoadMark);

        private readonly TransferToast owner;

        // One timer, started only while a finished toast waits to hide itself; stopped the moment it hides.
        private readonly System.Windows.Forms.Timer clock = new() { Interval = 25 };

        private Palette palette = Palette.Current;
        private TransferView view = TransferView.Idle;
        private Mode mode;
        private bool built;
        private bool cancelClicked;
        private bool userHidden;
        private long hideAt;
        private int textWidth;
        private int hot = -1;
        private int focused = -1;
        private (Mode, int, int, bool, bool, int, int)? announced;

        // Hit areas in client coordinates, laid out with the card, in Tab order; only what the view offers.
        private readonly List<(Button Kind, Rectangle Area)> buttons = [];
        private Rectangle retryArea;

        public ToastWindow(TransferToast owner)
        {
            this.owner = owner;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;
            RightToLeft = Theme.Direction;
            AccessibleRole = AccessibleRole.Alert;
            clock.Tick += OnTick;
        }

        // The toast is clickable (the buttons, Try again) but must never take the foreground from what the owner is doing.
        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= WsExNoActivate | WsExToolWindow;
                return parameters;
            }
        }

        protected override bool ShowWithoutActivation => true;

        protected override AccessibleObject CreateAccessibilityInstance() => new CardAccessible(this);

        private static Bitmap? LoadMark()
        {
            try
            {
                if (Environment.ProcessPath is { } exe && Icon.ExtractAssociatedIcon(exe) is { } icon)
                {
                    using (icon)
                    {
                        return icon.ToBitmap();
                    }
                }
            }
            catch (Exception e) when (e is ArgumentException or IOException or System.ComponentModel.Win32Exception)
            {
                Log.Write($"Transfer card mark not read: {e.Message}");
            }

            return null;
        }

        public void Present(TransferView next)
        {
            if (next.Phase == TransferPhase.Idle)
            {
                Dismiss();
                return;
            }

            if (next.Phase != TransferPhase.Running)
            {
                cancelClicked = false;
            }
            else if (cancelClicked)
            {
                next = next with { Cancelling = true };
            }

            var was = built ? mode : (Mode?)null;
            view = next;
            mode = next.Phase == TransferPhase.Running ? Mode.Running : next.Failed.Count == 0 ? Mode.Done : Mode.Failed;

            // A closed card stays closed until its batch is over (the result shows) or the tray asks for it.
            if (mode != Mode.Running || was != Mode.Running)
            {
                userHidden = false;
            }

            var current = Palette.Current;
            if (was != mode || current != palette || !built)
            {
                palette = current;
                BackColor = palette.Card;
                built = true;
            }

            Relayout();
            if (userHidden)
            {
                if (Visible)
                {
                    Hide();
                }

                return;
            }

            if (!Visible)
            {
                Opacity = 1;
                Show();
            }

            Opacity = 1;
            if (mode != Mode.Running)
            {
                if (was != mode)
                {
                    hideAt = Environment.TickCount64 + (mode == Mode.Failed ? HideAfterFailureMs : HideAfterMs);
                }

                clock.Start();
            }
            else
            {
                clock.Stop();
            }

            Announce();
            Invalidate();
        }

        /// <summary>Shows a card the owner closed, and asks for the keyboard so Tab, Enter and Space work on it.</summary>
        public void Reveal()
        {
            if (!built || mode != Mode.Running)
            {
                return;
            }

            userHidden = false;
            Relayout();
            Opacity = 1;
            if (!Visible)
            {
                Show();
            }

            Activate();
            Announce();
            Invalidate();
        }

        private void Dismiss()
        {
            clock.Stop();
            built = false;
            cancelClicked = false;
            userHidden = false;
            focused = -1;
            if (Visible)
            {
                Hide();
            }
        }

        private void Relayout()
        {
            var dpi = DeviceDpi;
            var height = Theme.Scale(mode switch { Mode.Running => RunningHeight, Mode.Done => DoneHeight, _ => FailedHeight }, dpi);
            var size = new Size(Theme.Scale(CardWidth, dpi), height);

            // The primary screen's work area: above the taskbar, in the bottom-right corner.
            var work = Screen.PrimaryScreen?.WorkingArea ?? SystemInformation.WorkingArea;
            var margin = Theme.Scale(EdgeMargin, dpi);
            var where = new Rectangle(new Point(work.Right - size.Width - margin, work.Bottom - size.Height - margin), size);

            var all = new Rectangle(Point.Empty, size);
            var box = Theme.Scale(ButtonSize, dpi);
            var gap = Theme.Scale(ButtonGap, dpi);
            var top = Theme.Scale(Pad, dpi);
            var wants = ToastButtons.Of(view);

            // Built from the trailing edge in: Close, then Stop, then Pause, so Tab runs Pause, Stop, Close.
            var kinds = new List<Button>(3);
            if (wants.Pause)
            {
                kinds.Add(Button.Pause);
            }

            if (wants.Stop)
            {
                kinds.Add(Button.Stop);
            }

            if (wants.Close)
            {
                kinds.Add(Button.Close);
            }

            buttons.Clear();
            var x = size.Width - Theme.Scale(ButtonRight, dpi) - (kinds.Count * box) - ((kinds.Count - 1) * gap);
            var first = x;
            foreach (var kind in kinds)
            {
                buttons.Add((kind, Theme.Flip(new Rectangle(x, top, box, box), all)));
                x += box + gap;
            }

            if (focused >= buttons.Count)
            {
                focused = buttons.Count - 1;
            }

            textWidth = Math.Max(0, first - Theme.Scale(8, dpi) - Theme.Scale(TextLeft, dpi));

            retryArea = Rectangle.Empty;
            if (mode == Mode.Failed && view.Sending && owner.OnRetry is not null)
            {
                using var font = Theme.Font(Theme.Scale(12, dpi), Weight.Bold);
                var wide = TextRenderer.MeasureText(Strings.Xfer_Retry, font, Size.Empty, Line | TextFormatFlags.NoClipping).Width;
                retryArea = Theme.Flip(
                    new Rectangle(Theme.Scale(TextLeft, dpi), height - Theme.Scale(28, dpi), wide, Theme.Scale(20, dpi)), all);
            }

            if (Bounds != where)
            {
                Bounds = where;
                using var shape = Theme.Rounded(new RectangleF(0, 0, size.Width, size.Height), Theme.Scale(Corner, dpi));
                Region = new Region(shape);
            }
        }

        private string NameOf(Button kind) => kind switch
        {
            Button.Pause => view.Paused ? Strings.Xfer_Resume : Strings.Xfer_Pause,
            Button.Stop => Strings.Xfer_Stop,
            _ => Strings.Xfer_Close,
        };

        private void Announce()
        {
            var key = (mode, view.Failed.Count, view.Done, view.Cancelling, view.Paused, view.Index, ToastText.Percent(view) / 10);
            if (key == announced)
            {
                return;
            }

            announced = key;
            AccessibleName = ToastText.Title(view, owner.PhoneName()) + ". " + ToastText.Subtitle(view);
            AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
        }

        private void OnTick(object? sender, EventArgs e)
        {
            var hold = mode == Mode.Failed ? HideAfterFailureMs : HideAfterMs;

            // Resting the pointer on the card holds it open.
            if (Bounds.Contains(Cursor.Position))
            {
                hideAt = Environment.TickCount64 + hold;
                Opacity = 1;
                return;
            }

            var left = hideAt - Environment.TickCount64;
            if (left <= 0)
            {
                Dismiss();
                Opacity = 1;
                return;
            }

            var fade = Theme.Motion.Reveal;
            if (left < fade)
            {
                Opacity = 1 - Progress.Leaving(fade - left, fade);
            }
        }

        private int ButtonAt(Point where)
        {
            for (var i = 0; i < buttons.Count; i++)
            {
                if (buttons[i].Area.Contains(where))
                {
                    return i;
                }
            }

            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var over = ButtonAt(e.Location);
            Cursor = over >= 0 || retryArea.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
            if (over != hot)
            {
                hot = over;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hot >= 0)
            {
                hot = -1;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            if (ButtonAt(e.Location) is var index and >= 0)
            {
                Press(index);
            }
            else if (retryArea.Contains(e.Location))
            {
                owner.OnRetry?.Invoke();
            }
        }

        /// <summary>What a button does. Pause toggles, Stop works for sends and receives, Close only hides a running card.</summary>
        private void Press(int index)
        {
            if (index < 0 || index >= buttons.Count)
            {
                return;
            }

            switch (buttons[index].Kind)
            {
                case Button.Pause when view.Paused:
                    owner.OnResume?.Invoke(view.Sending);
                    break;
                case Button.Pause:
                    owner.OnPause?.Invoke(view.Sending);
                    break;
                case Button.Stop:
                    cancelClicked = true;
                    view = view with { Cancelling = true };
                    owner.OnStop?.Invoke(view.Sending);
                    Relayout();
                    Announce();
                    Invalidate();
                    break;
                default:
                    if (mode == Mode.Running)
                    {
                        userHidden = true;
                        clock.Stop();
                        Hide();
                    }
                    else if (mode == Mode.Failed)
                    {
                        owner.OnClose?.Invoke();
                    }
                    else
                    {
                        Dismiss();
                    }

                    break;
            }
        }

        // Keys only reach the card while it is the active window, which the tray's Transfers row asks for.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (buttons.Count == 0)
            {
                return base.ProcessCmdKey(ref msg, keyData);
            }

            switch (keyData)
            {
                case Keys.Tab:
                    focused = (focused + 1) % buttons.Count;
                    Invalidate();
                    return true;
                case Keys.Tab | Keys.Shift:
                    focused = focused <= 0 ? buttons.Count - 1 : focused - 1;
                    Invalidate();
                    return true;
                case Keys.Enter or Keys.Space when focused >= 0:
                    Press(focused);
                    return true;
                default:
                    return base.ProcessCmdKey(ref msg, keyData);
            }
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (focused < 0 && buttons.Count > 0)
            {
                focused = 0;
                Invalidate();
            }
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (focused >= 0)
            {
                focused = -1;
                Invalidate();
            }
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            if (built)
            {
                Relayout();
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            var dpi = DeviceDpi;
            var all = ClientRectangle;
            using (var hairline = new Pen(palette.Hairline))
            using (var outline = Theme.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), Theme.Scale(Corner, dpi)))
            {
                g.DrawPath(hairline, outline);
            }

            int S(int v) => Theme.Scale(v, dpi);
            var title = ToastText.Title(view, owner.PhoneName());
            var subtitle = ToastText.Subtitle(view);

            PaintMark(g, Theme.Flip(new Rectangle(S(LogoLeft), S(Pad), S(LogoSize), S(LogoSize)), all), S(8));
            for (var i = 0; i < buttons.Count; i++)
            {
                PaintButton(g, i, S(ButtonRadius));
            }

            var titleFont = Theme.Font(S(14), Weight.Bold);
            var smallFont = Theme.Font(S(12), Weight.Regular);

            void Draw(string text, Font font, Color colour, int top, int lineHeight) =>
                TextRenderer.DrawText(
                    g, text, font, Theme.Flip(new Rectangle(S(TextLeft), top, textWidth, lineHeight), all), colour, Line | Theme.Start);

            switch (mode)
            {
                case Mode.Running:
                    Draw(title, titleFont, palette.Ink, S(Pad), S(20));
                    Draw(subtitle, smallFont, palette.Dim, S(Pad + 20), S(16));
                    PaintTrack(g, new RectangleF(S(TextLeft), S(58), Width - S(TextLeft) - S(LogoLeft), S(BarHeight)), all);
                    break;
                case Mode.Done:
                    if (subtitle.Length == 0)
                    {
                        Draw(title, titleFont, palette.Ink, (Height - S(20)) / 2, S(20));
                    }
                    else
                    {
                        Draw(title, titleFont, palette.Ink, S(Pad), S(20));
                        Draw(subtitle, smallFont, palette.Dim, S(Pad + 20), S(16));
                    }

                    break;
                default:
                    Draw(title, titleFont, palette.Ink, S(Pad), S(20));
                    Draw(subtitle, smallFont, palette.Dim, S(Pad + 20), S(16));
                    if (!retryArea.IsEmpty)
                    {
                        using var bold = Theme.Font(S(12), Weight.Bold);
                        TextRenderer.DrawText(g, Strings.Xfer_Retry, bold, retryArea, palette.Accent, Line | TextFormatFlags.NoClipping | Theme.Start);
                    }

                    break;
            }
        }

        private void PaintMark(Graphics g, Rectangle box, int radius)
        {
            var tint = mode switch { Mode.Running => palette.Accent, Mode.Done => Theme.Connected, _ => palette.Danger };
            if (Mark.Value is { } mark)
            {
                var state = g.Save();
                using var clip = Theme.Rounded(box, radius);
                g.SetClip(clip);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(mark, box);
                g.Restore(state);
                return;
            }

            using var fill = new SolidBrush(palette.AccentSoft);
            using var shape = Theme.Rounded(box, radius);
            g.FillPath(fill, shape);
            Icons.Draw(g, Icons.FileText, RectangleF.Inflate(box, -box.Width / 4f, -box.Height / 4f), tint);
        }

        private void PaintButton(Graphics g, int index, int radius)
        {
            var (kind, area) = buttons[index];
            if (index == hot)
            {
                using var fill = new SolidBrush(palette.Faint);
                using var shape = Theme.Rounded(area, radius);
                g.FillPath(fill, shape);
            }

            if (index == focused)
            {
                using var ring = new Pen(palette.Accent, 2);
                using var shape = Theme.Rounded(RectangleF.Inflate(area, -1, -1), radius);
                g.DrawPath(ring, shape);
            }

            var dpi = DeviceDpi;
            var glyph = Theme.Scale(kind == Button.Stop ? 14 : 16, dpi);
            var box = new RectangleF(area.X + ((area.Width - glyph) / 2f), area.Y + ((area.Height - glyph) / 2f), glyph, glyph);
            switch (kind)
            {
                case Button.Pause:
                    Icons.Fill(g, view.Paused ? PlayGlyph : PauseGlyph, box, palette.Ink, GlyphGrid);
                    break;
                case Button.Stop:
                    Icons.Fill(g, StopGlyph, box, palette.Danger, GlyphGrid);
                    break;
                default:
                    Icons.Draw(g, Icons.Close, box, palette.Dim);
                    break;
            }
        }

        /// <summary>The track fills from the leading edge, so in Arabic from the right.</summary>
        private void PaintTrack(Graphics g, RectangleF track, Rectangle all)
        {
            var radius = track.Height / 2f;
            using (var off = new SolidBrush(palette.Off))
            using (var shape = Theme.Rounded(Theme.Flip(track, all), radius))
            {
                g.FillPath(off, shape);
            }

            var filled = (int)Math.Round(track.Width * ToastText.Percent(view) / 100.0);
            if (filled > 0)
            {
                using var accent = new SolidBrush(palette.Accent);
                using var shape = Theme.Rounded(Theme.Flip(track with { Width = filled }, all), radius);
                g.FillPath(accent, shape);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                clock.Stop();
                clock.Dispose();
            }

            base.Dispose(disposing);
        }

        // The card is drawn, not made of controls, so a screen reader is given its three buttons here: a name, a role, and
        // the same press a click makes.
        private sealed class CardAccessible(ToastWindow card) : ControlAccessibleObject(card)
        {
            public override int GetChildCount() => card.buttons.Count;

            public override AccessibleObject? GetChild(int index) =>
                index >= 0 && index < card.buttons.Count ? new ButtonAccessible(card, this, index) : null;
        }

        private sealed class ButtonAccessible(ToastWindow card, AccessibleObject parent, int index) : AccessibleObject
        {
            private bool Live => index < card.buttons.Count;

            public override string? Name => Live ? card.NameOf(card.buttons[index].Kind) : string.Empty;

            public override AccessibleRole Role => AccessibleRole.PushButton;

            public override AccessibleObject? Parent => parent;

            public override Rectangle Bounds => Live ? card.RectangleToScreen(card.buttons[index].Area) : Rectangle.Empty;

            public override string? DefaultAction => Name;

            public override AccessibleStates State =>
                AccessibleStates.Focusable | (card.focused == index ? AccessibleStates.Focused : AccessibleStates.None);

            public override void DoDefaultAction() => card.Press(index);
        }
    }
}
