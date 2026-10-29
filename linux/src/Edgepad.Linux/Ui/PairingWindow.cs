using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Edgepad.Net;

namespace Edgepad.Ui;

/// <summary>
/// "Pair Pixel? Type the code shown on the phone:": a box for six digits, Pair and Cancel, and the seconds left.
/// Exactly one answer leaves it: the digits on Pair, a cancel on Cancel, Escape or the close box, or none at all when
/// it is withdrawn or runs out, which the pairing's own timer then settles. After the digits it stays up to say how
/// they went, until the owner closes it, or by itself a moment after a pairing that worked. Nothing here relies on the
/// window being on top (it asks, and Wayland may refuse). Every handler runs on the UI thread and none may throw.
/// </summary>
internal static class PairingWindow
{
    // How long "Paired with Pixel." stays before the window goes by itself.
    private static readonly TimeSpan Linger = TimeSpan.FromSeconds(2);

    /// <summary>Opens the question, and returns it to be told how the code went. Call it, and the question, on the UI thread.</summary>
    public static IPairingQuestion Open(string phoneName, long deadline, Action<string> onTyped, Action onOwnerCancelled)
    {
        var look = Look.Current;
        var code = new TextBox
        {
            // One more than the code, so a pasted "482 913" arrives whole before it is cut down to its digits.
            MaxLength = LinkCrypto.CodeDigits + 1,
            FontFamily = Look.Lato,
            FontSize = 20,
            Foreground = look.Ink,
            Padding = new Thickness(12, 8),
        };
        AutomationProperties.SetName(code, "Code shown on the phone");
        var countdown = look.Text("", 14, dim: true);
        var cancel = look.Button("Cancel");
        var pair = look.Button("Pair", primary: true);
        pair.IsEnabled = false;

        // Enter only does anything once six digits are in, since Pair is disabled until then.
        pair.IsDefault = true;
        var body = new StackPanel { Margin = new Thickness(24), Spacing = 10 };
        body.Children.Add(look.Text($"Pair {phoneName}?", 18, FontWeight.Bold));
        var ask = look.Text("Type the code shown on the phone:", 14, dim: true);
        body.Children.Add(ask);
        body.Children.Add(code);
        body.Children.Add(countdown);
        body.Children.Add(Look.Footer(cancel, pair));
        var window = look.Dialog("Pair a phone", 400, body);
        window.Topmost = true;

        // Set by the first answer, or by withdrawing, so no second one follows.
        var answered = false;

        // Set once the window says how the code went (it no longer asks, and is no longer withdrawn), and once it closed.
        var told = false;
        var closed = false;
        var tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };

        void Withdraw()
        {
            if (told || closed)
            {
                return;
            }

            answered = true;
            Close(window);
        }

        // Says how the code went in place of the question, with Close in place of Cancel and Pair.
        void Tell(string message, bool closeSoon)
        {
            if (closed)
            {
                return;
            }

            answered = true;
            told = true;
            tick.Stop();
            ask.Text = message;
            countdown.Text = "";
            code.IsEnabled = false;
            pair.IsVisible = false;
            cancel.Content = "Close";
            if (closeSoon)
            {
                tick.Interval = Linger;
                tick.Start();
            }
        }

        void Count()
        {
            // Told how it went: the tick now only times the close after a pairing that worked.
            if (told)
            {
                Close(window);
                return;
            }

            var left = PairingPrompt.SecondsLeft(deadline, Environment.TickCount64);
            countdown.Text = $"Closes in {left} s";

            // The pairing's own timer refuses the phone at the same moment; this only takes the window down.
            if (left == 0)
            {
                Withdraw();
            }
        }

        // A paste skips any key filter, so the text itself is filtered whenever it changes.
        code.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty)
            {
                return;
            }

            var digits = PairingPrompt.Digits(code.Text);
            if (digits != code.Text)
            {
                code.Text = digits;
                code.CaretIndex = digits.Length;
            }

            pair.IsEnabled = PairingPrompt.Complete(digits);
        };
        pair.Click += (_, _) =>
        {
            if (answered || !PairingPrompt.Complete(code.Text))
            {
                return;
            }

            // The window stays: the pairing tells it at once whether the code matched.
            answered = true;
            tick.Stop();
            code.IsEnabled = false;
            pair.IsEnabled = false;
            countdown.Text = "Checking the code";
            Deliver(() => onTyped(PairingPrompt.Digits(code.Text)));
        };
        cancel.Click += (_, _) => Close(window);
        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(window);
            }
        };
        tick.Tick += (_, _) => Count();
        window.Opened += (_, _) =>
        {
            window.Activate();
            code.Focus();
            Count();
            tick.Start();
        };

        // Cancel, the close box and Escape are all the owner saying no, unless an answer was already given.
        window.Closed += (_, _) =>
        {
            closed = true;
            tick.Stop();
            if (!answered)
            {
                answered = true;
                Deliver(onOwnerCancelled);
            }
        };

        window.Show();
        return new Question(phoneName, Tell, Withdraw);
    }

    private sealed class Question(string phone, Action<string, bool> tell, Action withdraw) : IPairingQuestion
    {
        public void Matched() => tell(PairingText.Matched(phone), false);

        public void Mismatched() => tell(PairingText.Mismatched, false);

        public void Finished(bool paired) => tell(paired ? PairingText.Paired(phone) : PairingText.NotFinished, paired);

        public void Dispose() => withdraw();
    }

    // The callbacks belong to the link's pairing; whatever they do, an exception must not end the UI thread.
    private static void Deliver(Action answer)
    {
        try
        {
            answer();
        }
        catch (Exception e)
        {
            Log.Write($"The pairing answer could not be delivered: {e.Message}");
        }
    }

    private static void Close(Window window)
    {
        try
        {
            window.Close();
        }
        catch (InvalidOperationException e)
        {
            // Already closed; the answer is set, and nothing may throw out of a handler.
            Log.Write($"The pairing window was already closed: {e.Message}");
        }
    }
}
