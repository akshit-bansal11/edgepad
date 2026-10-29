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
    public IDisposable Show(string phoneName, Action<string> onTyped, Action onOwnerCancelled)
    {
        var deadline = Environment.TickCount64 + (long)LinkResponder.OwnerTimeout.TotalMilliseconds;
        var shown = new Shown(ui);
        ui.Post(_ => shown.Open(phoneName, deadline, onTyped, onOwnerCancelled), null);
        announce(phoneName);
        return shown;
    }

    /// <summary>
    /// What the box keeps of what was typed or pasted: its digits, at most six. "482 913", as a phone groups the
    /// code, reads as 482913.
    /// </summary>
    public static string Digits(string typed)
    {
        var digits = new string([.. typed.Where(char.IsAsciiDigit)]);
        return digits.Length > LinkCrypto.CodeDigits ? digits[..LinkCrypto.CodeDigits] : digits;
    }

    /// <summary>Whether <paramref name="typed"/> is a whole code, so Pair may be pressed.</summary>
    public static bool Complete(string typed) => Digits(typed).Length == LinkCrypto.CodeDigits;

    /// <summary>The countdown: whole seconds left before <paramref name="deadline"/>, rounded up, never below zero.</summary>
    public static int SecondsLeft(long deadline, long now) => (int)Math.Max(0, (deadline - now + 999) / 1000);

    /// <summary>The handle LinkResponder disposes once it has its answer or stops waiting; that takes the window down.</summary>
    private sealed class Shown(SynchronizationContext ui) : IDisposable
    {
        // Both touched on the UI thread only.
        private PairingWindow? window;
        private bool withdrawn;

        public void Open(string phoneName, long deadline, Action<string> onTyped, Action onOwnerCancelled)
        {
            // Withdrawn or out of time before the post got here: a question nobody would read the answer to must not go up.
            if (withdrawn || Environment.TickCount64 >= deadline)
            {
                return;
            }

            window = new PairingWindow(phoneName, deadline, onTyped, onOwnerCancelled);
            window.FormClosed += (_, _) => window = null;

            // Not modal: the question waits beside the tray, and closing it disposes it.
            window.Show();
            window.Activate();
        }

        public void Dispose() => ui.Post(
            _ =>
            {
                withdrawn = true;
                window?.Withdraw();
            },
            null);
    }
}

/// <summary>
/// "Pair Pixel? Type the code shown on the phone:": a box for six digits, Pair and Cancel, and the seconds left.
/// Exactly one answer leaves it: the digits on Pair, a cancel on Cancel or the close box, or none at all when it is
/// withdrawn or runs out, which the pairing's own timer then settles.
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
    private readonly Label ask = new() { AutoSize = false, Text = "Type the code shown on the phone:" };
    private readonly Label countdown = new() { AutoSize = false };
    private readonly TextBox code = new()
    {
        BorderStyle = BorderStyle.None,

        // One more than the code, so a pasted "482 913" arrives whole before it is cut down to its digits.
        MaxLength = LinkCrypto.CodeDigits + 1,
        AccessibleName = "Code shown on the phone",
    };

    private readonly Framed codeFrame;
    private readonly FlatButton cancel;
    private readonly FlatButton pair;
    private readonly System.Windows.Forms.Timer tick = new() { Interval = 250 };

    // Set by the first answer, or by withdrawing, so no second one follows.
    private bool answered;

    public PairingWindow(string phoneName, long deadline, Action<string> onTyped, Action onOwnerCancelled)
    {
        this.deadline = deadline;
        this.onTyped = onTyped;
        this.onOwnerCancelled = onOwnerCancelled;

        Text = "Pair a phone";
        heading.Text = $"Pair {phoneName}?";
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
        cancel = new FlatButton("Cancel", ButtonKind.Outline, palette);
        pair = new FlatButton("Pair", ButtonKind.Primary, palette) { Enabled = false };
        code.KeyPress += (_, e) => e.Handled = !char.IsControl(e.KeyChar) && !char.IsAsciiDigit(e.KeyChar);

        // A paste skips KeyPress.
        code.TextChanged += (_, _) =>
        {
            var digits = PairingPrompt.Digits(code.Text);
            if (digits != code.Text)
            {
                code.Text = digits;
                code.SelectionStart = digits.Length;
            }

            pair.Enabled = PairingPrompt.Complete(digits);
        };

        pair.Click += (_, _) => Answer();
        cancel.Click += (_, _) => Close();
        tick.Tick += (_, _) => Count();

        // Enter only does anything once six digits are in, since Pair is disabled until then.
        AcceptButton = pair;
        CancelButton = cancel;
        Controls.AddRange([heading, ask, codeFrame, countdown, cancel, pair]);
        Count();
        tick.Start();
        Relayout();
    }

    /// <summary>Takes the window down without an answer: the pairing ended, or stopped waiting for one.</summary>
    public void Withdraw()
    {
        answered = true;
        Close();
    }

    private int S(int value) => Theme.Scale(value, DeviceDpi);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(Handle, palette.IsDark);
        Relayout();
    }

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

        answered = true;
        onTyped(PairingPrompt.Digits(code.Text));
        Close();
    }

    private void Count()
    {
        var left = PairingPrompt.SecondsLeft(deadline, Environment.TickCount64);
        countdown.Text = $"Closes in {left} s";

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

        void Put(Label label, float pixels, Weight weight, Color ink, int gap)
        {
            label.Font = Theme.Font(Theme.Scale(pixels, DeviceDpi), weight);
            label.ForeColor = ink;
            label.BackColor = palette.Card;
            var height = TextRenderer.MeasureText(
                label.Text, label.Font, new Size(width, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
            label.SetBounds(left, y, width, height);
            y += height + gap;
        }

        Put(heading, 18f, Weight.Bold, palette.Ink, S(6));
        Put(ask, 14f, Weight.Regular, palette.Dim, S(10));
        codeFrame.Place(new Rectangle(left, y, width, S(Box)), DeviceDpi);
        y += S(Box + 10);
        Put(countdown, 14f, Weight.Regular, palette.Dim, S(18));

        pair.SetBounds(left + width - pair.FitWidth(DeviceDpi), y, pair.FitWidth(DeviceDpi), S(Box));
        cancel.SetBounds(pair.Left - S(8) - cancel.FitWidth(DeviceDpi), y, cancel.FitWidth(DeviceDpi), S(Box));
        ClientSize = new Size(S(DialogWidth), y + S(Box + Pad));
    }
}
