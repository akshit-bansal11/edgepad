using Avalonia.Threading;
using Edgepad.Net;

namespace Edgepad.Ui;

/// <summary>Puts the pairing question up and returns the window, to be told how the code went. Called on the UI thread.</summary>
internal delegate IPairingQuestion PairingOpener(string phoneName, long deadline, Action<string> onTyped, Action onOwnerCancelled);

/// <summary>
/// The laptop's half of a Wi-Fi or USB pairing (<see cref="IPairingPrompt"/>): a window that names the phone and asks
/// the owner to type the six digits the phone shows. Asking never blocks the link's thread: the window goes up on the
/// UI thread, which owns every window the app has. <paramref name="announce"/> is told the phone's name as it goes up,
/// for the status line, since a window may open behind the one in use (Wayland ignores always-on-top). Everything the
/// pairing then tells the question is passed to the window on the UI thread, in order; disposing it takes down a window
/// still asking. A question withdrawn, or out of time, before the UI got to it is never put.
/// </summary>
internal sealed class PairingPrompt(Action<Action> ui, Action<string> announce, PairingOpener open) : IPairingPrompt
{
    /// <summary>The prompt in the app: on Avalonia's UI thread, as <see cref="PairingWindow"/>.</summary>
    public static PairingPrompt OnUiThread(Action<string> announce) =>
        new(work => Dispatcher.UIThread.Post(work), announce, PairingWindow.Open);

    public IPairingQuestion Show(string phoneName, Action<string> onTyped, Action onOwnerCancelled)
    {
        var deadline = Environment.TickCount64 + (long)LinkResponder.OwnerTimeout.TotalMilliseconds;
        var shown = new Shown(ui);
        ui(() => shown.Open(() => open(phoneName, deadline, onTyped, onOwnerCancelled), deadline));
        announce(phoneName);
        return shown;
    }

    /// <summary>
    /// What the box keeps of what was typed or pasted: its digits, at most six. "482 913", as a phone groups the
    /// code, reads as 482913.
    /// </summary>
    public static string Digits(string? typed) =>
        new([.. (typed ?? "").Where(char.IsAsciiDigit).Take(LinkCrypto.CodeDigits)]);

    /// <summary>Whether <paramref name="typed"/> is a whole code, so Pair may be pressed.</summary>
    public static bool Complete(string? typed) => Digits(typed).Length == LinkCrypto.CodeDigits;

    /// <summary>The countdown: whole seconds left before <paramref name="deadline"/>, rounded up, never below zero.</summary>
    public static int SecondsLeft(long deadline, long now) => (int)Math.Max(0, (deadline - now + 999) / 1000);

    private sealed class Shown(Action<Action> ui) : IPairingQuestion
    {
        // Touched on the UI thread only.
        private IPairingQuestion? window;

        // Set at once by Dispose, on the pairing's thread, so an Open already queued ahead of the withdrawal sees it.
        private volatile bool withdrawn;

        public void Open(Func<IPairingQuestion> opener, long deadline)
        {
            if (!withdrawn && Environment.TickCount64 < deadline)
            {
                window = opener();
            }
        }

        public void Matched() => ui(() => window?.Matched());

        public void Mismatched() => ui(() => window?.Mismatched());

        public void Finished(bool paired) => ui(() => window?.Finished(paired));

        public void Dispose()
        {
            withdrawn = true;
            ui(() => window?.Dispose());
        }
    }
}
