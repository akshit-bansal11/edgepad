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
        using var single = TakeLock(Path.Combine(Xdg.CacheFolder, "lock"));
        if (single is null)
        {
            Log.Write($"Edgepad {Version} not started: another copy is running");
            Console.Error.WriteLine("Edgepad is already running.");
            return 1;
        }

        Log.Write($"Edgepad {Version} starting");
        var config = Xdg.ConfigFolder;
        var trust = new TrustStore(Path.Combine(config, "trusted-phone.txt"));
        var pin = new PinStore(config);

        // Wi-Fi and USB: on unless the owner turned it off in the tray (network.txt); the pairings and this laptop's id live beside it.
        var networkSettings = NetworkSettings.ForCurrentUser(config);
        var paired = new PairedLinks(Path.Combine(config, "paired-links.txt"));
        var identity = LinkIdentity.ForCurrentUser(config);

        // SIGTERM (a logout, systemd, kill) and Ctrl+C end the UI loop, or the wait below when there is no UI,
        // instead of the process, so the server's Dispose runs and releases whatever the phone was holding down.
        using var shell = new Shell(trust, pin, networkSettings, paired, identity, Version);
        using var quit = new ManualResetEventSlim();
        using var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Stop);
        using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, Stop);
        using (var server = new LinuxServer(trust, pin, shell.AskTrust, shell.SetStatus))
        {
            shell.Attach(server);
            server.Start();

            // Disposed before the server, whose HoldOff and Resume it calls.
            using var away = new SessionWatch(server.HoldOff, server.Resume);
            try
            {
                // The tray and the dialogs: Avalonia owns this thread until Quit. The Bluetooth service runs on
                // its own threads and keeps serving whatever happens here.
                shell.Run();
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
        return 0;

        void Stop(PosixSignalContext context)
        {
            context.Cancel = true;
            quit.Set();
            shell.RequestQuit();
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
