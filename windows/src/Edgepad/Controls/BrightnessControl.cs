using System.Management;

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
