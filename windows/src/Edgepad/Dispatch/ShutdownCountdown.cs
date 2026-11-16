using System.Globalization;
using System.Text;
using Edgepad.Ui;

namespace Edgepad.Dispatch;

/// <summary>
/// The 60 s the owner has to cancel a shutdown. Pure arithmetic on a clock the caller supplies, so a test can
/// drive it: <see cref="Poll"/> returns true exactly once, when the time is up and nobody cancelled.
/// </summary>
internal sealed class CountdownClock(int seconds, Func<long> nowMs)
{
    private readonly long deadline = nowMs() + (seconds * 1000L);
    private bool finished;

    /// <summary>Whole seconds left, rounded up, never below zero.</summary>
    public int SecondsLeft => (int)Math.Max(0, (deadline - nowMs() + 999) / 1000);

    public void Cancel() => finished = true;

    public bool Poll()
    {
        if (finished || nowMs() < deadline)
        {
            return false;
        }

        finished = true;
        return true;
    }
}

/// <summary>
/// The window that counts down to a shutdown the owner armed from the phone. Topmost, Cancel is the default button
/// and has the focus, so Enter or Space cancels. It shows at once (no animation, so inside the 300 ms reveal
/// ceiling) on a thread of its own, because the caller is the media thread.
/// </summary>
internal sealed class ShutdownCountdown : Form
{
    public const int Seconds = 60;

    private static readonly Lock Gate = new();
    private static readonly CompositeFormat ShutdownText = CompositeFormat.Parse(Strings.EndAction_ShutdownText);
    private static ShutdownCountdown? open;

    private readonly CountdownClock clock = new(Seconds, () => Environment.TickCount64);
    private readonly Label text = new() { AutoSize = true, MaximumSize = new Size(360, 0), Location = new Point(16, 16) };
    private readonly Button cancel = new() { Text = Strings.EndAction_Cancel, AutoSize = true, DialogResult = DialogResult.Cancel };
    private readonly System.Windows.Forms.Timer tick = new() { Interval = 250 };
    private readonly Action shutDown;

    private ShutdownCountdown(Action shutDown)
    {
        this.shutDown = shutDown;
        Text = Strings.EndAction_ShutdownTitle;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        TopMost = true;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(0, 0, 16, 16);
        cancel.Location = new Point(280, 64);
        CancelButton = cancel;
        AcceptButton = cancel;
        Controls.Add(text);
        Controls.Add(cancel);
        ActiveControl = cancel;
        ShowSeconds();
        tick.Tick += (_, _) => Step();
        tick.Start();
    }

    /// <summary>Puts the countdown up, once; a second call while it is open does nothing.</summary>
    public static void Show(Action shutDown)
    {
        lock (Gate)
        {
            if (open is not null)
            {
                return;
            }

            var thread = new Thread(() =>
            {
                using var form = new ShutdownCountdown(shutDown);
                lock (Gate)
                {
                    open = form;
                }

                try
                {
                    Application.Run(form);
                }
                finally
                {
                    lock (Gate)
                    {
                        open = null;
                    }
                }
            })
            { IsBackground = true, Name = "Edgepad shutdown countdown" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
    }

    /// <summary>Closes the window without shutting down (ACTION 62, or the session ending). Safe from any thread.</summary>
    public static void Dismiss()
    {
        ShutdownCountdown? form;
        lock (Gate)
        {
            form = open;
        }

        if (form is { IsHandleCreated: true, IsDisposed: false })
        {
            try
            {
                form.BeginInvoke(form.Close);
            }
            catch (InvalidOperationException)
            {
                // Closed between the check and the call: nothing left to dismiss.
            }
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        cancel.Focus();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        tick.Dispose();
        clock.Cancel();
        base.OnFormClosed(e);
    }

    private void ShowSeconds() =>
        text.Text = string.Format(CultureInfo.CurrentCulture, ShutdownText, clock.SecondsLeft);

    private void Step()
    {
        if (clock.Poll())
        {
            tick.Stop();
            Close();
            shutDown();
            return;
        }

        ShowSeconds();
    }
}
