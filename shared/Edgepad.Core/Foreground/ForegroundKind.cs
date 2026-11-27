namespace Edgepad.Foreground;

/// <summary>
/// Works out which kind of app is in front, "media", "timeline", "browser" or empty, from the foreground token and from which
/// app the OS says is playing, and sends it only when it changes. Does nothing until <see cref="Start"/>; nothing here logs the
/// app. <c>samePlayer</c> says whether a foreground token and a media source name are the same app.
/// </summary>
internal sealed class ForegroundKind(string os, Func<string, string, bool> samePlayer, Action<string> send)
{
    private readonly Lock gate = new();
    private bool started;
    private string token = "";
    private string? source;
    private string last = "";

    public void Start()
    {
        lock (gate)
        {
            started = true;
        }
    }

    public void Stop()
    {
        lock (gate)
        {
            started = false;
        }
    }

    /// <summary>Forgets the token, the player and what was sent, so the next kind is news again.</summary>
    public void Reset()
    {
        lock (gate)
        {
            token = "";
            source = null;
            last = "";
        }
    }

    /// <summary>The token of the app now in front, empty for none.</summary>
    public void SetForeground(string value)
    {
        lock (gate)
        {
            token = value;
        }

        Recompute();
    }

    /// <summary>What the OS names the playing session's app, null when nothing plays.</summary>
    public void SetPlaying(string? value)
    {
        lock (gate)
        {
            source = value;
        }

        Recompute();
    }

    private void Recompute()
    {
        string kind;
        lock (gate)
        {
            if (!started)
            {
                return;
            }

            var playing = token.Length > 0 && source is not null && samePlayer(token, source);
            kind = AppKinds.Resolve(token.Length > 0 ? AppKinds.Of(os, token) : null, playing);
            if (kind == last)
            {
                return;
            }

            last = kind;
        }

        try
        {
            send(kind);
        }
        catch (Exception)
        {
            // A lost report costs nothing: the next change is sent afresh.
        }
    }
}
