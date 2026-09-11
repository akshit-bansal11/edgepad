using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;

namespace Edgepad.Controls;

/// <summary>
/// The built-in panel's brightness, through WMI. A WMI call is far slower than an input frame, so it runs
/// on its own thread and the latest requested value wins: while one call is in flight, newer requests
/// overwrite the waiting one instead of queueing behind it. A dial drag therefore never lags further
/// behind the finger than one WMI call.
/// </summary>
internal sealed class BrightnessControl : IDisposable
{
    private const int None = -1;

    /// <summary>How often the level is read where WMI's change event cannot be subscribed.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly AutoResetEvent wake = new(initialState: false);
    private readonly Thread worker;
    private int pending = None;
    private volatile bool disposed;

    public BrightnessControl()
    {
        worker = new Thread(Run) { IsBackground = true, Name = "edgepad-brightness" };
        worker.Start();
    }

    public void Set(int percent)
    {
        Interlocked.Exchange(ref pending, percent);
        wake.Set();
    }

    /// <summary>The panel's current brightness, or null when nothing here has WMI brightness (external monitors).</summary>
    public static int? Read()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            using var monitors = searcher.Get();
            foreach (var monitor in monitors.OfType<ManagementObject>())
            {
                using (monitor)
                {
                    return Convert.ToInt32(monitor["CurrentBrightness"], CultureInfo.InvariantCulture);
                }
            }

            return null;
        }
        catch (Exception e) when (e is ManagementException or COMException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reports each change of the panel's brightness, whoever makes it (the phone, the brightness keys,
    /// Windows' own slider), until the handle is disposed. WMI raises an event for it; where that event
    /// cannot be subscribed, the level is read every <see cref="PollInterval"/> instead. Null when there is
    /// no WMI brightness at all.
    /// </summary>
    public static IDisposable? Watch(Action<int> onChange)
    {
        var last = Read();
        if (last is null)
        {
            return null;
        }

        var watcher = new ManagementEventWatcher(
            new ManagementScope(@"root\WMI"),
            new EventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
        try
        {
            watcher.EventArrived += (_, e) => onChange(Convert.ToInt32(e.NewEvent["Brightness"], CultureInfo.InvariantCulture));
            watcher.Start();
            return new Subscription(() =>
            {
                watcher.Stop();
                watcher.Dispose();
            });
        }
        catch (Exception e) when (e is ManagementException or COMException or UnauthorizedAccessException)
        {
            watcher.Dispose();
            Log.Write($"Brightness events unavailable, polling instead: {e.Message}");
            var timer = new System.Threading.Timer(
                _ =>
                {
                    if (Read() is { } level && level != last)
                    {
                        last = level;
                        onChange(level);
                    }
                },
                null,
                PollInterval,
                PollInterval);
            return new Subscription(timer.Dispose);
        }
    }

    private void Run()
    {
        while (true)
        {
            wake.WaitOne();
            if (disposed)
            {
                return;
            }

            var percent = Interlocked.Exchange(ref pending, None);
            if (percent == None)
            {
                continue;
            }

            try
            {
                Apply(percent);
            }
            catch (ManagementException e)
            {
                // External monitors have no WMI brightness; only the built-in panel does.
                Log.Write($"Brightness change failed: {e.Message}");
            }
        }
    }

    // ponytail: queries the monitor object on every call; cache it if WMI latency ever shows in the dial.
    private static void Apply(int percent)
    {
        using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
        using var monitors = searcher.Get();
        foreach (var monitor in monitors.OfType<ManagementObject>())
        {
            using (monitor)
            {
                monitor.InvokeMethod("WmiSetBrightness", [1u, (byte)percent]);
            }
        }
    }

    public void Dispose()
    {
        disposed = true;
        wake.Set();
        worker.Join(TimeSpan.FromSeconds(2));
        wake.Dispose();
    }
}
