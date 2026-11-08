using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Edgepad.Net;

namespace Edgepad.Ui;

/// <summary>
/// The first-run question: whether phones may find this laptop over Wi-Fi and USB, and, as its own choice, whether to
/// share the clipboard. Both stay off until the owner answers. Only what has never been answered is asked, so an
/// install that already chose one is asked about the other alone. "Not now" is the default button, so a stray Enter
/// opens nothing; closing the window answers nothing, and the next launch asks again.
/// </summary>
internal static class FirstRunWindow
{
    public const string DocumentationUrl = "https://edgepad.akshitbansal.dev/docs";

    public const string NetworkBody =
        "Phones on the same Wi-Fi, or connected by USB tethering, will be able to find this laptop and ask to pair. "
            + "Nothing connects without the pairing code typed here.";

    /// <summary>The firewall line, for the second text block, which can be selected and copied.</summary>
    public static readonly string Firewall =
        $"A firewall may block them. Allow {NetworkSettings.DefaultPort}/tcp and 5353/udp, for example:\n"
            + $"sudo ufw allow {NetworkSettings.DefaultPort}/tcp && sudo ufw allow 5353/udp\n{DocumentationUrl}";

    /// <summary>
    /// Asks what has not been answered. Network is true for Allow and false for Not now; Clipboard is the check box as it
    /// stood when a button was pressed. Null is no answer. Call on the UI thread.
    /// </summary>
    public static Task<(bool? Network, bool? Clipboard)> Ask(bool askNetwork, bool askClipboard)
    {
        var look = Look.Current;
        var answer = new TaskCompletionSource<(bool? Network, bool? Clipboard)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allow = look.Button("Allow", primary: true);
        var notNow = look.Button("Not now");
        notNow.IsDefault = true;

        var body = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        CheckBox? share = null;
        if (askNetwork)
        {
            body.Children.Add(look.Text("Let phones find this laptop?", 18, FontWeight.Bold));
            body.Children.Add(look.Text(NetworkBody, 14));
            body.Children.Add(new SelectableTextBlock
            {
                Text = Firewall,
                FontFamily = Look.Lato,
                FontSize = 13,
                Foreground = look.Dim,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        if (askClipboard)
        {
            if (!askNetwork)
            {
                body.Children.Add(look.Text("Share clipboard?", 18, FontWeight.Bold));
            }

            // Its own choice when it sits beside the Wi-Fi question; the buttons answer the clipboard alone otherwise.
            if (askNetwork)
            {
                share = new CheckBox { Content = "Share clipboard", FontFamily = Look.Lato, FontSize = 14, Foreground = look.Ink };
                body.Children.Add(share);
            }

            body.Children.Add(look.Text("Copy on one, paste on the other: text and images.", 14, dim: true));
        }

        body.Children.Add(Look.Footer(notNow, allow));
        var window = look.Dialog("Edgepad", 460, body);
        window.Topmost = true;
        allow.Click += (_, _) => Finish(window, answer, true, share, askNetwork);
        notNow.Click += (_, _) => Finish(window, answer, false, share, askNetwork);
        window.Closed += (_, _) => answer.TrySetResult((null, null));
        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                window.Close();
            }
        };
        window.Opened += (_, _) => notNow.Focus();
        try
        {
            window.Show();
        }
        catch (Exception e)
        {
            Log.Write($"Could not open the first-run window: {e.Message}");
            answer.TrySetResult((null, null));
        }

        return answer.Task;
    }

    private static void Finish(Window window, TaskCompletionSource<(bool? Network, bool? Clipboard)> answer, bool yes, CheckBox? share, bool askNetwork)
    {
        // Beside the Wi-Fi question the buttons answer it and the check box answers the clipboard; alone, the buttons answer the clipboard.
        answer.TrySetResult(askNetwork ? (yes, share?.IsChecked) : (null, yes));
        try
        {
            window.Close();
        }
        catch (InvalidOperationException e)
        {
            Log.Write($"The first-run window was already closed: {e.Message}");
        }
    }
}
