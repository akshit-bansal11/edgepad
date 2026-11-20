using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Edgepad.Dashboard;

/// <summary>
/// How a vendor CLI run ended. <paramref name="LastLine"/> is the last non-empty line of the first 64 KiB of stdout (stderr is
/// discarded). A run that could not start has exit code -1; one that ran past its time was killed and is <paramref name="TimedOut"/>.
/// </summary>
internal sealed record ProcessResult(int ExitCode, string LastLine, bool TimedOut = false);

/// <summary>Runs one program with fixed arguments and waits for it. The plugin installer's only way to start a process.</summary>
internal interface IProcessRunner
{
    /// <summary>Runs <paramref name="file"/> (an absolute path) with <paramref name="arguments"/> as separate arguments, never through a shell string.</summary>
    Task<ProcessResult> RunAsync(string file, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>
/// The real runner: <see cref="ProcessStartInfo.ArgumentList"/> (no quoting by hand), no window, stdin closed so nothing can wait
/// for an answer, <c>NO_COLOR=1</c>, stdout read up to 64 KiB, stderr drained and dropped, and the whole process tree killed at the
/// timeout. Never throws for a program that is missing or will not start: that is exit code -1.
/// </summary>
internal sealed class ProcessRunner : IProcessRunner
{
    public const int MaxOutputChars = 64 * 1024;

    public async Task<ProcessResult> RunAsync(string file, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            WorkingDirectory = workingDirectory,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["NO_COLOR"] = "1";
        using var process = new Process { StartInfo = info };
        try
        {
            if (!process.Start())
            {
                return new ProcessResult(-1, string.Empty);
            }
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            Log.Write($"{Path.GetFileName(file)} could not be started: {e.GetType().Name}");
            return new ProcessResult(-1, string.Empty);
        }

        process.StandardInput.Close();
        process.ErrorDataReceived += static (_, _) => { };
        process.BeginErrorReadLine();
        var output = ReadCapped(process.StandardOutput);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception e) when (e is Win32Exception or InvalidOperationException or NotSupportedException)
            {
                // Ended on its own just now.
            }

            Log.Write($"{Path.GetFileName(file)} took longer than {timeout.TotalSeconds:0} s and was stopped");
            await output.ConfigureAwait(false);
            return new ProcessResult(-1, string.Empty, TimedOut: true);
        }

        return new ProcessResult(process.ExitCode, LastLine(await output.ConfigureAwait(false)));
    }

    /// <summary>The last line with something on it, trimmed; empty when there is none.</summary>
    public static string LastLine(string text)
    {
        var lines = text.Split('\n');
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length > 0)
            {
                return trimmed;
            }
        }

        return string.Empty;
    }

    // Keeps the first 64 KiB and reads the rest away, so a chatty program never blocks on a full pipe.
    private static async Task<string> ReadCapped(StreamReader reader)
    {
        var kept = new StringBuilder();
        var buffer = new char[4096];
        try
        {
            int n;
            while ((n = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
            {
                kept.Append(buffer, 0, Math.Min(n, Math.Max(0, MaxOutputChars - kept.Length)));
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            // The process was killed under the read.
        }

        return kept.ToString();
    }
}
