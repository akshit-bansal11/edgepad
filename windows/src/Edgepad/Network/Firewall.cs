using System.ComponentModel;
using System.Diagnostics;

namespace Edgepad.Network;

/// <summary>
/// One inbound rule for this exe on every network profile, added behind Windows' own elevation prompt. The rule names
/// the program, not a port, so it covers the TCP listener and mDNS on UDP 5353 alike, and a port the listener fell back
/// to. Inbound rules already naming this exe go first, because an allow rule cannot beat the block rule a dismissed
/// Windows prompt leaves. The elevated half is this same exe, run with <see cref="ElevatedArgument"/>.
/// </summary>
internal static class Firewall
{
    public const string RuleName = "Edgepad";

    /// <summary>The one argument <c>Program.Main</c> acts on. It carries no data; the path is read from the process.</summary>
    public const string ElevatedArgument = "--allow-firewall";

    // ERROR_CANCELLED: the owner said no to the elevation prompt.
    private const int Declined = 1223;

    private const int Failed = 1;

    /// <summary>
    /// The arguments for <c>netsh</c> to add the rule. The exe path is the only thing put in, and it is quoted; a
    /// Windows path cannot hold a quote, so one that does is refused rather than allowed to end the quoting early.
    /// </summary>
    public static string Arguments(string exe) =>
        $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow program=\"{Quotable(exe)}\" enable=yes profile=any";

    /// <summary>The arguments for <c>netsh</c> to delete every inbound rule naming this exe, allow or block.</summary>
    public static string DeleteArguments(string exe) =>
        $"advfirewall firewall delete rule name=all dir=in program=\"{Quotable(exe)}\"";

    /// <summary>
    /// Asks Windows for the rule, off the UI thread, and tells <paramref name="done"/> on the UI thread whether it is
    /// there. The owner declining the prompt is their answer, logged, not an error. Call it on the UI thread.
    /// </summary>
    public static void Allow(Action<bool> done) =>
        _ = Task.Run(Relaunch).ContinueWith(task => done(task.Result), TaskScheduler.FromCurrentSynchronizationContext());

    /// <summary>
    /// The elevated half: replaces the rules and returns the add's exit code. Only ever acts on this process's own path,
    /// and logs nothing, so an administrator's process writes nothing to a folder the user can redirect; the copy that
    /// asked logs the exit code.
    /// </summary>
    public static int RunElevated()
    {
        if (Environment.ProcessPath is not { } exe)
        {
            return Failed;
        }

        try
        {
            // Ignored: 1 means no rule matched, and the add below decides the outcome either way.
            _ = Netsh(DeleteArguments(exe));
            return Netsh(Arguments(exe));
        }
        catch (Win32Exception)
        {
            return Failed;
        }
    }

    private static string Quotable(string exe) => exe.Contains('"', StringComparison.Ordinal)
        ? throw new ArgumentException("An exe path with a quote in it", nameof(exe))
        : exe;

    private static int Netsh(string arguments)
    {
        // The full path, so a netsh.exe planted elsewhere on the search path is never the one run.
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "netsh.exe"), arguments) { CreateNoWindow = true };
        using var netsh = Process.Start(start);
        if (netsh is null)
        {
            return Failed;
        }

        netsh.WaitForExit();
        return netsh.ExitCode;
    }

    private static bool Relaunch()
    {
        if (Environment.ProcessPath is not { } exe)
        {
            Log.Write("The firewall rule was not added: this process's path is unknown");
            return false;
        }

        var start = new ProcessStartInfo(exe, ElevatedArgument)
        {
            Verb = "runas",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        try
        {
            using var elevated = Process.Start(start);
            if (elevated is null)
            {
                Log.Write("The firewall rule was not added: Windows started nothing");
                return false;
            }

            elevated.WaitForExit();
            Log.Write(elevated.ExitCode == 0
                ? "Windows Firewall now lets Edgepad take Wi-Fi and USB connections"
                : $"netsh could not add the firewall rule: exit code {elevated.ExitCode}");
            return elevated.ExitCode == 0;
        }
        catch (Win32Exception e) when (e.NativeErrorCode == Declined)
        {
            Log.Write("The firewall rule was not added: the owner declined the Windows prompt");
            return false;
        }
        catch (Exception e)
        {
            // Top-level boundary of a fire-and-forget task: an escaped exception here would be lost silently.
            Log.Write($"The firewall rule could not be added: {e.Message}");
            return false;
        }
    }
}
