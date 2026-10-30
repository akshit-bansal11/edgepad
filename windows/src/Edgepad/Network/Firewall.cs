using System.ComponentModel;
using System.Diagnostics;

namespace Edgepad.Network;

/// <summary>
/// One inbound rule for this exe on every network profile, added behind Windows' own elevation prompt. The rule names
/// the program, not a port, so it covers the TCP listener and mDNS on UDP 5353 alike, and a port the listener fell back
/// to. Inbound rules already naming this exe go first, because an allow rule cannot beat the block rule a dismissed
/// Windows prompt leaves. System32's <c>cmd.exe</c> is what gets elevated, never this exe: a DLL planted beside it, in
/// Downloads for example, must never load elevated.
/// </summary>
internal static class Firewall
{
    public const string RuleName = "Edgepad";

    // ERROR_CANCELLED: the owner said no to the elevation prompt.
    private const int Declined = 1223;

    /// <summary>
    /// The arguments for <c>netsh</c> to add the rule. The exe path is the only thing put in, and it is quoted; a
    /// Windows path cannot hold a quote, so one that does is refused rather than allowed to end the quoting early.
    /// A path with a percent sign is refused too, because <c>cmd</c> would expand it.
    /// </summary>
    public static string Arguments(string exe) =>
        $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow program=\"{Quotable(exe)}\" enable=yes profile=any";

    /// <summary>The arguments for <c>netsh</c> to delete every inbound rule naming this exe, allow or block.</summary>
    public static string DeleteArguments(string exe) =>
        $"advfirewall firewall delete rule name=all dir=in program=\"{Quotable(exe)}\"";

    /// <summary>
    /// What follows <c>cmd /c</c>: the delete, then the add, which runs whether or not the delete matched and whose exit
    /// code is <c>cmd</c>'s. <c>cmd</c> strips the outer quotes, leaving the ones around each path. <c>netsh</c> goes by
    /// its full path, because <c>cmd</c> searches the current directory first.
    /// </summary>
    public static string CommandLine(string exe)
    {
        var netsh = Path.Combine(Environment.SystemDirectory, "netsh.exe");
        return $"\"\"{netsh}\" {DeleteArguments(exe)} & \"{netsh}\" {Arguments(exe)}\"";
    }

    /// <summary>
    /// Asks Windows for the rule, off the UI thread, and tells <paramref name="done"/> on the UI thread whether it is
    /// there. The owner declining the prompt is their answer, logged, not an error. Call it on the UI thread.
    /// </summary>
    public static void Allow(Action<bool> done) =>
        _ = Task.Run(RunElevated).ContinueWith(task => done(task.Result), TaskScheduler.FromCurrentSynchronizationContext());

    private static string Quotable(string exe) =>
        exe.Contains('"', StringComparison.Ordinal) || exe.Contains('%', StringComparison.Ordinal)
            ? throw new ArgumentException("An exe path with a quote or a percent sign in it", nameof(exe))
            : exe;

    private static bool RunElevated()
    {
        if (Environment.ProcessPath is not { } exe)
        {
            Log.Write("The firewall rule was not added: this process's path is unknown");
            return false;
        }

        try
        {
            // /d skips the AutoRun commands in the user's registry, which would otherwise run elevated too. /s makes the
            // outer quotes the only ones stripped, whatever the paths hold.
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), $"/d /s /c {CommandLine(exe)}")
            {
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Environment.SystemDirectory,
            };
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
