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

    private readonly Action<string> log;
    private int failed;

    /// <param name="plan">The process that opens an address. See <see cref="Windows"/> and <see cref="Linux"/>.</param>
    /// <param name="launch">Starts it and says whether it started; tests pass a fake. Default <see cref="Process.Start(ProcessStartInfo)"/>.</param>
    /// <param name="log">Where a failed open is reported; tests pass a fake. Default the app log.</param>
    public ProcessUrlOpener(Func<Uri, ProcessStartInfo> plan, Func<ProcessStartInfo, bool>? launch = null, Action<string>? log = null)
    {
        this.plan = plan;
        this.launch = launch ?? Start;
        this.log = log ?? Log.Write;
    }

    /// <summary>Opens that were refused or could not start.</summary>
    public int Failed => Volatile.Read(ref failed);

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
            return Fail("refused, not an https address", url);
        }

        try
        {
            return launch(plan(url)) || Fail("did not start", url);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return Fail($"could not start: {e.GetType().Name}", url);
        }
    }

    // Host and path only: the query may hold a token or a search the person typed.
    private bool Fail(string why, Uri url)
    {
        Interlocked.Increment(ref failed);
        log($"Opening a link {why}: {url.Host}{url.AbsolutePath}");
        return false;
    }

    private static bool Start(ProcessStartInfo info)
    {
        using var started = Process.Start(info);
        return started is not null;
    }
}
