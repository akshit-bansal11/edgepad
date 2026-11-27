using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Edgepad.Power;

namespace Edgepad.Ui;

/// <summary>
/// The 60 s the owner has to cancel a restart or shut down the phone asked for. Topmost, with Cancel as the default
/// button, so Enter or Space cancels; closing the window any other way cancels too. Nothing runs outside the minute: a
/// 250 ms timer lives only while the window is open.
/// </summary>
internal static class ShutdownCountdown
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(250);

    // UI thread only.
    private static Window? open;

    // What is counting, for a phone that reconnects; set and cleared on the UI thread, read from any.
    private static volatile Running? running;

    /// <summary>The countdown that is up, with the seconds left, or null.</summary>
    public static (PowerKind Kind, int SecondsLeft)? Pending =>
        running is { } now ? (now.Kind, SecondsLeft(now.DeadlineMs, Environment.TickCount64)) : null;

    /// <summary>
    /// Puts the countdown up, once. <paramref name="ended"/> is called exactly once, off the UI thread: true when the
    /// time ran out, false when it was cancelled or could not be shown. From any thread.
    /// </summary>
    public static void Show(PowerKind kind, int seconds, Action<bool> ended) =>
        Dispatcher.UIThread.Post(() => Open(kind, seconds, ended));

    /// <summary>Closes the window, which cancels. Nothing when none is up. From any thread.</summary>
    public static void Cancel() => Dispatcher.UIThread.Post(() => open?.Close());

    /// <summary>Whole seconds left, rounded up, never below zero.</summary>
    internal static int SecondsLeft(long deadlineMs, long nowMs) => (int)Math.Max(0, (deadlineMs - nowMs + 999) / 1000);

    internal static string Message(PowerKind kind, int seconds) =>
        Say.Format(kind == PowerKind.Restart ? LinuxStrings.Power_RestartText : LinuxStrings.Power_ShutDownText, seconds);

    private static void Open(PowerKind kind, int seconds, Action<bool> ended)
    {
        if (open is not null)
        {
            _ = Task.Run(() => ended(false));
            return;
        }

        var look = Look.Current;
        var deadline = Environment.TickCount64 + (seconds * 1000L);
        var text = look.Text(Message(kind, seconds), 14);
        var cancel = look.Button(LinuxStrings.Power_Cancel, primary: true);
        cancel.IsDefault = true;
        var title = kind == PowerKind.Restart ? LinuxStrings.Power_RestartTitle : LinuxStrings.Power_ShutDownTitle;
        var window = look.Dialog(title, 400, new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 18,
            Children = { text, Look.Footer(cancel) },
        });
        window.Topmost = true;

        var timer = new DispatcherTimer { Interval = Tick };
        var due = false;
        timer.Tick += (_, _) =>
        {
            var left = SecondsLeft(deadline, Environment.TickCount64);
            text.Text = Message(kind, left);
            if (left == 0 && !due)
            {
                due = true;
                window.Close();
            }
        };
        cancel.Click += (_, _) => window.Close();
        window.Closed += (_, _) =>
        {
            timer.Stop();
            open = null;
            running = null;

            // Off the UI thread: the restart or shut down call waits on the bus.
            _ = Task.Run(() => ended(due));
        };
        open = window;
        running = new Running(kind, deadline);
        try
        {
            window.Show();
            timer.Start();
            cancel.Focus();
        }
        catch (Exception e)
        {
            // No window means no way to cancel, so the shutdown does not happen.
            Log.Write($"Could not open the shutdown countdown, so the laptop stays on: {e.Message}");
            open = null;
            running = null;
            timer.Stop();
            _ = Task.Run(() => ended(false));
        }
    }

    private sealed record Running(PowerKind Kind, long DeadlineMs);
}
