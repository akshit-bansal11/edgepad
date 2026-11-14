using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Edgepad.Bluetooth;
using Edgepad.Net;
using Edgepad.Platform;
using Edgepad.Trust;
using Edgepad.Ui;

// On Linux only AssemblyDirectory has any meaning here, and leaving it out means libc is never loaded from a
// look-alike dropped beside the app: the system loader's own search finds it.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace Edgepad;

// The app is Linux only; saying so lets the platform analyzer check the signal and file-lock calls against Linux
// alone. On the class, not the assembly, so the tests can still call the rest from a plain net10.0 project.
[SupportedOSPlatform("linux")]
internal static class Program
{
    /// <summary>This build's version, without the commit hash a release build appends.</summary>
    private static string Version =>
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    private static int Main()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write($"Unhandled: {e.ExceptionObject}");

        // One instance per user: a second copy would register a second profile for the same service id.
        PrivateStorage.CreateDirectory(Xdg.CacheFolder);
        var lockPath = Path.Combine(Xdg.CacheFolder, "lock");
        var runningPath = Path.Combine(Xdg.CacheFolder, "running");
        using var single = TakeLock(lockPath) ?? HandleSecondLaunch(lockPath, runningPath);
        if (single is null)
        {
            return 1;
        }

        NameSelf(runningPath);

        Log.Write($"Edgepad {Version} starting");
        var config = Xdg.ConfigFolder;
        var trust = new TrustStore(Path.Combine(config, "trusted-phone.txt"));
        var pin = new PinStore(config);

        // Wi-Fi and USB: off until the owner says yes (network.txt); the pairings and this laptop's id live beside it.
        var networkSettings = NetworkSettings.ForCurrentUser(config);
        var paired = new PairedLinks(Path.Combine(config, "paired-links.txt"));
        var identity = LinkIdentity.ForCurrentUser(config);

        // SIGTERM (a logout, systemd, kill) and Ctrl+C end the UI loop, or the wait below when there is no UI,
        // instead of the process, so the server's Dispose runs and releases whatever the phone was holding down.
        using var shell = new Shell(trust, pin, networkSettings, paired, identity, Version);
        using var quit = new ManualResetEventSlim();
        using var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Stop);
        using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, Stop);
        var exitCode = 0;
        using (var server = new LinuxServer(trust, pin, shell.AskTrust, identity, shell.SetStatus))
        {
            shell.Attach(server);
            server.Start();

            // Disposed before the server, whose HoldOff and Resume it calls.
            using var away = new SessionWatch(server.HoldOff, server.Resume);
            try
            {
                // The tray and the dialogs: Avalonia owns this thread until Quit. The Bluetooth service runs on
                // its own threads and keeps serving whatever happens here. Non-zero when the tray could not be set up.
                exitCode = shell.Run();
            }
            catch (Exception e)
            {
                // No display (a session over ssh, no X11 or Wayland), or Avalonia could not start. Still serve:
                // phones already trusted connect, and any other is refused because there is nobody to ask.
                Log.Write($"The tray could not start, so Edgepad runs without one until it is stopped: {e}");
                Console.Error.WriteLine("Edgepad could not open a display; running without a tray.");
                quit.Wait();
            }

            // Before the server goes: it closes the Wi-Fi and USB links, whose sessions the server's host ends.
            shell.StopNetwork();
        }

        Log.Write($"Edgepad {Version} quitting");
        return exitCode;

        void Stop(PosixSignalContext context)
        {
            context.Cancel = true;
            quit.Set();
            shell.RequestQuit();
        }
    }

    /// <summary>
    /// Another copy holds the lock. The same version only says so (a launcher click that did nothing visible would
    /// otherwise be a mystery) and leaves; a different version asks the old one to quit and takes its place, since a
    /// second copy is how an update gets installed. Null means this copy must exit.
    /// </summary>
    private static FileStream? HandleSecondLaunch(string lockPath, string runningPath)
    {
        (string Version, int Pid)? running = null;
        try
        {
            running = SingleInstance.Parse(File.ReadAllText(runningPath));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"The running copy's file could not be read: {e.Message}");
        }

        string? exe = null;
        if (running is { } named)
        {
            try
            {
                exe = File.ResolveLinkTarget($"/proc/{named.Pid}/exe", returnFinalTarget: false)?.FullName;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Write($"Process {named.Pid} could not be inspected: {e.Message}");
            }
        }

        var same = SingleInstance.SameProgram(exe, Environment.ProcessPath);
        if (SingleInstance.Decide(running, Version, same) == SecondLaunch.TakeOver && running is { } old)
        {
            Log.Write($"Edgepad {Version} replaces {old.Version} (process {old.Pid})");
            if (Native.Libc.Terminate(old.Pid))
            {
                for (var wait = 0; wait < 50; wait++)
                {
                    Thread.Sleep(200);
                    if (TakeLock(lockPath) is { } taken)
                    {
                        return taken;
                    }
                }
            }

            Log.Write($"Edgepad {Version} not started: {old.Version} did not quit");
            Notifier.SendNow("Edgepad could not start", $"Edgepad {old.Version} is still running. Quit it from the tray, then start this one.");
            return null;
        }

        Log.Write($"Edgepad {Version} not started: another copy is running");
        Console.Error.WriteLine("Edgepad is already running. Find it in the tray.");
        Notifier.SendNow("Edgepad is already running", "Find it in the tray.");
        return null;
    }

    /// <summary>Writes this copy's version and process id for a later second launch to read. Losing it only costs the take-over.</summary>
    private static void NameSelf(string runningPath)
    {
        try
        {
            File.WriteAllText(runningPath, SingleInstance.Line(Version, Environment.ProcessId));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"This copy's file {runningPath} could not be written: {e.Message}");
        }
    }

    /// <summary>
    /// The lock file, held for the life of the process, or null when another copy holds it. FileShare.None is
    /// the stdlib's flock(LOCK_EX | LOCK_NB) on Linux; the kernel lets go of it when the process dies, so a crash
    /// leaves no stale lock behind, unlike a PID file. (Setting DOTNET_SYSTEM_IO_DISABLEFILELOCKING turns it off.)
    /// </summary>
    private static FileStream? TakeLock(string path)
    {
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
    }
}
