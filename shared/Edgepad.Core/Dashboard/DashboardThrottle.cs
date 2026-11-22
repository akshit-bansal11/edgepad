namespace Edgepad.Dashboard;

/// <summary>
/// What the dashboard limits for the whole process, not for one phone link: a link's <see cref="IntegrationHost"/> is rebuilt whenever
/// a switch changes (the phone can flip developer mode at will), so a count or a timestamp kept in the host would reset with it.
/// A platform holds one of these for its lifetime and hands it to every <see cref="DashboardParts"/> it makes.
/// </summary>
internal sealed class DashboardThrottle
{
    public const int MaxOpens = 3;
    public static readonly TimeSpan OpenWindow = TimeSpan.FromSeconds(10);

    private readonly Lock gate = new();
    private readonly Queue<DateTimeOffset> opens = new();
    private readonly Dictionary<string, DateTimeOffset> refreshed = [];

    /// <summary>True, and counts it, when fewer than <see cref="MaxOpens"/> links were opened in the last <see cref="OpenWindow"/>, by any phone.</summary>
    public bool AllowOpen(DateTimeOffset now)
    {
        lock (gate)
        {
            while (opens.Count > 0 && now - opens.Peek() >= OpenWindow)
            {
                opens.Dequeue();
            }

            if (opens.Count >= MaxOpens)
            {
                return false;
            }

            opens.Enqueue(now);
            return true;
        }
    }

    /// <summary>When the git host <paramref name="host"/> was last asked for a refresh; null when never (or after <see cref="ForgetRefresh"/>).</summary>
    public DateTimeOffset? LastRefresh(string host)
    {
        lock (gate)
        {
            return refreshed.TryGetValue(host, out var at) ? at : null;
        }
    }

    public void Refreshed(string host, DateTimeOffset at)
    {
        lock (gate)
        {
            refreshed[host] = at;
        }
    }

    /// <summary>Lets the next refresh of <paramref name="host"/> run at once (a sign-in just finished).</summary>
    public void ForgetRefresh(string host)
    {
        lock (gate)
        {
            _ = refreshed.Remove(host);
        }
    }
}
