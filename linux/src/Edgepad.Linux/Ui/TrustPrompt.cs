namespace Edgepad.Ui;

/// <summary>
/// The decision around the trust question, apart from the window that puts it: ask, wait up to a timeout, and
/// fail closed. A phone is a keyboard, so anything but a clear yes is a no: no answer in time, a window that
/// could not open, an exception, a UI that is not up. Called on a session thread, which holds the phone's
/// HELLO_ACK until this returns; the phone waits longer than the timeout, so it hears the refusal.
/// </summary>
internal sealed class TrustPrompt(Func<string, CancellationToken, Task<bool>> show, TimeSpan timeout)
{
    /// <summary>
    /// How long the question waits for the owner. No answer is a no: a laptop left alone must not end up trusting
    /// whichever paired phone happened to try it.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    /// <summary>How long Trust stays disabled after the question appears: a click already in flight must not land on it.</summary>
    public static readonly TimeSpan TrustDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// True only if <paramref name="show"/> answered yes within the timeout. The token it is given is cancelled
    /// when this returns, so a question nobody is waiting for any more closes itself.
    /// </summary>
    public bool Ask(string address)
    {
        using var over = new CancellationTokenSource();
        try
        {
            var answer = show(address, over.Token);
            return answer.Wait(timeout) && answer.Result;
        }
        catch (Exception e)
        {
            // Refusing is the safe answer when the question cannot even be put.
            Log.Write($"Could not ask whether to trust {address}: {e.Message}");
            return false;
        }
        finally
        {
            over.Cancel();
        }
    }
}
