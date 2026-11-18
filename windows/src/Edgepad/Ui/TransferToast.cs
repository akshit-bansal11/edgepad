using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Text;
using Edgepad.Controls;
using Edgepad.Transfer;

namespace Edgepad.Ui;

/// <summary>
/// The words of a transfer toast, with no window behind them, so a table can pin every phase and every failure. Everything
/// comes from <see cref="Strings"/>; nothing here is a literal a translator could not reach.
/// </summary>
internal static class ToastText
{
    private const int NameMax = 32;

    // Hit once per progress tick, so parsed once.
    private static readonly CompositeFormat Count = CompositeFormat.Parse(Strings.Xfer_Count);
    private static readonly CompositeFormat DetailFormat = CompositeFormat.Parse(Strings.Xfer_Detail);
    private static readonly CompositeFormat DetailNoSpeed = CompositeFormat.Parse(Strings.Xfer_DetailNoSpeed);
    private static readonly CompositeFormat PercentFormat = CompositeFormat.Parse(Strings.Xfer_Percent);

    /// <summary>The share of the current file moved so far, 0-100.</summary>
    public static int Percent(TransferView view) =>
        view.Size == 0 ? 0 : (int)Math.Min(100UL, view.Sent * 100 / view.Size);

    public static string Title(TransferView view)
    {
        if (view.Phase == TransferPhase.Running)
        {
            return view.Cancelling ? Strings.Xfer_Cancelling : view.Sending ? Strings.Xfer_Sending : Strings.Xfer_Receiving;
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
            return Strings.Xfer_SentSome.Fill(view.Done, view.Done + failed);
        }

        return failed == 1
            ? Strings.Xfer_FailedOne.Fill(FileNames.Middle(view.Failed[0].Name, NameMax))
            : Strings.Xfer_FailedMany.Fill(failed);
    }

    public static string Subtitle(TransferView view)
    {
        if (view.Phase == TransferPhase.Running)
        {
            var name = FileNames.Middle(view.Name, NameMax);
            return view.Total > 0
                ? string.Format(CultureInfo.CurrentCulture, Count, view.Index, view.Total) + " · " + name
                : name;
        }

        if (view.Failed.Count > 0)
        {
            var first = view.Failed[0];
            return view.Done == 0 && view.Failed.Count == 1
                ? Reason(first.Why)
                : FileNames.Middle(first.Name, NameMax) + " · " + Reason(first.Why);
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

    /// <summary>"37% · 12.4 MB of 33 MB · 4.2 MB/s"; the speed is left out until the meter has one.</summary>
    public static string Detail(TransferView view)
    {
        var percent = string.Format(CultureInfo.CurrentCulture, PercentFormat, Percent(view));
        var moved = Bytes(view.Sent);
        var size = Bytes(view.Size);
        var rest = view.BytesPerSecond > 0
            ? string.Format(CultureInfo.CurrentCulture, DetailFormat, moved, size, Bytes(view.BytesPerSecond))
            : string.Format(CultureInfo.CurrentCulture, DetailNoSpeed, moved, size);
        return percent + " · " + rest;
    }

    /// <summary>1024-based units: "0 B", "1023 B", "1.0 KB", "12.4 MB". The number follows the current culture.</summary>
    public static string Bytes(ulong bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? string.Create(CultureInfo.CurrentCulture, $"{bytes} B")
            : string.Create(CultureInfo.CurrentCulture, $"{value:0.0} {units[unit]}");
    }
}

/// <summary>
/// The progress and result of a batch of transfers, as a small card in the corner of the screen: never takes focus, is not in
/// Alt-Tab or the taskbar, and sits above the taskbar. Same lifecycle as <see cref="LevelOverlay"/>: <see cref="Show"/> may be
/// called from any thread (it only posts, so it is safe from inside the feed's event), the window is built once on the UI
/// thread, repainted, rebuilt after a throw, and given up on after three throws in a row.
/// </summary>
internal sealed class TransferToast : IDisposable
{
    // Measurements at 96 dpi.
    internal const int CardWidth = 360;
    internal const int RunningHeight = 88;
    internal const int DoneHeight = 76;
    internal const int FailedHeight = 104;

    private const int MaxFailures = 3;

    private readonly SynchronizationContext ui;
    private ToastWindow? window;
    private int failures;
    private volatile bool stopped;
    private TransferView? latest;
    private int posted;

    public TransferToast() => ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

    /// <summary>Cancel was pressed. Set before the first <see cref="Show"/>; read on the UI thread.</summary>
    public Action? OnCancel { get; set; }

    public Action? OnRetry { get; set; }

    public Action? OnClose { get; set; }

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

    private sealed class ToastWindow : Form
    {
        private const int WsExToolWindow = 0x00000080;
        private const int WsExNoActivate = 0x08000000;
        private const int Corner = 12;
        private const int EdgeMargin = 16;
        private const int Badge = 32;
        private const int TextLeft = 60;
        private const int ButtonHeight = 32;
        private const int ButtonGap = 8;
        private const int HideAfterMs = 4000;
        private const TextFormatFlags Line =
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter;

        private readonly TransferToast owner;

        // One timer, started only while a finished toast waits to hide itself; stopped the moment it hides.
        private readonly System.Windows.Forms.Timer clock = new() { Interval = 25 };

        private Palette palette = Palette.Current;
        private TransferView view = TransferView.Idle;
        private Mode mode;
        private bool built;
        private bool cancelClicked;
        private long hideAt;
        private (Mode, int, int, bool, int, int)? announced;
        private FlatButton? cancel;
        private FlatButton? retry;
        private FlatButton? close;

        public ToastWindow(TransferToast owner)
        {
            this.owner = owner;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            RightToLeft = Theme.Direction;
            AccessibleRole = AccessibleRole.Alert;
            clock.Tick += OnTick;
        }

        // The toast is clickable (Cancel, Try again, Close) but must never take the foreground from what the owner is doing.
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
            var current = Palette.Current;
            if (was != mode || current != palette || !built)
            {
                palette = current;
                BackColor = palette.Card;
                RebuildButtons();
                built = true;
            }

            Relayout();
            if (!Visible)
            {
                Opacity = 1;
                Show();
            }

            Opacity = 1;
            if (mode == Mode.Done)
            {
                if (was != Mode.Done)
                {
                    hideAt = Environment.TickCount64 + HideAfterMs;
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

        private void Dismiss()
        {
            clock.Stop();
            built = false;
            cancelClicked = false;
            if (Visible)
            {
                Hide();
            }
        }

        private void RebuildButtons()
        {
            foreach (var old in new[] { cancel, retry, close })
            {
                old?.Dispose();
            }

            cancel = retry = close = null;
            if (mode == Mode.Running && view.Sending && owner.OnCancel is not null)
            {
                cancel = Add(Strings.Common_Cancel, ButtonKind.Outline, () =>
                {
                    cancelClicked = true;
                    view = view with { Cancelling = true };
                    if (cancel is not null)
                    {
                        cancel.Enabled = false;
                    }

                    owner.OnCancel?.Invoke();
                    Announce();
                    Invalidate();
                });
            }
            else if (mode == Mode.Failed)
            {
                if (owner.OnRetry is not null)
                {
                    retry = Add(Strings.Xfer_Retry, ButtonKind.Primary, () => owner.OnRetry?.Invoke());
                }

                close = Add(Strings.Xfer_Close, ButtonKind.Ghost, () => owner.OnClose?.Invoke());
            }
        }

        private FlatButton Add(string text, ButtonKind kind, Action click)
        {
            var button = new FlatButton(text, kind, palette) { TabStop = false };
            button.Click += (_, _) => click();
            Controls.Add(button);
            return button;
        }

        private void Relayout()
        {
            var dpi = DeviceDpi;
            var height = Theme.Scale(mode switch { Mode.Running => RunningHeight, Mode.Done => DoneHeight, _ => FailedHeight }, dpi);
            var size = new Size(Theme.Scale(CardWidth, dpi), height);
            var work = LevelOverlay.Metrics.Where();
            var margin = Theme.Scale(EdgeMargin, dpi);
            var where = new Rectangle(new Point(work.Right - size.Width - margin, work.Bottom - size.Height - margin), size);

            var bh = Theme.Scale(ButtonHeight, dpi);
            var right = size.Width - margin;
            if (mode == Mode.Running && cancel is not null)
            {
                var bw = cancel.FitWidth(dpi);
                cancel.Bounds = Theme.Flip(new Rectangle(right - bw, (height - bh) / 2, bw, bh), new Rectangle(Point.Empty, size));
            }
            else if (mode == Mode.Failed)
            {
                var bottom = height - Theme.Scale(12, dpi) - bh;
                var all = new Rectangle(Point.Empty, size);
                if (close is not null)
                {
                    var bw = close.FitWidth(dpi);
                    close.Bounds = Theme.Flip(new Rectangle(right - bw, bottom, bw, bh), all);
                    right -= bw + Theme.Scale(ButtonGap, dpi);
                }

                if (retry is not null)
                {
                    var bw = retry.FitWidth(dpi);
                    retry.Bounds = Theme.Flip(new Rectangle(right - bw, bottom, bw, bh), all);
                }
            }

            if (Bounds != where)
            {
                Bounds = where;
                using var shape = Theme.Rounded(new RectangleF(0, 0, size.Width, size.Height), Theme.Scale(Corner, dpi));
                Region = new Region(shape);
            }
        }

        private void Announce()
        {
            var key = (mode, view.Failed.Count, view.Done, view.Cancelling, view.Index, ToastText.Percent(view) / 10);
            if (key == announced)
            {
                return;
            }

            announced = key;
            AccessibleName = ToastText.Title(view) + ". " + ToastText.Subtitle(view);
            AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
        }

        private void OnTick(object? sender, EventArgs e)
        {
            // Resting the pointer on the card holds it open.
            if (Bounds.Contains(Cursor.Position))
            {
                hideAt = Environment.TickCount64 + HideAfterMs;
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
            var pad = S(EdgeMargin);
            var title = ToastText.Title(view);
            var subtitle = ToastText.Subtitle(view);

            var (glyph, tint) = mode switch
            {
                Mode.Running => (Icons.Smartphone, palette.Accent),
                Mode.Done => (Icons.Check, Theme.Connected),
                _ => (Icons.Close, palette.Danger),
            };
            var soft = mode == Mode.Running ? palette.AccentSoft : Color.FromArgb(palette.HighContrast ? 255 : 40, tint);
            var badgeTop = mode switch { Mode.Running => (Height - S(Badge)) / 2, Mode.Done => (Height - S(Badge)) / 2, _ => S(14) };
            var badge = Theme.Flip(new RectangleF(pad, badgeTop, S(Badge), S(Badge)), all);
            using (var fill = new SolidBrush(soft))
            {
                g.FillEllipse(fill, badge);
            }

            var inset = S(8);
            Icons.Draw(g, glyph, RectangleF.Inflate(badge, -inset, -inset), tint);

            var textRight = mode == Mode.Running && cancel is not null ? Width - pad - cancel.Width - S(12) : Width - pad;
            var textWidth = textRight - S(TextLeft);
            var titleFont = Theme.Font(S(14), Weight.Bold);
            var smallFont = Theme.Font(S(12), Weight.Regular);
            var tinyFont = Theme.Font(S(11), Weight.Regular);

            void Draw(string text, Font font, Color colour, int top, int lineHeight) =>
                TextRenderer.DrawText(
                    g, text, font, Theme.Flip(new Rectangle(S(TextLeft), top, textWidth, lineHeight), all), colour, Line | Theme.Start);

            switch (mode)
            {
                case Mode.Running:
                    Draw(title, titleFont, palette.Ink, S(12), S(20));
                    Draw(subtitle, smallFont, palette.Dim, S(32), S(16));
                    PaintTrack(g, new RectangleF(S(TextLeft), S(54), textWidth, S(6)), all);
                    Draw(ToastText.Detail(view), tinyFont, palette.Dim, S(64), S(14));
                    break;
                case Mode.Done:
                    if (subtitle.Length == 0)
                    {
                        Draw(title, titleFont, palette.Ink, (Height - S(20)) / 2, S(20));
                    }
                    else
                    {
                        Draw(title, titleFont, palette.Ink, S(16), S(20));
                        Draw(subtitle, smallFont, palette.Dim, S(38), S(16));
                    }

                    break;
                default:
                    Draw(title, titleFont, palette.Ink, S(12), S(20));
                    Draw(subtitle, smallFont, palette.Dim, S(34), S(16));
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
    }
}
