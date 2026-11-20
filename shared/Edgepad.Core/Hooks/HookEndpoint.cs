using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Edgepad.Trust;

namespace Edgepad.Hooks;

/// <summary>
/// Where the hook channel listens for this user: a named pipe on Windows, a Unix socket elsewhere. Each platform's app supplies
/// one (WindowsHookListener; <see cref="UnixSocketListener"/> on Linux) and hands it to <see cref="HookServer"/>.
/// </summary>
internal interface IHookListener : IDisposable
{
    /// <summary>What hook.json names: the full pipe path or the socket path.</summary>
    string Endpoint { get; }

    /// <summary>The next connection, already known to come from this user. Null once the listener is disposed.</summary>
    Task<Stream?> AcceptAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The discovery file the plugin's hook client reads: <c>{"v":1,"endpoint","token","pid"}</c>, user-only (0600 on Unix). The token
/// is 32 random bytes in base64url, made new by every <see cref="HookEndpoint"/> and by <see cref="Rotate"/> (an integration was
/// switched on). Dispose deletes the file, so a client finds nothing to talk to once the app is gone.
/// </summary>
internal sealed class HookEndpoint : IDisposable
{
    public const string FileName = "hook.json";
    public const string PipePrefix = "edgepad-hook-";

    private readonly Lock gate = new();
    private readonly string endpoint;
    private string token;
    private bool disposed;

    /// <summary>Writes <paramref name="file"/> naming <paramref name="endpoint"/>. Throws what the disk threw when it cannot.</summary>
    public HookEndpoint(string file, string endpoint)
    {
        File = file;
        this.endpoint = endpoint;
        token = NewToken();
        Write();
    }

    public string File { get; }

    /// <summary>The current token. The server reads it on every request, so a rotation takes effect at once.</summary>
    public string Token
    {
        get
        {
            lock (gate)
            {
                return token;
            }
        }
    }

    /// <summary>
    /// hook.json's place for this user: <c>%LOCALAPPDATA%\Edgepad</c> on Windows, <c>~/Library/Application Support/Edgepad</c> on a Mac,
    /// <c>$XDG_RUNTIME_DIR/edgepad</c> on Linux. Null on Linux without XDG_RUNTIME_DIR: there is no shared-folder fallback.
    /// </summary>
    public static string? DefaultFile()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Edgepad", FileName);
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Edgepad", FileName);
        }

        return SocketDirectory() is { } dir ? Path.Combine(dir, FileName) : null;
    }

    /// <summary>Linux: <c>$XDG_RUNTIME_DIR/edgepad</c>, the folder of hook.json and hook.sock; null when the variable is unset or not absolute.</summary>
    public static string? SocketDirectory() =>
        Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } runtime && Path.IsPathFullyQualified(runtime)
            ? Path.Combine(runtime, "edgepad")
            : null;

    /// <summary>The pipe's name for a user: <c>edgepad-hook-</c> and the first 16 hex digits of SHA-256 of the SID.</summary>
    public static string PipeName(string userSid) =>
        PipePrefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(userSid)))[..16];

    /// <summary>The pipe's full path, as hook.json names it: <c>\\.\pipe\edgepad-hook-…</c>.</summary>
    public static string PipePath(string userSid) => @"\\.\pipe\" + PipeName(userSid);

    /// <summary>A new token, written to the file before it is used. False (logged) when the file could not be written; the old one then stays.</summary>
    public bool Rotate()
    {
        lock (gate)
        {
            if (disposed)
            {
                return false;
            }

            var before = token;
            token = NewToken();
            try
            {
                Write();
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                token = before;
                Log.Write($"The hook file could not be rewritten: {e.GetType().Name}");
                return false;
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            try
            {
                System.IO.File.Delete(File);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Write($"The hook file could not be removed: {e.GetType().Name}");
            }
        }
    }

    private static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private void Write()
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteNumber("v", 1);
            json.WriteString("endpoint", endpoint);
            json.WriteString("token", token);
            json.WriteNumber("pid", Environment.ProcessId);
            json.WriteEndObject();
        }

        var bytes = buffer.ToArray();
        PrivateStorage.Replace(File, temp => System.IO.File.WriteAllBytes(temp, bytes));
    }
}
