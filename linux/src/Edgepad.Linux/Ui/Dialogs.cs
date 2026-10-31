using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Edgepad.Ui;

/// <summary>
/// The message boxes Avalonia does not have. Each completes when the window closes, however it closes, and the safe
/// answer is the default one: a stray Enter chooses "No", and so do Escape and the close button except in
/// <see cref="Ask"/>, where they choose nothing. Call on the UI thread.
/// </summary>
internal static class Dialogs
{
    /// <summary>Asks a yes/no question. True only if the owner pressed <paramref name="yes"/>.</summary>
    public static Task<bool> Confirm(Window? owner, string question, string yes, string no, bool dangerous = false)
    {
        var look = Look.Current;
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var yesButton = look.Button(yes, primary: !dangerous, danger: dangerous);
        var noButton = look.Button(no, primary: dangerous);
        noButton.IsDefault = true;
        var window = look.Dialog("Edgepad", 440, Body(look, question, Look.Footer(noButton, yesButton)));
        yesButton.Click += (_, _) => Finish(window, answer, true);
        noButton.Click += (_, _) => Finish(window, answer, false);
        Present(window, owner, answer, false, noButton);
        return answer.Task;
    }

    /// <summary>
    /// Asks a question whose answer is kept for good, so going away is not an answer: true for <paramref name="yes"/>,
    /// false for <paramref name="no"/>, null when the window closed some other way.
    /// </summary>
    public static Task<bool?> Ask(string question, string yes, string no)
    {
        var look = Look.Current;
        var answer = new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var yesButton = look.Button(yes, primary: true);
        var noButton = look.Button(no);
        noButton.IsDefault = true;
        var window = look.Dialog("Edgepad", 440, Body(look, question, Look.Footer(noButton, yesButton)));
        window.Topmost = true;
        yesButton.Click += (_, _) => Finish(window, answer, true);
        noButton.Click += (_, _) => Finish(window, answer, false);
        Present(window, null, answer, null, noButton);
        return answer.Task;
    }

    /// <summary>Shows a message with one OK button; completes when it is closed.</summary>
    public static Task Inform(Window? owner, string message)
    {
        var look = Look.Current;
        var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ok = look.Button("OK", primary: true);
        ok.IsDefault = true;
        var window = look.Dialog("Edgepad", 440, Body(look, message, Look.Footer(ok)));
        ok.Click += (_, _) => Finish(window, closed, true);
        Present(window, owner, closed, false, ok);
        return closed.Task;
    }

    private static StackPanel Body(Look look, string text, Control footer) => new()
    {
        Margin = new Thickness(24),
        Spacing = 18,
        Children = { look.Text(text, 14), footer },
    };

    private static void Present<T>(Window window, Window? owner, TaskCompletionSource<T> result, T otherwise, Control focus)
    {
        // Closing any other way gives otherwise, and finishes the task so nothing waits on a window that is gone.
        window.Closed += (_, _) => result.TrySetResult(otherwise);
        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                window.Close();
            }
        };
        window.Opened += (_, _) => focus.Focus();
        try
        {
            if (owner is null)
            {
                window.Show();
            }
            else
            {
                _ = window.ShowDialog(owner);
            }
        }
        catch (Exception e)
        {
            Log.Write($"Could not open a dialog: {e.Message}");
            result.TrySetResult(otherwise);
        }
    }

    private static void Finish<T>(Window window, TaskCompletionSource<T> result, T value)
    {
        result.TrySetResult(value);
        try
        {
            window.Close();
        }
        catch (InvalidOperationException e)
        {
            // Already closed; the result is set, and nothing may throw out of a handler.
            Log.Write($"The dialog was already closed: {e.Message}");
        }
    }
}
