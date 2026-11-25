using Edgepad.Hooks;
using Edgepad.Notifications;
using Edgepad.Startup;
using Edgepad.Transfer;
using Edgepad.Trust;
using Edgepad.Ui;
using Edgepad.Updates;

// Every native library this app imports is a Windows system DLL. Searching System32 only means a look-alike
// dropped beside the exe, in Downloads say, is never loaded in its place.
[assembly: System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]

namespace Edgepad;

/// <summary>
/// The entry point. An agent's hook command and Explorer's Send to are answered and leave at once; otherwise it settles
/// which copy runs (a second launch, an update taking over, a restart as administrator) before the tray starts.
/// </summary>
internal static class Program
{
    /// <summary>A newer copy sets this to ask the running one to quit.</summary>
    private const string QuitEventName = @"Local\Edgepad.Quit";

    /// <summary>How long a new copy waits for the running one to hand over.</summary>
    private static readonly TimeSpan HandOver = TimeSpan.FromSeconds(10);

    /// <summary>This build's version, without the commit hash a release build appends.</summary>
    public static string Version => Application.ProductVersion.Split('+')[0];

    [STAThread]
    private static int Main(string[] args)
    {
        // A hook command of an agent's plugin: answers on the inherited stdout and leaves, before the mutex, logging or any UI.
        if (HookClient.IsHook(args))
        {
            return HookClient.RunConsole(args);
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write($"Unhandled: {e.ExceptionObject}");

        var launch = SingleInstance.Parse(args);
        if (launch.ProbeNotifications)
        {
            // The 4.9.0 spike: writes the listener's access status to the log and exits, touching nothing else.
            NotificationProbe.Run();
            return 0;
        }

        if (launch.SendPaths is { Count: > 0 } paths)
        {
            // Explorer's Send to: pass the paths to the running copy and leave without a word, whatever the answer.
            _ = SendPipe.Send(paths);
            return 0;
        }

        // A copy that restarted itself as administrator names the user it was started for. Another account would
        // change what HKCU means, so it refuses.
        if (!SingleInstance.RestartAllowed(launch.RestartSid, SingleInstance.CurrentUserSid()))
        {
            Log.Write($"Edgepad {Version} not started: --restart named a different user");
            return 0;
        }

        // An exception out of a UI event handler goes to the log and the app keeps running, instead of WinForms'
        // Continue/Quit box, which a tray app's owner has no way to make sense of.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Write($"UI thread: {e.Exception}");
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
            Ask.Message(
                null,
                Strings.App_RunningAsAdmin,
                "Edgepad",
                MessageBoxIcon.Information);
            return 0;
        }

        using var owned = single;
        var lockFree = Acquire(single);
        var choice = SingleInstance.Decide(lockFree, !lockFree && SameVersionRunning(), restart: launch.RestartSid is not null);
        if (choice == LaunchChoice.Notify)
        {
            // Double-clicking the exe again to "open" it must not quit the copy that is serving a phone.
            Log.Write($"Edgepad {Version} not started: this version is already running");
            Ask.Message(
                null,
                Strings.App_AlreadyRunning,
                "Edgepad",
                MessageBoxIcon.Information);
            return 0;
        }

        if (choice == LaunchChoice.TakeOver && !TakeOver(single))
        {
            Log.Write($"Edgepad {Version} not started: another copy is running and did not hand over");
            Ask.Message(
                null,
                Strings.App_DidNotHandOver,
                "Edgepad",
                MessageBoxIcon.Information);
            return 0;
        }

        // Started by an in-place update: the copy it replaced is removed once it has handed over, if the rules allow.
        if (UpdatePaths.ReplacesArgument(args) is { } replaced
            && Environment.ProcessPath is { } self
            && System.Version.TryParse(Version.Split('-')[0], out var selfVersion))
        {
            _ = WindowsUpdater.DeleteReplacedAsync(replaced, self, selfVersion);
        }

        try
        {
            using var quit = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, QuitEventName);

            // Held for as long as this copy runs, so a second launch of this version can tell it is one.
            using var versionMark = new Mutex(initiallyOwned: false, SingleInstance.VersionMutexName(Version));
            Log.Write($"Edgepad {Version} starting");

            // Up to 4.2.0 the files lived in Roaming. Moved before TrayContext builds the stores that read them, and
            // only once this copy holds the lock, so the copy that handed over is no longer writing them.
            UserFolder.Migrate(UserFolder.Legacy, UserFolder.Current, Log.Write);

            // Each release is its own file, Edgepad-<version>.exe, so the Run key can still name whichever copy
            // was running when Start with Windows was ticked. Point it at this one: the version last run is the
            // one that starts at sign-in, not the one the setting happened to be switched on from.
            if (RunAtLogin.IsEnabled)
            {
                RunAtLogin.Set(true);
            }

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

        return 0;
    }

    /// <summary>True when nothing else holds the single-instance lock, and this copy now does.</summary>
    private static bool Acquire(Mutex single)
    {
        try
        {
            return single.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            // The previous owner exited without releasing it; the wait still acquired it.
            return true;
        }
    }

    private static bool SameVersionRunning()
    {
        if (!Mutex.TryOpenExisting(SingleInstance.VersionMutexName(Version), out var mark))
        {
            return false;
        }

        mark.Dispose();
        return true;
    }

    /// <summary>True once this copy holds the single-instance mutex, after asking the running (different) copy to quit.</summary>
    private static bool TakeOver(Mutex single)
    {
        try
        {
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
