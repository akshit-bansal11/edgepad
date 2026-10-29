using System.ComponentModel;
using System.Diagnostics;

namespace Edgepad.Network;

/// <summary>
/// The tray's "Allow through Windows Firewall…": one inbound rule for this exe on private and public networks, added by
/// <c>netsh</c> behind Windows' own elevation prompt. The rule names the program, not a port, so it covers the TCP
/// listener and mDNS on UDP 5353 alike, and a port the listener fell back to.
/// </summary>
internal static class Firewall
{
    public const string RuleName = "Edgepad";

    // ERROR_CANCELLED: the owner said no to the elevation prompt.
    private const int Declined = 1223;

    /// <summary>
    /// The arguments for <c>netsh</c>. The exe path is the only thing put in, and it is quoted; a Windows path cannot
    /// hold a quote, so one that does is refused rather than allowed to end the quoting early.
    /// </summary>
    public static string Arguments(string exe)
    {
        if (exe.Contains('"', StringComparison.Ordinal))
        {
            throw new ArgumentException("An exe path with a quote in it", nameof(exe));
        }

        return $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow program=\"{exe}\" enable=yes profile=private,public";
    }

    /// <summary>
    /// Asks Windows to add the rule, off the UI thread. The owner declining the prompt is their answer, logged, not an
    /// error. Running it twice adds a second, identical rule, which changes nothing.
    /// </summary>
    public static void Allow()
    {
        if (Environment.ProcessPath is not { } exe)
        {
            Log.Write("The firewall rule was not added: this process's path is unknown");
            return;
        }

        // The full path, so a netsh.exe planted elsewhere on the search path is never the one elevated.
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "netsh.exe"), Arguments(exe))
        {
            Verb = "runas",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        _ = Task.Run(() =>
        {
            try
            {
                using var netsh = Process.Start(start);
                if (netsh is null)
                {
                    return;
                }

                netsh.WaitForExit();
                Log.Write(netsh.ExitCode == 0
                    ? "Windows Firewall now lets Edgepad take Wi-Fi and USB connections"
                    : $"netsh could not add the firewall rule: exit code {netsh.ExitCode}");
            }
            catch (Win32Exception e) when (e.NativeErrorCode == Declined)
            {
                Log.Write("The firewall rule was not added: the owner declined the Windows prompt");
            }
            catch (Exception e)
            {
                // Top-level boundary of a fire-and-forget task: an escaped exception here would be lost silently.
                Log.Write($"The firewall rule could not be added: {e.Message}");
            }
        });
    }
}
