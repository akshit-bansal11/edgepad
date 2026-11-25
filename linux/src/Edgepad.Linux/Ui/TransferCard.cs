using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Edgepad.Transfer;
using GlyphPath = Avalonia.Controls.Shapes.Path;

namespace Edgepad.Ui;

/// <summary>
/// What the transfer card says about a <see cref="TransferView"/>: pure, so the window only draws it. The words are the Mac
/// panel's ("Sending to Pixel", "file · 2 of 5 · 4.2 MB/s", "Not received: it arrived damaged").
/// </summary>
internal static class TransferCardText
{
    public static string Title(TransferView view, string phone) =>
        Say.Format(view.Sending ? LinuxStrings.Xfer_PanelSending : LinuxStrings.Xfer_PanelReceiving, phone);

    /// <summary>"file · 2 of 5 · 4.2 MB/s"; the count only when the batch has more than one file, the speed only once it is known.</summary>
    public static string Detail(TransferView view)
    {
        var file = Say.MiddleTruncate(view.Name);
        if (view.Paused)
        {
            return Say.Format(LinuxStrings.Xfer_PanelHeld, file);
        }

        // Receiving has no file count: the running index is all there is to show.
        var total = Math.Max(view.Total, view.Index);
        var speed = view.BytesPerSecond > 0 ? ByteSize.Format((ulong)view.BytesPerSecond) : null;
        return (total > 1, speed) switch
        {
            (true, { } rate) => Say.Format(LinuxStrings.Xfer_PanelDetail, file, view.Index, total, rate),
            (true, null) => Say.Format(LinuxStrings.Xfer_PanelCount, file, view.Index, total),
            (false, { } rate) => Say.Format(LinuxStrings.Xfer_PanelSpeed, file, rate),
            _ => file,
        };
    }

    /// <summary>The line a finished batch leaves: "Not received: why" for the last failure, else what arrived.</summary>
    public static string Result(TransferView view) =>
        view.Failed.Count > 0
            ? Say.Format(view.Sending ? LinuxStrings.Xfer_NotSent : LinuxStrings.Xfer_NotReceived, Reason(view.Failed[^1].Why, view.Sending))
            : Say.Format(view.Sending ? LinuxStrings.Send_Sent : LinuxStrings.Balloon_FileReceived, Say.MiddleTruncate(view.Name));

    /// <summary>A card the owner closed comes back when its batch ends with a failure, so the reason is read.</summary>
    public static bool ShowsAfterClose(TransferView view) => view.Phase == TransferPhase.Finished && view.Failed.Count > 0;

    public static string Reason(TransferFailure why, bool sending) => why switch
    {
        TransferFailure.Refused => sending ? LinuxStrings.Xfer_ReasonRejectedPhone : LinuxStrings.Xfer_ReasonRejectedHere,
        TransferFailure.Damaged => LinuxStrings.Xfer_ReasonDamaged,
        TransferFailure.WriteFailed => LinuxStrings.Xfer_ReasonWrite,
        TransferFailure.Busy => LinuxStrings.Xfer_ReasonBusy,
        TransferFailure.TimedOut => LinuxStrings.Xfer_ReasonTimedOut,
        TransferFailure.LinkLost => LinuxStrings.Xfer_ReasonLost,
        TransferFailure.Unreadable => LinuxStrings.Xfer_ReasonUnreadable,
        TransferFailure.Cancelled => LinuxStrings.Xfer_ReasonCancelled,
        _ => LinuxStrings.Xfer_ReasonNotSent,
    };

    /// <summary>Share of the file moved, 0 to 1; a finished batch with no failure is full.</summary>
    public static double Fraction(TransferView view)
    {
        if (view.Phase == TransferPhase.Finished && view.Failed.Count == 0)
        {
            return 1;
        }

        return view.Size == 0 ? 0 : Math.Min(1.0, (double)view.Sent / view.Size);
    }
}

/// <summary>
/// The card for a file moving to or from the phone, as on the Mac: small, borderless, always on top, bottom-right of the
/// primary screen's work area, never takes focus. Title, file line, a 4 px bar, and Pause or Resume, Stop and Close. Close
/// only hides it, the transfer goes on, and the tray's Transfers row brings it back. A failure says why, then the card goes
/// after <see cref="LingerFailed"/>. UI thread only; the feed's changes are posted here.
/// </summary>
internal sealed class TransferCard
{
    private const double Width = 380;
    private const double Height = 78;
    private const double Margin = 16;

    private const string PauseGlyph = "M3,2 H6 V12 H3 Z M8,2 H11 V12 H8 Z";
    private const string PlayGlyph = "M3,1.5 L12,7 L3,12.5 Z";
    private const string StopGlyph = "M3,3 H11 V11 H3 Z";
    private const string CloseGlyph = "M3,3 L11,11 M11,3 L3,11";

    private static readonly TimeSpan LingerDone = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan LingerFailed = TimeSpan.FromSeconds(6);

    /// <summary>False once a window could not be shown, so the arrival notification speaks instead.</summary>
    internal static volatile bool CanShow = true;

    private readonly TransferFeed feed;
    private readonly Func<string> phoneName;
    private readonly Func<bool, bool> pause;
    private readonly Func<bool, bool> resume;
    private readonly Action<bool> stop;

    private TransferView view = TransferView.Idle;
    private string phone = "";

    // The owner pressed Close: the card stays away until the batch is over or the tray asks for it.
    private bool dismissed;
    private DispatcherTimer? lingering;
    private Window? window;
    private TextBlock? title;
    private TextBlock? detail;
    private Grid? bar;
    private Border? fill;
    private Button? pauseButton;
    private Button? stopButton;

    public TransferCard(TransferFeed feed, Func<string> phoneName, Func<bool, bool> pause, Func<bool, bool> resume, Action<bool> stop)
    {
        this.feed = feed;
        this.phoneName = phoneName;
        this.pause = pause;
        this.resume = resume;
        this.stop = stop;
        feed.Changed += next => Dispatcher.UIThread.Post(() => Apply(next));
    }

    /// <summary>The tray's Transfers row.</summary>
    public void ShowAgain()
    {
        dismissed = false;
        Apply(feed.View);
    }

    private void Apply(TransferView next)
    {
        view = next;
        lingering?.Stop();
        if (next.Phase == TransferPhase.Idle)
        {
            dismissed = false;
            Hide();
            return;
        }

        if (phoneName() is { Length: > 0 } name)
        {
            phone = name;
        }

        if (dismissed && TransferCardText.ShowsAfterClose(next))
        {
            dismissed = false;
        }

        if (dismissed)
        {
            // Closed by the owner and now over with nothing to report: the batch is let go.
            if (next.Phase == TransferPhase.Finished)
            {
                feed.Dismiss();
            }

            return;
        }

        if (!Show())
        {
            return;
        }

        Render();
        if (next.Phase == TransferPhase.Finished)
        {
            StartLinger(next.Failed.Count > 0 ? LingerFailed : LingerDone);
        }
    }

    private void StartLinger(TimeSpan after)
    {
        lingering = new DispatcherTimer { Interval = after };
        lingering.Tick += (_, _) =>
        {
            lingering?.Stop();
            feed.Dismiss();
        };
        lingering.Start();
    }

    private void Render()
    {
        if (title is null || detail is null || fill is null || bar is null || pauseButton is null || stopButton is null)
        {
            return;
        }

        var finished = view.Phase == TransferPhase.Finished;
        var failed = finished && view.Failed.Count > 0;
        title.Text = finished ? TransferCardText.Result(view) : TransferCardText.Title(view, phone);
        detail.Text = finished ? "" : TransferCardText.Detail(view);
        detail.IsVisible = !finished;
        pauseButton.IsVisible = !finished;
        stopButton.IsVisible = !finished;
        fill.Background = failed ? Look.Current.Danger : Look.Current.Accent;
        var share = Math.Clamp(TransferCardText.Fraction(view), 0, 1);
        bar.ColumnDefinitions[0].Width = new GridLength(share, GridUnitType.Star);
        bar.ColumnDefinitions[1].Width = new GridLength(1 - share, GridUnitType.Star);

        var label = view.Paused ? LinuxStrings.Xfer_Resume : LinuxStrings.Xfer_Pause;
        pauseButton.Content = Glyph(view.Paused ? PlayGlyph : PauseGlyph, Look.Current.Ink, stroke: false);
        AutomationProperties.SetName(pauseButton, label);
        ToolTip.SetTip(pauseButton, label);
    }

    private bool Show()
    {
        if (window is not null)
        {
            return true;
        }

        try
        {
            window = Build();
            window.Show();
            Place(window);
            return true;
        }
        catch (Exception e)
        {
            // No window to show (no display server answering, say): the arrival notification takes over.
            Log.Write($"The transfer card could not be shown: {e.Message}");
            CanShow = false;
            window = null;
            return false;
        }
    }

    private void Hide()
    {
        var closing = window;
        window = null;
        title = null;
        detail = null;
        bar = null;
        fill = null;
        pauseButton = null;
        stopButton = null;
        closing?.Close();
    }

    private static void Place(Window card)
    {
        if (card.Screens.Primary is not { } screen)
        {
            return;
        }

        var area = screen.WorkingArea;
        card.Position = new PixelPoint(
            area.Right - (int)Math.Round((Width + Margin) * screen.Scaling),
            area.Bottom - (int)Math.Round((Height + Margin) * screen.Scaling));
    }

    private Window Build()
    {
        var look = Look.Current;
        title = look.Text("", 13, FontWeight.SemiBold);
        title.TextWrapping = TextWrapping.NoWrap;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        detail = look.Text("", 11, dim: true);
        detail.TextWrapping = TextWrapping.NoWrap;
        detail.TextTrimming = TextTrimming.CharacterEllipsis;

        pauseButton = IconButton(look, LinuxStrings.Xfer_Pause, Glyph(PauseGlyph, look.Ink, stroke: false), () =>
        {
            var sending = view.Sending;
            _ = view.Paused ? resume(sending) : pause(sending);
        });
        stopButton = IconButton(look, LinuxStrings.Xfer_Stop, Glyph(StopGlyph, look.Danger, stroke: false), () => stop(view.Sending));
        var close = IconButton(look, LinuxStrings.Common_Close, Glyph(CloseGlyph, look.Dim, stroke: true), CloseTapped);

        fill = new Border { Background = look.Accent, CornerRadius = new CornerRadius(2) };
        bar = new Grid
        {
            Height = 4,
            Margin = new Thickness(14, 0, 14, 14),
            ColumnDefinitions = [new ColumnDefinition(0, GridUnitType.Star), new ColumnDefinition(1, GridUnitType.Star)],
        };
        bar.Children.Add(new Border { Background = look.Line, CornerRadius = new CornerRadius(2) });
        bar.Children.Add(fill);
        Grid.SetColumnSpan(bar.Children[0], 2);

        var lines = new StackPanel { Margin = new Thickness(0, 12, 8, 0), Spacing = 2, Children = { title, detail } };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(0, 8, 8, 0), VerticalAlignment = VerticalAlignment.Top, Children = { pauseButton, stopButton, close } };
        var grid = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(1, GridUnitType.Star), new ColumnDefinition(GridLength.Auto)],
            RowDefinitions = [new RowDefinition(1, GridUnitType.Star), new RowDefinition(GridLength.Auto)],
        };
        if (Mark() is { } mark)
        {
            mark.Margin = new Thickness(14, 14, 10, 0);
            grid.Children.Add(mark);
        }

        Grid.SetColumn(lines, 1);
        Grid.SetColumn(buttons, 2);
        Grid.SetRow(bar, 1);
        Grid.SetColumnSpan(bar, 3);
        grid.Children.Add(lines);
        grid.Children.Add(buttons);
        grid.Children.Add(bar);

        var card = new Window
        {
            FlowDirection = Look.Direction,
            Width = Width,
            Height = Height,
            CanResize = false,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            WindowDecorations = WindowDecorations.None,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            Background = Brushes.Transparent,
            FontFamily = Look.Lato,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Title = LinuxStrings.App_Name,
            Content = new Border
            {
                Background = look.Card,
                BorderBrush = look.Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Child = grid,
            },
        };
        Motion.RevealWindow(card);
        return card;
    }

    private void CloseTapped()
    {
        dismissed = true;
        lingering?.Stop();
        Hide();
        if (view.Phase == TransferPhase.Finished)
        {
            feed.Dismiss();
        }
    }

    private static Image? Mark()
    {
        try
        {
            var dark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
            using var stream = AssetLoader.Open(new Uri($"avares://Edgepad.Linux/Assets/{(dark ? "edgepad-64-dark" : "edgepad-64")}.png"));
            var image = new Image { Source = new Bitmap(stream), Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Top };
            return image;
        }
        catch (Exception e)
        {
            Log.Write($"The transfer card's mark could not be loaded: {e.Message}");
            return null;
        }
    }

    private static GlyphPath Glyph(string data, IBrush brush, bool stroke) => new()
    {
        Data = Geometry.Parse(data),
        Width = 14,
        Height = 14,
        Stretch = Stretch.Uniform,
        Fill = stroke ? null : brush,
        Stroke = stroke ? brush : null,
        StrokeThickness = 1.6,
        StrokeLineCap = PenLineCap.Round,
    };

    private static Button IconButton(Look look, string label, Control glyph, Action click)
    {
        var button = new Button
        {
            Content = glyph,
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Foreground = look.Ink,
        };
        AutomationProperties.SetName(button, label);
        ToolTip.SetTip(button, label);
        button.Click += (_, _) => click();
        button.PointerEntered += (_, _) => button.Opacity = 0.7;
        button.PointerExited += (_, _) => button.Opacity = 1;
        return button;
    }
}
