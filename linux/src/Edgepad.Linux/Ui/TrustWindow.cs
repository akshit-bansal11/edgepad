using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace Edgepad.Ui;

/// <summary>
/// The trust question, as a window: "Don't trust" is the default and Escape or closing it answers the same, and
/// "Trust" stays disabled for <see cref="TrustPrompt.TrustDelay"/> so a click already in flight cannot land on it.
/// It closes itself, as a No, when the session stops waiting. Nothing here relies on the window being on top (it
/// asks for that and may not get it, notably on Wayland): the status line and the log name the phone too.
/// </summary>
internal static class TrustWindow
{
    /// <summary>Opens the question. Call on the UI thread; the task completes when the window closes.</summary>
    public static Task<bool> Open(string address, CancellationToken stillWaiting)
    {
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // A question whose answer nobody would read must not be put on screen at all.
        if (stillWaiting.IsCancellationRequested)
        {
            answer.TrySetResult(false);
            return answer.Task;
        }

        var look = Look.Current;
        var trust = look.Button("Trust", primary: true);
        trust.IsEnabled = false;
        var dont = look.Button("Don't trust");
        dont.IsDefault = true;

        var body = new StackPanel { Margin = new Thickness(24), Spacing = 14 };
        body.Children.Add(look.Text($"Trust {address}?", 18, FontWeight.Bold));
        body.Children.Add(look.Text(
            "A paired phone is asking to control this laptop's pointer and keyboard. Trust it only if it is "
                + $"yours. With no answer in {(int)TrustPrompt.DefaultTimeout.TotalSeconds} seconds, it is not trusted.",
            14,
            dim: true));
        body.Children.Add(Look.Footer(dont, trust));
        var window = look.Dialog("Edgepad", 440, body);
        window.Topmost = true;

        var enable = new DispatcherTimer { Interval = TrustPrompt.TrustDelay };
        enable.Tick += (_, _) =>
        {
            enable.Stop();
            trust.IsEnabled = true;
        };
        trust.Click += (_, _) => Finish(window, answer, true);
        dont.Click += (_, _) => Finish(window, answer, false);
        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Finish(window, answer, false);
            }
        };
        window.Opened += (_, _) =>
        {
            window.Activate();
            dont.Focus();
            enable.Start();
        };
        window.Closed += (_, _) =>
        {
            enable.Stop();
            answer.TrySetResult(false);
        };
        stillWaiting.Register(() => Dispatcher.UIThread.Post(() => Finish(window, answer, false)));

        window.Show();
        return answer.Task;
    }

    private static void Finish(Window window, TaskCompletionSource<bool> answer, bool trusted)
    {
        answer.TrySetResult(trusted);
        try
        {
            window.Close();
        }
        catch (InvalidOperationException e)
        {
            // Already closed; the answer is set, and nothing may throw out of a handler.
            Log.Write($"The trust window was already closed: {e.Message}");
        }
    }
}
