using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Edgepad.Power;
using Edgepad.Protocol;
using Edgepad.Ui;

namespace Edgepad.Dispatch;

/// <summary>
/// The 60 s the owner has to cancel a restart or shutdown. Pure arithmetic on a clock the caller supplies, so a test can
/// drive it: <see cref="Poll"/> returns true exactly once, when the time is up and nobody cancelled.
/// </summary>
internal sealed class CountdownClock(int seconds, Func<long> nowMs)
{
    private long deadline = nowMs() + (seconds * 1000L);
    private bool finished;

    /// <summary>Whole seconds left, rounded up, never below zero.</summary>
    public int SecondsLeft => (int)Math.Max(0, (Volatile.Read(ref deadline) - nowMs() + 999) / 1000);

    public void Cancel() => finished = true;

    /// <summary>Starts the full window again, from now.</summary>
    public void Restart() => Volatile.Write(ref deadline, nowMs() + (seconds * 1000L));

    public bool Poll()
    {
        if (finished || nowMs() < Volatile.Read(ref deadline))
        {
            return false;
        }

        finished = true;
        return true;
    }
}

/// <summary>
/// The window that counts down to a restart or shutdown the owner armed from the phone. Topmost, Cancel is the default
/// button and has the focus, so Enter or Space cancels. It shows at once (no animation, so inside the 300 ms reveal
/// ceiling) on a thread of its own, because the caller is the session's thread. One for the whole app: a session
/// ending never takes it down, and a phone that reconnects reads <see cref="Pending"/>.
/// </summary>
internal sealed partial class ShutdownCountdown : Form
{
    private static readonly Lock Gate = new();
    private static readonly CompositeFormat RestartText = CompositeFormat.Parse(Strings.Power_RestartText);
    private static readonly CompositeFormat ShutdownText = CompositeFormat.Parse(Strings.Power_ShutdownText);
    private static (PowerKind Kind, CountdownClock Clock)? pending;
    private static bool dismissed;

    private readonly CountdownClock clock;
    private readonly PowerKind kind;
    private readonly CompositeFormat format;
    private readonly Label text = new() { AutoSize = true, MaximumSize = new Size(360, 0), Location = new Point(16, 16) };
    private readonly Button cancel = new() { Text = Strings.Power_Cancel, AutoSize = true, DialogResult = DialogResult.Cancel };
    private readonly System.Windows.Forms.Timer tick = new() { Interval = 250 };
    private bool reachedZero;
    private long lastTick = Environment.TickCount64;
    private long lastAwake = AwakeMs();

    private ShutdownCountdown(PowerKind kind, CountdownClock clock)
    {
        this.clock = clock;
        this.kind = kind;
        format = kind == PowerKind.Restart ? RestartText : ShutdownText;
        Text = kind == PowerKind.Restart ? Strings.Power_RestartTitle : Strings.Power_ShutdownTitle;
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

    /// <summary>The countdown that is up, and its whole seconds left; null when none is.</summary>
    public static (PowerKind Kind, int SecondsLeft)? Pending
    {
        get
        {
            lock (Gate)
            {
                return pending is { } up ? (up.Kind, up.Clock.SecondsLeft) : null;
            }
        }
    }

    /// <summary>
    /// Puts the countdown up. False, and nothing changes, while one is already up. <c>ended</c> is called once,
    /// after the window has closed: true at zero, false when the owner or <see cref="Dismiss"/> cancelled it.
    /// </summary>
    public static bool Show(PowerKind kind, int seconds, Action<bool> ended)
    {
        lock (Gate)
        {
            if (pending is not null)
            {
                return false;
            }

            var clock = new CountdownClock(seconds, () => Environment.TickCount64);
            pending = (kind, clock);
            dismissed = false;
            var thread = new Thread(() => Run(kind, clock, ended)) { IsBackground = true, Name = "Edgepad power countdown" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return true;
        }
    }

    /// <summary>Takes the window down without restarting or shutting down (ACTION 11), within one tick. Safe from any thread.</summary>
    public static void Dismiss()
    {
        lock (Gate)
        {
            dismissed = pending is not null;
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

    /// <summary>
    /// The tick clock keeps counting while the laptop sleeps and the awake clock does not, so a gap of seconds between
    /// the two since the last tick was a sleep. Checked inside the tick, before the clock is polled: a resume event can
    /// arrive after the first tick on waking, and that tick would already see zero.
    /// </summary>
    internal static bool Slept(long tickDeltaMs, long awakeDeltaMs) => tickDeltaMs - awakeDeltaMs >= 5_000;

    /// <summary>Milliseconds awake. QueryUnbiasedInterruptTime leaves out sleep and hibernation.</summary>
    private static long AwakeMs() =>
        QueryUnbiasedInterruptTime(out var hundredNs) ? (long)(hundredNs / 10_000) : Environment.TickCount64;

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryUnbiasedInterruptTime(out ulong unbiasedTime);

    private static void Run(PowerKind kind, CountdownClock clock, Action<bool> ended)
    {
        var fired = false;
        try
        {
            using var form = new ShutdownCountdown(kind, clock);
            Application.Run(form);
            fired = form.reachedZero;
        }
        finally
        {
            lock (Gate)
            {
                pending = null;
                dismissed = false;
            }

            ended(fired);
        }
    }

    private void ShowSeconds() =>
        text.Text = string.Format(CultureInfo.CurrentCulture, format, clock.SecondsLeft);

    private void Step()
    {
        bool stop;
        lock (Gate)
        {
            stop = dismissed;
        }

        if (stop)
        {
            tick.Stop();
            Close();
            return;
        }

        // A wake gives the owner the whole window again, and tells the phone.
        var nowTick = Environment.TickCount64;
        var nowAwake = AwakeMs();
        if (Slept(nowTick - lastTick, nowAwake - lastAwake))
        {
            clock.Restart();
            WindowsPower.Report($"{(kind == PowerKind.Restart ? PowerStateToken.Restart : PowerStateToken.ShutDown)}/{PowerControl.CountdownSeconds}");
        }

        lastTick = nowTick;
        lastAwake = nowAwake;

        if (clock.Poll())
        {
            reachedZero = true;
            tick.Stop();
            Close();
            return;
        }

        ShowSeconds();
    }
}
