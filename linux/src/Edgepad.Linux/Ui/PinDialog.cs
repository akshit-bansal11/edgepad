using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Edgepad.Protocol;
using Key = Avalonia.Input.Key; // Edgepad.Protocol.Key, the KEY frame, is in scope here too

namespace Edgepad.Ui;

/// <summary>Two masked boxes that take six digits only; OK is refused until they match. The digits are never logged.</summary>
internal static class PinDialog
{
    /// <summary>
    /// The PIN typed, or null if the owner cancelled or closed the window. Call on the UI thread; the dialog is
    /// modal over <paramref name="owner"/>.
    /// </summary>
    public static Task<string?> Ask(Window owner)
    {
        var look = Look.Current;
        var result = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Box("New PIN", look);
        var again = Box("New PIN again", look);
        var problem = look.Text("", 14);
        problem.Foreground = look.Danger;
        var cancel = look.Button("Cancel");
        var ok = look.Button("Use this PIN", primary: true);
        ok.IsDefault = true;

        var body = new StackPanel { Margin = new Thickness(24), Spacing = 8 };
        body.Children.Add(look.Text("New PIN", 18, FontWeight.Bold));
        body.Children.Add(look.Text("Six digits", 14, FontWeight.Bold));
        body.Children.Add(first);
        body.Children.Add(look.Text("Again", 14, FontWeight.Bold));
        body.Children.Add(again);
        body.Children.Add(problem);
        body.Children.Add(Look.Footer(cancel, ok));
        var window = look.Dialog("New PIN", 360, body);

        ok.Click += (_, _) =>
        {
            var message = PinEntry.Problem(first.Text, again.Text);
            if (message is null)
            {
                result.TrySetResult(first.Text);
                window.Close();
                return;
            }

            problem.Text = message;
            (first.Text?.Length == Pin.Length ? again : first).Focus();
        };
        cancel.Click += (_, _) => window.Close();
        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                window.Close();
            }
        };
        window.Opened += (_, _) => first.Focus();
        window.Closed += (_, _) => result.TrySetResult(null);
        _ = window.ShowDialog(owner);
        return result.Task;
    }

    private static TextBox Box(string name, Look look)
    {
        var box = new TextBox
        {
            PasswordChar = '•',
            MaxLength = Pin.Length,
            FontFamily = Look.Lato,
            FontSize = 16,
            Foreground = look.Ink,
            Padding = new Thickness(12, 8),
        };
        AutomationProperties.SetName(box, name);

        // A paste skips any key filter, so the text itself is filtered whenever it changes.
        box.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty && PinEntry.Digits(box.Text) is var digits && digits != box.Text)
            {
                box.Text = digits;
            }
        };
        return box;
    }
}
