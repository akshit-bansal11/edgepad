using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Edgepad.Ui;

/// <summary>
/// The 60 s the owner has to cancel a shutdown the phone armed for the end of the media. Topmost, with Cancel as the
/// default button, so Enter or Space cancels; closing the window any other way cancels too. Nothing runs outside the
/// minute: a 250 ms timer lives only while the window is open. The text is plain English until 4.5.0 localisation.
/// </summary>
internal static class ShutdownCountdown
{
    public const int Seconds = 60;

    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(250);

    // UI thread only.
    private static Window? open;

    /// <summary>Puts the countdown up, once; a second call while it is open does nothing. From any thread.</summary>
    public static void Show(Action shutDown) => Dispatcher.UIThread.Post(() => Open(shutDown));

    /// <summary>Whole seconds left, rounded up, never below zero.</summary>
    internal static int SecondsLeft(long deadlineMs, long nowMs) => (int)Math.Max(0, (deadlineMs - nowMs + 999) / 1000);

    // plain English until 4.5.0 localisation
    internal static string Message(int seconds) => $"The media ended. This laptop shuts down in {seconds} seconds.";

    private static void Open(Action shutDown)
    {
        if (open is not null)
        {
            return;
        }

        var look = Look.Current;
        var deadline = Environment.TickCount64 + (Seconds * 1000L);
        var text = look.Text(Message(Seconds), 14);
        var cancel = look.Button("Cancel", primary: true); // plain English until 4.5.0 localisation
        cancel.IsDefault = true;
        var window = look.Dialog("Shutting down", 400, new StackPanel // plain English until 4.5.0 localisation
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
            text.Text = Message(left);
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
            if (due)
            {
                // Off the UI thread: the call waits on the bus.
                _ = Task.Run(shutDown);
            }
        };
        open = window;
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
        }
    }
}
