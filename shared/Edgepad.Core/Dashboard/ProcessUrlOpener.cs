using System.Diagnostics;

namespace Edgepad.Dashboard;

/// <summary>
/// Opens an address in the default browser by starting one process. Only <see cref="GitOpenGate"/> results reach it, and it still
/// refuses anything that is not an https address with a host. The address is the one argument, never part of a shell string.
/// </summary>
internal sealed class ProcessUrlOpener : IUrlOpener
{
    private readonly Func<Uri, ProcessStartInfo> plan;
    private readonly Func<ProcessStartInfo, bool> launch;

    /// <param name="plan">The process that opens an address. See <see cref="Windows"/> and <see cref="Linux"/>.</param>
    /// <param name="launch">Starts it and says whether it started; tests pass a fake. Default <see cref="Process.Start(ProcessStartInfo)"/>.</param>
    public ProcessUrlOpener(Func<Uri, ProcessStartInfo> plan, Func<ProcessStartInfo, bool>? launch = null)
    {
        this.plan = plan;
        this.launch = launch ?? Start;
    }

    /// <summary>ShellExecute on the exact address (no cmd.exe, no arguments).</summary>
    public static ProcessStartInfo Windows(Uri url) => new(url.AbsoluteUri) { UseShellExecute = true };

    /// <summary>xdg-open with the address as its one argument (no shell).</summary>
    public static ProcessStartInfo Linux(Uri url)
    {
        var info = new ProcessStartInfo("xdg-open") { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add(url.AbsoluteUri);
        return info;
    }

    public bool Open(Uri url)
    {
        if (url.Scheme != Uri.UriSchemeHttps || url.Host.Length == 0)
        {
            return false;
        }

        try
        {
            return launch(plan(url));
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            Console.Error.WriteLine($"Edgepad: could not open a link: {e.Message}");
            return false;
        }
    }

    private static bool Start(ProcessStartInfo info)
    {
        using var started = Process.Start(info);
        return started is not null;
    }
}
