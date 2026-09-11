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
        using var single = new Mutex(initiallyOwned: false, @"Local\Edgepad");
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
            using var tray = new TrayContext(quit);
            Application.Run(tray);
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
