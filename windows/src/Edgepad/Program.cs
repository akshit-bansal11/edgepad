namespace Edgepad;

internal static class Program
{
    /// <summary>A newer copy sets this to ask the running one to quit.</summary>
    private const string QuitEventName = @"Local\Edgepad.Quit";

    /// <summary>How long a new copy waits for the running one to hand over.</summary>
    private static readonly TimeSpan HandOver = TimeSpan.FromSeconds(10);

    /// <summary>This build's version, without the commit hash a release build appends.</summary>
    public static string Version => Application.ProductVersion.Split('+')[0];

    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write($"Unhandled: {e.ExceptionObject}");
        ApplicationConfiguration.Initialize();

        // One instance per user session: a second copy would advertise a second RFCOMM service. A new copy
        // asks the running one to quit rather than exiting silently, which used to leave an old copy running
        // after an update.
        Mutex single;
        try
        {
            single = new Mutex(initiallyOwned: false, @"Local\Edgepad");
        }
        catch (UnauthorizedAccessException)
        {
            // A copy running as administrator owns the mutex, and an ordinary copy is not allowed to open it,
            // let alone ask that copy to quit.
            Log.Write($"Edgepad {Version} not started: a copy running as administrator holds the lock");
            MessageBox.Show(
                "Edgepad is already running as administrator. Quit it from its tray icon, or run this one as administrator too.",
                "Edgepad",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        using var owned = single;
        if (!TakeOver(single))
        {
            Log.Write($"Edgepad {Version} not started: another copy is running and did not hand over");
            MessageBox.Show(
                "Another Edgepad is already running and did not hand over. Quit it from its tray icon, then run this one again.",
                "Edgepad",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        try
        {
            using var quit = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, QuitEventName);
            Log.Write($"Edgepad {Version} starting");
            var tray = new TrayContext(quit);
            try
            {
                Application.Run(tray);
            }
            finally
            {
                // Shutting down must never stop this copy from letting go of the lock.
                try
                {
                    tray.Dispose();
                }
                catch (Exception e)
                {
                    Log.Write($"Shutdown: {e}");
                }
            }
        }
        finally
        {
            single.ReleaseMutex();
        }
    }

    /// <summary>True once this copy holds the single-instance mutex, after asking a running copy to quit.</summary>
    private static bool TakeOver(Mutex single)
    {
        try
        {
            if (single.WaitOne(TimeSpan.Zero))
            {
                return true;
            }

            // Copies from before 0.3.0 do not listen for this; they keep the mutex and this copy gives up.
            if (EventWaitHandle.TryOpenExisting(QuitEventName, out var quit))
            {
                using (quit)
                {
                    quit.Set();
                }
            }

            return single.WaitOne(HandOver);
        }
        catch (AbandonedMutexException)
        {
            // The previous owner exited without releasing it; the wait still acquired it.
            return true;
        }
    }
}
