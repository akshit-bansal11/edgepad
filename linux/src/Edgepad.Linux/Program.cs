using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Edgepad.Bluetooth;
using Edgepad.Trust;

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

        // SIGTERM (a logout, systemd, kill) and Ctrl+C end the wait below instead of the process, so the server's
        // Dispose runs and releases whatever the phone was holding down.
        using var quit = new ManualResetEventSlim();
        using var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Stop);
        using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, Stop);
        using (var server = new LinuxServer(trust, pin, AskTrust, status => Log.Write($"Status: {status}")))
        {
            server.Start();
            quit.Wait();
        }

        Log.Write($"Edgepad {Version} quitting");
        return 0;

        void Stop(PosixSignalContext context)
        {
            context.Cancel = true;
            quit.Set();
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

    /// <summary>
    /// STAND-IN until the trust prompt (step 8.5): every phone not already trusted is refused. A phone is a
    /// keyboard, so with nobody to ask the answer is no. Until then a phone is trusted by writing its address
    /// into trusted-phone.txt by hand (docs/LINUX.md).
    /// </summary>
    private static bool AskTrust(string address)
    {
        Log.Write($"Not trusting {address}: this build has no trust prompt yet; add it to {Path.Combine(Xdg.ConfigFolder, "trusted-phone.txt")} by hand");
        return false;
    }
}
