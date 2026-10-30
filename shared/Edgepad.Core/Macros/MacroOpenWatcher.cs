namespace Edgepad.Macros;

/// <summary>
/// Tells the phone which macro apps are running while its grid is open: one survey every <paramref name="every"/>,
/// and one character per slot, sent only when the string changes. It runs on a timer thread, where an escape ends
/// the process, so <see cref="Poll"/> never throws.
/// </summary>
internal sealed class MacroOpenWatcher(
    Func<IReadOnlyList<Macro>> macros, IMacroWindows windows, Action<string> send, TimeSpan every) : IDisposable
{
    private readonly Lock gate = new();
    private readonly Lock polling = new();
    private System.Threading.Timer? timer;
    private volatile bool stopped;
    private string? last;

    /// <summary>Polls now and then on the interval. The phone hears the state afresh, whatever it was last told.</summary>
    public void Start()
    {
        lock (gate)
        {
            if (timer is null)
            {
                stopped = false;
                last = null;
                timer = new System.Threading.Timer(_ => Poll(), null, TimeSpan.Zero, every);
            }
        }
    }

    public void Stop() => Dispose();

    public void Dispose()
    {
        lock (gate)
        {
            stopped = true;
            timer?.Dispose();
            timer = null;
        }
    }

    /// <summary>One survey, and one send if it changed anything. A tick that finds the last one still running skips.</summary>
    public void Poll()
    {
        if (stopped || !polling.TryEnter())
        {
            return;
        }

        try
        {
            var text = Survey();
            if (text != last)
            {
                // Noted before the send: a dead link would otherwise be retried, and logged, on every tick.
                last = text;
                send(text);
            }
        }
        catch (Exception e)
        {
            Log.Write($"The macro window state could not be sent: {e.Message}");
        }
        finally
        {
            polling.Exit();
        }
    }

    private string Survey()
    {
        var current = macros();
        try
        {
            return string.Concat(windows.Survey(current).Select(Wire));
        }
        catch (Exception e)
        {
            var nothing = new string(Wire(OpenState.Untracked), current.Count);
            if (nothing != last)
            {
                Log.Write($"The macro windows could not be surveyed: {e.Message}");
            }

            return nothing;
        }
    }

    private static char Wire(OpenState state) => state switch
    {
        OpenState.Closed => '0',
        OpenState.Open => '1',
        OpenState.Front => '2',
        _ => '-',
    };
}
