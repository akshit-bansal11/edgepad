namespace Edgepad.Foreground;

/// <summary>
/// Tells the phone which app is in front while it asks to hear: at once when it starts, then on change, never faster than
/// every <see cref="Every"/> with the newest value winning. Nothing here throws, and nothing logs the app.
/// </summary>
internal sealed class ForegroundReporter : IDisposable
{
    internal static readonly TimeSpan Every = TimeSpan.FromMilliseconds(200);

    private readonly IForegroundApp source;
    private readonly Action<string> send;
    private readonly Action<TimeSpan, Action> later;
    private readonly ForegroundKind? kind;
    private readonly Lock gate = new();
    private IDisposable? watch;
    private string? pending;
    private string last = "";
    private bool watching;
    private bool cooling;
    private int generation;

    /// <param name="later">Runs the action once after the delay, on any thread. Not inline: the reporter holds its lock when it asks.</param>
    /// <param name="kind">Told the token of every app reported, and started and stopped with the watch.</param>
    public ForegroundReporter(IForegroundApp source, Action<string> send, Action<TimeSpan, Action>? later = null, ForegroundKind? kind = null)
    {
        this.source = source;
        this.send = send;
        this.later = later ?? Later;
        this.kind = kind;
    }

    /// <summary>True starts watching and sends the current app at once; false stops. Either again is a no-op.</summary>
    public void Watch(bool value)
    {
        lock (gate)
        {
            if (value == watching)
            {
                return;
            }

            generation++;
            cooling = false;
            pending = null;
            last = ""; // nothing known is what the phone assumes, so only a known app is news
            var stopped = watch;
            watch = null;
            stopped?.Dispose();
            kind?.Stop();
            watching = value;
            if (!value)
            {
                Log.Write("foreground watch off");
                return;
            }

            Log.Write("foreground watch on");
            kind?.Reset();
            kind?.Start();
            try
            {
                watch = source.Watch(Changed);
            }
            catch (Exception)
            {
                watch = null;
                watching = false;
            }
        }
    }

    public void Dispose() => Watch(false);

    private void Changed(ForegroundApp app)
    {
        string? text;
        lock (gate)
        {
            if (!watching)
            {
                return;
            }

            pending = ForegroundApp.Encode(app);
            if (cooling)
            {
                return;
            }

            text = Take();
        }

        Emit(text);
    }

    // Under the gate. Starts the cooldown only when something goes out.
    private string? Take()
    {
        var text = pending;
        pending = null;
        if (text is null || text == last)
        {
            return null;
        }

        last = text;
        cooling = true;
        var mine = generation;
        later(Every, () => Cooled(mine));
        return text;
    }

    private void Cooled(int mine)
    {
        string? text;
        lock (gate)
        {
            if (mine != generation)
            {
                return;
            }

            cooling = false;
            text = Take();
        }

        Emit(text);
    }

    private void Emit(string? text)
    {
        if (text is null)
        {
            return;
        }

        try
        {
            send(text);
        }
        catch (Exception)
        {
            // A lost report costs nothing: the next change is sent afresh.
        }

        kind?.SetForeground(text.Split('/')[0]);
    }

    private static void Later(TimeSpan delay, Action action) =>
        _ = Task.Delay(delay).ContinueWith(static (_, state) => ((Action)state!)(), action, TaskScheduler.Default);
}
