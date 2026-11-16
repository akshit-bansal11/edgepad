using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Edgepad.Startup;

namespace Edgepad.Transfer;

/// <summary>
/// Explorer's Send to › Edgepad starts <c>Edgepad.exe --send &lt;path&gt;</c>, which hands the path to the running copy through
/// this pipe and exits. The pipe is closed to every other user (<see cref="PipeOptions.CurrentUserOnly"/>) and a line read
/// from it is trusted no further than <see cref="Validate"/>: an existing regular file under the transfer cap.
/// </summary>
internal sealed class SendPipe : IDisposable
{
    internal const string PipeName = "Edgepad.Send";
    private const string ShortcutName = "Edgepad.lnk";
    private const int MaxLine = 32 * 1024;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(2);

    private readonly Action<string> onPath;
    private readonly CancellationTokenSource stop = new();

    public SendPipe(Action<string> onPath)
    {
        this.onPath = onPath;
        _ = Task.Run(() => ServeAsync(stop.Token), CancellationToken.None);
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

    /// <summary>Hands <paramref name="path"/> to the running copy. False when none answered.</summary>
    public static bool Send(string path)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect((int)Patience.TotalMilliseconds);
            using var writer = new StreamWriter(client, new UTF8Encoding(false));
            writer.WriteLine(Path.GetFullPath(path));
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
            link = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, [file]);
            if (link is null)
            {
                return;
            }

            var linkType = link.GetType();
            if (File.Exists(file)
                && string.Equals(linkType.InvokeMember("TargetPath", BindingFlags.GetProperty, null, link, null) as string, exe, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _ = linkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, [exe]);
            _ = linkType.InvokeMember("Arguments", BindingFlags.SetProperty, null, link, [SingleInstance.SendOption]);
            _ = linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
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
                if (Validate(await ReadLineAsync(reader, timeout.Token)) is { } path)
                {
                    onPath(path);
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
        while (text.Length <= MaxLine && await reader.ReadAsync(one.AsMemory(), cancel) == 1 && one[0] != '\n')
        {
            _ = text.Append(one[0]);
        }

        return text.ToString().TrimEnd('\r');
    }

    public void Dispose()
    {
        stop.Cancel();
        stop.Dispose();
    }
}
