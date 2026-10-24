using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;

namespace Edgepad.Platform;

/// <summary>A finished child process: its exit code and what it wrote to standard output.</summary>
internal sealed record ProcResult(int ExitCode, string Output);

/// <summary>
/// Runs a command-line tool and collects its output. Never through a shell: the file and each argument go to the
/// kernel as they are, so nothing in an argument can be read as syntax. Every call has a timeout and leaves no child
/// behind (a process that overruns is killed and reaped), and a tool that is not installed answers null instead of
/// throwing. Blocking, so it is only ever called from a worker thread, never from the session's reader.
/// </summary>
internal static class Proc
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);
    private static readonly ConcurrentDictionary<string, bool> Reported = new();

    /// <summary>
    /// The result, or null when the tool could not be started (not installed) or did not finish within
    /// <paramref name="timeout"/>. A tool that ran and failed is a result with a non-zero exit code.
    /// </summary>
    public static ProcResult? Run(string file, IReadOnlyList<string> arguments, TimeSpan? timeout = null)
    {
        var limit = timeout ?? DefaultTimeout;
        var info = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        // The tools' output is parsed, so it must not be translated.
        info.Environment["LC_ALL"] = "C";

        Process? process;
        try
        {
            process = Process.Start(info);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            ReportOnce(file, $"{file} could not be started: {e.Message}");
            return null;
        }

        if (process is null)
        {
            return null;
        }

        using (process)
        {
            // Both pipes are drained so a chatty tool never blocks on a full pipe; nothing is ever sent to it.
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(limit))
            {
                Kill(process);
                ReportOnce(file, $"{file} did not finish within {(int)limit.TotalSeconds} s and was stopped");
                return null;
            }

            // The tool has exited, so its pipes close at once unless it left a child holding them.
            _ = error.Wait(limit);
            return output.Wait(limit) ? new ProcResult(process.ExitCode, output.Result) : null;
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Exited between the timeout and the kill: nothing left to stop.
        }

        // Reaps it, so no zombie is left.
        process.WaitForExit();
    }

    private static void ReportOnce(string key, string message)
    {
        if (Reported.TryAdd(key, true))
        {
            Log.Write(message);
        }
    }
}
