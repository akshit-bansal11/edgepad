using System.Collections.Concurrent;
using Edgepad.Protocol;

namespace Edgepad.Platform;

/// <summary>
/// The thread a session's slow work runs on: starting wpctl, a D-Bus call to logind or a player, loginctl. The
/// session's reader must never wait for any of that, since a frame behind it would stand still (a pointer that
/// freezes while a volume process starts). The reader only posts here and moves on. One-off jobs (a mute, a track
/// skip) run in the order posted; level changes are coalesced per control (<see cref="Coalescer{TKey,TValue}"/>), so a
/// drag of 40 values a second costs the few that the work can keep up with, always ending on the newest.
/// </summary>
internal sealed class Worker : IDisposable
{
    private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(2);

    private readonly ConcurrentQueue<Action> jobs = new();
    private readonly Coalescer<ControlId, byte> levels = new();
    private readonly SemaphoreSlim wake = new(0);
    private readonly CancellationTokenSource stopping = new();
    private readonly Action<ControlId, byte> apply;
    private readonly Thread thread;

    public Worker(Action<ControlId, byte> apply)
    {
        this.apply = apply;
        thread = new Thread(Run) { IsBackground = true, Name = "edgepad-controls" };
        thread.Start();
    }

    /// <summary>Queues a one-off job. Never blocks.</summary>
    public void Post(Action job)
    {
        jobs.Enqueue(job);
        Wake();
    }

    /// <summary>Records a level for a control, replacing one not yet applied. Never blocks.</summary>
    public void Set(ControlId control, byte percent)
    {
        levels.Post(control, percent);
        Wake();
    }

    public void Dispose()
    {
        stopping.Cancel();

        // Background thread: if a process it started is still running past the timeout, the app is not held for it.
        if (thread.Join(JoinTimeout))
        {
            wake.Dispose();
            stopping.Dispose();
        }
    }

    private void Wake()
    {
        try
        {
            wake.Release();
        }
        catch (ObjectDisposedException)
        {
            // The session ended between the frame and here: nothing is listening any more.
        }
    }

    private void Run()
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                wake.Wait(stopping.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            while (jobs.TryDequeue(out var job))
            {
                Guard(job);
            }

            foreach (var (control, percent) in levels.Take())
            {
                Guard(() => apply(control, percent));
            }
        }
    }

    // A failing job is logged and dropped: this thread must outlive any one bad call.
    private static void Guard(Action job)
    {
        try
        {
            job();
        }
        catch (Exception e)
        {
            Log.Write($"A control change failed: {e}");
        }
    }
}
