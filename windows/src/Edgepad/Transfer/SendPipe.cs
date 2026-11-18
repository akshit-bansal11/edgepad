using System.Globalization;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Edgepad.Startup;

namespace Edgepad.Transfer;

/// <summary>
/// Explorer's Send to › Edgepad starts <c>Edgepad.exe --send &lt;path&gt;...</c>, which hands the paths to the running copy through
/// this pipe and exits. The pipe is closed to every other user (<see cref="PipeOptions.CurrentUserOnly"/>) and a line read
/// from it is trusted no further than <see cref="Validate"/>: an existing regular file under the transfer cap.
/// </summary>
internal sealed class SendPipe : IDisposable
{
    internal const string PipeName = "Edgepad.Send";
    private const string ShortcutName = "Edgepad.lnk";
    private const int MaxLine = 32 * 1024;
    private const int MaxTotal = 256 * 1024;
    internal const int MaxFiles = 100;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(2);

    private readonly Action<IReadOnlyList<string>, int> onPaths;
    private readonly CancellationTokenSource stop = new();

    public SendPipe(Action<IReadOnlyList<string>, int> onPaths)
    {
        this.onPaths = onPaths;
        _ = Task.Run(() => ServeAsync(stop.Token), CancellationToken.None);
    }

    /// <summary>
    /// <see cref="Validate"/> for every line: blank lines are ignored, a repeat counts once, at most <see cref="MaxFiles"/> are
    /// kept and the rest are dropped. Skipped is the number of non-blank lines that were not usable files.
    /// </summary>
    internal static (IReadOnlyList<string> Valid, int Skipped) ValidateAll(IEnumerable<string?> lines)
    {
        var valid = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skipped = 0;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || valid.Count >= MaxFiles)
            {
                continue;
            }

            if (Validate(line) is not { } path)
            {
                skipped++;
            }
            else if (seen.Add(path))
            {
                valid.Add(path);
            }
        }

        return (valid, skipped);
    }

    /// <summary>The full path when <paramref name="line"/> names an existing file (not a folder, not empty, not over 2 GiB); else null.</summary>
    internal static string? Validate(string? line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.Length > MaxLine || line.Contains('\0'))
        {
            return null;
        }

        try
        {
            var full = Path.GetFullPath(line.Trim());
            if (!File.Exists(full))
            {
                return null;
            }

            var length = new FileInfo(full).Length;
            return length > 0 && (ulong)length <= TransferLimits.MaxBytes ? full : null;
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Hands <paramref name="paths"/> to the running copy over one connection, a path per line. False when none answered.</summary>
    public static bool Send(IEnumerable<string> paths)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect((int)Patience.TotalMilliseconds);
            using var writer = new StreamWriter(client, new UTF8Encoding(false));
            foreach (var path in paths)
            {
                writer.WriteLine(Path.GetFullPath(path));
            }

            writer.Flush();
            return true;
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.Write($"Send to: no running copy answered: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Makes the per-user Send to shortcut, or repoints it when an update moved the exe. Late-bound WScript.Shell, so no
    /// reference and no admin rights; the SendTo folder is the user's own.
    /// </summary>
    public static void EnsureShortcut()
    {
        object? shell = null;
        object? link = null;
        try
        {
            if (Environment.ProcessPath is not { } exe
                || Type.GetTypeFromProgID("WScript.Shell") is not { } type)
            {
                return;
            }

            var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SendTo), ShortcutName);
            shell = Activator.CreateInstance(type);
            link = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, [file], CultureInfo.InvariantCulture);
            if (link is null)
            {
                return;
            }

            var linkType = link.GetType();
            var target = linkType.InvokeMember(
                "TargetPath", BindingFlags.GetProperty, null, link, null, CultureInfo.InvariantCulture) as string;
            if (File.Exists(file) && string.Equals(target, exe, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _ = linkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, [exe], CultureInfo.InvariantCulture);
            _ = linkType.InvokeMember("Arguments", BindingFlags.SetProperty, null, link, [SingleInstance.SendOption], CultureInfo.InvariantCulture);
            _ = linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null, CultureInfo.InvariantCulture);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or TargetInvocationException or COMException)
        {
            Log.Write($"Send to shortcut not written: {e.Message}");
        }
        finally
        {
            foreach (var com in new[] { link, shell })
            {
                if (com is not null && Marshal.IsComObject(com))
                {
                    _ = Marshal.ReleaseComObject(com);
                }
            }
        }
    }

    private async Task ServeAsync(CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancel);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                timeout.CancelAfter(Patience);
                using var reader = new StreamReader(server, new UTF8Encoding(false), false, 1024, leaveOpen: true);
                var lines = new List<string?>();
                var total = 0;
                while (lines.Count < MaxFiles * 2 && total < MaxTotal && await ReadLineAsync(reader, timeout.Token) is { } line)
                {
                    lines.Add(line);
                    total += line.Length + 1;
                }

                var (valid, skipped) = ValidateAll(lines);
                if (valid.Count > 0)
                {
                    onPaths(valid, skipped);
                }
            }
            catch (OperationCanceledException)
            {
                // Stopping, or a client that connected and said nothing: either way the next one is waited for.
            }
            catch (IOException e)
            {
                Log.Write($"Send to pipe: {e.Message}");
            }
        }
    }

    private static async Task<string?> ReadLineAsync(StreamReader reader, CancellationToken cancel)
    {
        var text = new StringBuilder();
        var one = new char[1];
        var ended = false;
        while (text.Length <= MaxLine)
        {
            if (await reader.ReadAsync(one.AsMemory(), cancel) != 1)
            {
                ended = true;
                break;
            }

            if (one[0] == '\n')
            {
                break;
            }

            _ = text.Append(one[0]);
        }

        return ended && text.Length == 0 ? null : text.ToString().TrimEnd('\r');
    }

    public void Dispose()
    {
        stop.Cancel();
        stop.Dispose();
    }
}
