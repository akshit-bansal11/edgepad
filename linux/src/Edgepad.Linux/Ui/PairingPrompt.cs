using Avalonia.Threading;
using Edgepad.Net;

namespace Edgepad.Ui;

/// <summary>Puts the pairing question up. Called on the UI thread; <paramref name="withdrawn"/> is cancelled when it must come down unanswered.</summary>
internal delegate void PairingOpener(string phoneName, long deadline, Action<string> onTyped, Action onOwnerCancelled, CancellationToken withdrawn);

/// <summary>
/// The laptop's half of a Wi-Fi or USB pairing (<see cref="IPairingPrompt"/>): a window that names the phone and asks
/// the owner to type the six digits the phone shows. Asking never blocks the link's thread: the window goes up on the
/// UI thread, which owns every window the app has. <paramref name="announce"/> is told the phone's name as it goes up,
/// for the status line, since a window may open behind the one in use (Wayland ignores always-on-top). Disposing the
/// handle takes the window down unanswered; a question withdrawn, or out of time, before the UI got to it is never put.
/// </summary>
internal sealed class PairingPrompt(Action<Action> ui, Action<string> announce, PairingOpener open) : IPairingPrompt
{
    /// <summary>The prompt in the app: on Avalonia's UI thread, as <see cref="PairingWindow"/>.</summary>
    public static PairingPrompt OnUiThread(Action<string> announce) =>
        new(work => Dispatcher.UIThread.Post(work), announce, PairingWindow.Open);

    public IDisposable Show(string phoneName, Action<string> onTyped, Action onOwnerCancelled)
    {
        var deadline = Environment.TickCount64 + (long)LinkResponder.OwnerTimeout.TotalMilliseconds;
        var gone = new CancellationTokenSource();
        var withdrawn = gone.Token;
        ui(() =>
        {
            if (!withdrawn.IsCancellationRequested && Environment.TickCount64 < deadline)
            {
                open(phoneName, deadline, onTyped, onOwnerCancelled, withdrawn);
            }
        });
        announce(phoneName);
        return new Withdraw(gone);
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

    private sealed class Withdraw(CancellationTokenSource gone) : IDisposable
    {
        public void Dispose()
        {
            // The token was handed out first; cancelling runs the window's own close on the UI thread.
            gone.Cancel();
            gone.Dispose();
        }
    }
}
