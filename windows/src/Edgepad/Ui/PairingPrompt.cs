using Edgepad.Net;

namespace Edgepad.Ui;

/// <summary>
/// The laptop's half of a Wi-Fi or USB pairing (<see cref="IPairingPrompt"/>): a window that names the phone and asks
/// the owner to type the six digits the phone shows. It is put up on the tray's thread, which owns every window the
/// app has, so the link's thread that asks is never blocked; <paramref name="announce"/> is told the phone's name as it
/// goes up, for the notification the owner actually notices, since a tray app cannot take the foreground.
/// </summary>
internal sealed class PairingPrompt(SynchronizationContext ui, Action<string> announce) : IPairingPrompt
{
    public IPairingQuestion Show(string phoneName, Action<string> onTyped, Action onOwnerCancelled) =>
        Show(phoneName, null, onTyped, onOwnerCancelled);

    public IPairingQuestion Show(string phoneName, string? source, Action<string> onTyped, Action onOwnerCancelled)
    {
        var deadline = Environment.TickCount64 + (long)LinkResponder.OwnerTimeout.TotalMilliseconds;
        var shown = new Shown(ui);
        ui.Post(_ => shown.Open(phoneName, source, deadline, onTyped, onOwnerCancelled), null);
        announce(phoneName);
        return shown;
    }

    /// <summary>
    /// What the box keeps of what was typed or pasted: its digits, at most six. "482 913", as a phone groups the
    /// code, reads as 482913.
    /// </summary>
    public static string Digits(string typed) => DigitInput.Normalize(typed, LinkCrypto.CodeDigits);

    /// <summary>Whether <paramref name="typed"/> is a whole code, so Pair may be pressed.</summary>
    public static bool Complete(string typed) => Digits(typed).Length == LinkCrypto.CodeDigits;

    /// <summary>The countdown: whole seconds left before <paramref name="deadline"/>, rounded up, never below zero.</summary>
    public static int SecondsLeft(long deadline, long now) => (int)Math.Max(0, (deadline - now + 999) / 1000);

    /// <summary>
    /// The question LinkResponder tells how the code went and disposes when it is done; every call is posted to the
    /// tray's thread, in order, so it reaches the window after the post that opened it.
    /// </summary>
    private sealed class Shown(SynchronizationContext ui) : IPairingQuestion
    {
        // Both touched on the UI thread only.
        private PairingWindow? window;
        private string phone = "";

        // Set at once by Dispose, on the pairing's thread, so an Open already posted ahead of the withdrawal sees it.
        private volatile bool withdrawn;

        public void Open(string phoneName, string? source, long deadline, Action<string> onTyped, Action onOwnerCancelled)
        {
            // Withdrawn or out of time before the post got here: a question nobody would read the answer to must not go up.
            if (withdrawn || Environment.TickCount64 >= deadline)
            {
                return;
            }

            phone = phoneName;
            window = new PairingWindow(phoneName, source, deadline, onTyped, onOwnerCancelled);
            window.FormClosed += (_, _) => window = null;

            // Not modal, and not activated: a phone on the same Wi-Fi must not be able to take the keyboard from what the
            // owner is typing. The notification says it is there, and the window waits, on top, until it is clicked.
            window.Show();

            // Wanted but not forced on the owner: the taskbar button flashes until the window is clicked.
            var flash = new Injection.FlashInfo(window.Handle);
            _ = Injection.NativeMethods.FlashWindowEx(in flash);
        }

        public void Matched() => Post(() => window?.Tell(PairingText.Matched(phone), Outcome.Neutral));

        public void Mismatched() => Post(() => window?.Tell(PairingText.Mismatched, Outcome.Bad));

        public void Finished(bool paired) =>
            Post(() => window?.Tell(paired ? PairingText.Paired(phone) : PairingText.NotFinished, paired ? Outcome.Good : Outcome.Bad));

        public void Dispose()
        {
            withdrawn = true;
            Post(() => window?.Withdraw());
        }

        private void Post(Action work) => ui.Post(_ => work(), null);
    }
}

/// <summary>
/// "Pair Pixel? Type the code shown on the phone:": a box for six digits, Pair and Cancel, and the seconds left.
/// Exactly one answer leaves it: the digits on Pair, a cancel on Cancel or the close box, or none at all when it is
/// withdrawn or runs out, which the pairing's own timer then settles. After the digits it stays up to say how they
/// went (<see cref="Tell"/>), until the owner closes it, or by itself a moment after a pairing that worked.
/// </summary>
internal sealed class PairingWindow : Form
{
    private const int DialogWidth = 400;
    private const int Pad = 24;
    private const int Box = 36;

    private readonly Palette palette = Palette.Current;
    private readonly long deadline;
    private readonly Action<string> onTyped;
    private readonly Action onOwnerCancelled;

    private readonly Label heading = new() { AutoSize = false };
    private readonly Label origin = new() { AutoSize = false };
    private readonly Label ask = new() { AutoSize = false, Text = Strings.Pair_Ask };
    private readonly Label warn = new() { AutoSize = false, Text = PairingText.Warning };
    private readonly Label countdown = new() { AutoSize = false };
    private readonly TextBox code = new()
    {
        BorderStyle = BorderStyle.None,

        // Wide, so a paste arrives whole before DigitInput cuts it down to its digits.
        MaxLength = DigitInput.PasteLimit,
        AccessibleName = Strings.Pair_CodeName,
    };

    private readonly Framed codeFrame;
    private readonly FlatButton cancel;
    private readonly FlatButton pair;
    private readonly System.Windows.Forms.Timer tick = new() { Interval = 250 };


    // How long "Paired with Pixel." stays before the window goes by itself.
    private const int LingerMs = 2000;

    // Set by the first answer, or by withdrawing, so no second one follows.
    private bool answered;

    // Set once the window says how the code went: it no longer asks, and is no longer withdrawn.
    private bool told;

    private Outcome outcome;
    private readonly OutcomeGlyph glyph = new() { Visible = false };

    public PairingWindow(string phoneName, string? source, long deadline, Action<string> onTyped, Action onOwnerCancelled)
    {
        this.deadline = deadline;
        this.onTyped = onTyped;
        this.onOwnerCancelled = onOwnerCancelled;

        Text = Strings.Pair_Title;
        RightToLeft = Theme.Direction;
        RightToLeftLayout = Theme.IsRtl;
        heading.Text = Strings.Pair_Heading.Fill(phoneName);
        origin.Text = source ?? "";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = true;
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = palette.Card;

        codeFrame = new Framed(code, palette, radius: 8, inset: 12);
        cancel = new FlatButton(Strings.Common_Cancel, ButtonKind.Outline, palette);
        pair = new FlatButton(Strings.Pair_Button, ButtonKind.Primary, palette) { Enabled = false };
        code.KeyPress += (_, e) => e.Handled = !char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar);

        // A paste skips KeyPress.
        code.TextChanged += (_, _) => pair.Enabled = PairingPrompt.Complete(DigitInput.Apply(code, LinkCrypto.CodeDigits));

        pair.Click += (_, _) => Answer();
        cancel.Click += (_, _) => Close();
        tick.Tick += (_, _) => Count();

        // Enter only does anything once six digits are in, since Pair is disabled until then.
        AcceptButton = pair;
        CancelButton = cancel;
        Controls.AddRange([heading, origin, ask, glyph, codeFrame, countdown, warn, pair, cancel]);
        Count();
        tick.Start();
        Relayout();
    }

    /// <summary>Takes the window down without an answer: the pairing ended, or stopped waiting for one.</summary>
    public void Withdraw()
    {
        if (told)
        {
            return;
        }

        // Out of time, not called off: the owner who was reading the phone's code is told nothing was paired, rather
        // than watching the window vanish and wondering.
        if (!answered && PairingPrompt.SecondsLeft(deadline, Environment.TickCount64) == 0)
        {
            Tell(PairingText.TimedOut, Outcome.Bad);
            return;
        }

        answered = true;
        Close();
    }

    /// <summary>
    /// Says how the code went in place of the question, with Close in place of Cancel and Pair. A good outcome gets a
    /// check and the accent ink and the window then goes by itself a moment later; a bad one a cross and the danger ink.
    /// The words stay as the cue that does not rely on colour.
    /// </summary>
    public void Tell(string message, Outcome outcome)
    {
        this.outcome = outcome;
        answered = true;
        told = true;
        tick.Stop();
        ask.Text = message;
        ask.AccessibleName = message;
        countdown.Text = "";
        code.Enabled = false;
        pair.Enabled = false;
        cancel.Text = Strings.Common_Close;
        Relayout();
        if (outcome == Outcome.Good)
        {
            tick.Interval = LingerMs;
            tick.Start();
        }
    }

    private int S(int value) => Theme.Scale(value, DeviceDpi);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(Handle, palette.IsDark);
        Relayout();
    }

    /// <summary>Shown without taking the foreground, so stray keystrokes meant for another window do not land in the code box.</summary>
    protected override bool ShowWithoutActivation => true;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        code.Focus();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Relayout();
    }

    // Cancel, the close box, Alt+F4: all the owner saying no, unless an answer was already given.
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        tick.Stop();
        if (!answered)
        {
            answered = true;
            onOwnerCancelled();
        }

        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            tick.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Answer()
    {
        if (answered || !PairingPrompt.Complete(code.Text))
        {
            return;
        }

        // The window stays: the pairing tells it at once whether the code matched.
        answered = true;
        tick.Stop();
        code.Enabled = false;
        pair.Enabled = false;
        countdown.Text = Strings.Pair_Checking;
        onTyped(PairingPrompt.Digits(code.Text));
    }

    private void Count()
    {
        // Told how it went: the tick now only times the close after a pairing that worked.
        if (told)
        {
            Close();
            return;
        }

        var left = PairingPrompt.SecondsLeft(deadline, Environment.TickCount64);
        countdown.Text = Strings.Pair_ClosesIn.Fill(left);

        // The pairing's own timer refuses the phone at the same moment; this only takes the window down.
        if (left == 0)
        {
            Withdraw();
        }
    }

    private void Relayout()
    {
        var width = S(DialogWidth - (Pad * 2));
        var left = S(Pad);
        var y = S(Pad);

        void Put(Label label, float pixels, Weight weight, Color ink, int gap, int indent = 0)
        {
            label.Font = Theme.Font(Theme.Scale(pixels, DeviceDpi), weight);
            label.ForeColor = ink;
            label.BackColor = palette.Card;
            var height = TextRenderer.MeasureText(
                label.Text, label.Font, new Size(width - indent, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
            label.SetBounds(left + indent, y, width - indent, height);
            y += height + gap;
        }

        Put(heading, 18f, Weight.Bold, palette.Ink, S(6));
        if (origin.Text.Length > 0)
        {
            Put(origin, 14f, Weight.Regular, palette.Dim, S(6));
        }

        var askTop = y;
        var indent = outcome == Outcome.Neutral ? 0 : S(24);
        var askInk = outcome switch { Outcome.Good => palette.Accent, Outcome.Bad => palette.Danger, _ => palette.Dim };
        Put(ask, 14f, Weight.Regular, askInk, S(10), indent);
        glyph.Visible = outcome is Outcome.Good or Outcome.Bad;
        glyph.Set(outcome == Outcome.Good ? Icons.Check : Icons.Close, askInk, palette.Card);
        glyph.SetBounds(left, askTop, S(18), S(18));
        codeFrame.Place(new Rectangle(left, y, width, S(Box)), DeviceDpi);
        y += S(Box + 10);
        Put(countdown, 14f, Weight.Regular, palette.Dim, S(10));
        warn.Visible = !told;
        if (!told)
        {
            Put(warn, 13f, Weight.Regular, palette.Dim, S(18));
        }

        cancel.SetBounds(left + width - cancel.FitWidth(DeviceDpi), y, cancel.FitWidth(DeviceDpi), S(Box));
        pair.SetBounds(cancel.Left - S(8) - pair.FitWidth(DeviceDpi), y, pair.FitWidth(DeviceDpi), S(Box));
        ClientSize = new Size(S(DialogWidth), y + S(Box + Pad));
    }
}

/// <summary>How the pairing went, for the window to say it: nothing yet, well, or not.</summary>
internal enum Outcome
{
    Neutral,
    Good,
    Bad,
}

/// <summary>A small line icon in one colour on the card, for the pairing result.</summary>
internal sealed class OutcomeGlyph : Control
{
    private string icon = Icons.Check;
    private Color ink;

    public OutcomeGlyph()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        TabStop = false;
    }

    public void Set(string path, Color colour, Color back)
    {
        icon = path;
        ink = colour;
        BackColor = back;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        Icons.Draw(e.Graphics, icon, new RectangleF(0, 0, Width, Height), ink);
    }
}
