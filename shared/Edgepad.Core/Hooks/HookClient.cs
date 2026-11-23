using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Edgepad.Hooks;

/// <summary>Seams for tests. The apps pass none.</summary>
internal sealed class HookClientOptions
{
    /// <summary>hook.json. <see cref="HookEndpoint.DefaultFile"/> when null.</summary>
    public string? File { get; init; }

    /// <summary>Connects to an endpoint within the timeout or throws. A current-user-only pipe (or Unix socket) by default.</summary>
    public Func<string, TimeSpan, Stream>? Connect { get; init; }

    /// <summary>How long a permission request waits for the laptop's decision.</summary>
    public TimeSpan ReplyWait { get; init; } = HookClient.ReplyWait;
}

/// <summary>
/// The plugins' hook command, run as the app's own executable: <c>"&lt;app&gt;" --hook &lt;claude|codex&gt; &lt;perm|session|usage|spend&gt;</c>,
/// the hook JSON on stdin for perm and session, the JSON as the next argument for usage and spend. It hands the event to the running
/// app over the hook channel named in hook.json (one request line, one reply line) and, for a permission request only, prints the
/// app's allow or deny in the PermissionRequest output shape. Rules kept whatever happens: the exit code is 0 (2 would block a
/// tool); nothing is printed except that decision (no output means "defer": the agent asks as usual; for SessionStart and
/// UserPromptSubmit stdout would reach the model); the token and the request are never printed or logged; the agent comes from
/// argv, never the payload; only whitelisted fields leave. protocol/hook-vectors.txt CLIENT lines hold the exact requests.
/// A platform calls <see cref="RunConsole"/> first thing in Main when <see cref="IsHook"/>, before any mutex, tray or window.
/// </summary>
internal static class HookClient
{
    public const string Flag = "--hook";
    public const int StdinCap = 1 << 20;
    public const int ArgCap = 8192;
    public const int HookFileCap = 4096;
    public const int ReplyCap = 4096;
    public const int InputCap = 5000; // characters, above the laptop's 4096-byte cap so its rule decides exactly
    public const int MaxParts = 1024;
    public const string DenyMessage = "Denied from Edgepad on the phone.";
    public static readonly TimeSpan ConnectWait = TimeSpan.FromSeconds(2);

    // The perm hook's own timeout is 120 s; the laptop answers within 30 s.
    public static readonly TimeSpan ReplyWait = TimeSpan.FromSeconds(35);

    private const string PipeRoot = @"\\.\pipe\";

    // The lifecycle events the laptop's SessionBoard maps, and the Notification types that mean "the agent waits for you".
    // Gemini CLI adds BeforeAgent and AfterAgent and its own Notification type; OpenCode's plugin sends the five dotted names.
    private static readonly string[] Events =
    [
        "SessionStart", "UserPromptSubmit", "Notification", "Stop", "SessionEnd",
        "BeforeAgent", "AfterAgent",
        "session.created", "session.busy", "session.idle", "permission.asked", "session.deleted",
    ];

    private static readonly string[] Notifications = ["permission_prompt", "idle_prompt", "agent_needs_input", "ToolPermission"];

    // Bash `command`; Edit/Write/MultiEdit/Read/NotebookEdit `file_path`; WebFetch `url`. The laptop picks per tool.
    private static readonly string[] InputFields = ["command", "file_path", "url"];

    /// <summary>True when the app was started as a hook command.</summary>
    public static bool IsHook(string[] args) => args.Length > 0 && args[0] == Flag;

    /// <summary><see cref="Run"/> on the process's own stdin and stdout, read and written as UTF-8 whatever the console code page.</summary>
    public static int RunConsole(string[] args)
    {
        try
        {
            using var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            using var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
            return Run(args, stdin, stdout);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>Handles <c>--hook &lt;agent&gt; &lt;kind&gt; [json]</c>. Never throws; always 0.</summary>
    public static int Run(string[] args, TextReader stdin, TextWriter stdout, HookClientOptions? opts = null)
    {
        try
        {
            opts ??= new HookClientOptions();
            if (args.Length < 3 || args[0] != Flag || args[1] is not ("claude" or "codex" or "gemini" or "opencode") || args[2] is not (HookProtocol.Perm or HookProtocol.Session or HookProtocol.Usage or HookProtocol.Spend))
            {
                return 0;
            }

            // Gemini CLI and OpenCode report their sessions only: approvals and limits stay with Claude Code and Codex.
            if (args[1] is "gemini" or "opencode" && args[2] != HookProtocol.Session)
            {
                return 0;
            }

            var (agent, kind) = (args[1], args[2]);
            byte[]? data;
            if (kind is HookProtocol.Perm or HookProtocol.Session)
            {
                if (ReadCapped(stdin) is not { } text || Parse(text) is not { } hook)
                {
                    return 0;
                }

                using (hook)
                {
                    data = BuildData(agent, kind, hook.RootElement);
                }
            }
            else
            {
                if (args.Length < 4 || Encoding.UTF8.GetByteCount(args[3]) > ArgCap || Parse(args[3]) is not { } doc)
                {
                    return 0;
                }

                using (doc)
                {
                    data = doc.RootElement.ValueKind == JsonValueKind.Object ? Write(w => doc.RootElement.WriteTo(w)) : null;
                }
            }

            if (data is null || (opts.File ?? HookEndpoint.DefaultFile()) is not { } file || ReadHookFile(file) is not { } info)
            {
                return 0;
            }

            var line = Write(w =>
            {
                w.WriteStartObject();
                w.WriteNumber("v", 1);
                w.WriteString("token", info.Token);
                w.WriteString("agent", agent);
                w.WriteString("kind", kind);
                w.WritePropertyName("d");
                w.WriteRawValue(data, skipInputValidation: true);
                w.WriteEndObject();
            });
            var decision = Send(opts, info.Endpoint, [.. line, (byte)'\n'], kind == HookProtocol.Perm);
            if (decision is not (HookProtocol.Allow or HookProtocol.Deny))
            {
                return 0;
            }

            var output = Write(w =>
            {
                w.WriteStartObject();
                w.WriteStartObject("hookSpecificOutput");
                w.WriteString("hookEventName", "PermissionRequest");
                w.WriteStartObject("decision");
                w.WriteString("behavior", decision);
                if (decision == HookProtocol.Deny)
                {
                    w.WriteString("message", DenyMessage);
                }

                w.WriteEndObject();
                w.WriteEndObject();
                w.WriteEndObject();
            });
            stdout.Write(Encoding.UTF8.GetString(output) + "\n");
            stdout.Flush();
        }
        catch (Exception)
        {
            // Whatever goes wrong, print nothing: the agent then asks the person as it always does.
        }

        return 0;
    }

    // The request's `d` for a hook payload, or null when this event is not sent.
    private static byte[]? BuildData(string agent, string kind, JsonElement hook)
    {
        if (hook.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var session = Cut(Str(hook, "session_id"), 128) ?? string.Empty;
        var cwd = Cut(Str(hook, "cwd"), 1024) ?? string.Empty;
        if (kind == HookProtocol.Perm)
        {
            if (Cut(Str(hook, "tool_name"), 64) is not { Length: > 0 } tool)
            {
                return null;
            }

            return Write(w =>
            {
                w.WriteStartObject();
                w.WriteString("session_id", session);
                w.WriteString("cwd", cwd);
                w.WriteString("tool_name", tool);
                var truncated = false;
                w.WriteStartObject("tool_input");
                if (hook.TryGetProperty("tool_input", out var raw) && raw.ValueKind == JsonValueKind.Object)
                {
                    foreach (var key in InputFields)
                    {
                        if (raw.TryGetProperty(key, out var value))
                        {
                            truncated |= WriteField(w, agent, key, value);
                        }
                    }
                }

                w.WriteEndObject();

                // Only when a field was cut, so the laptop offers Deny only: what it would show is not all that runs.
                if (truncated)
                {
                    w.WriteBoolean("truncated", true);
                }

                w.WriteEndObject();
            });
        }

        if (Str(hook, "hook_event_name") is not { } name || !Events.Contains(name))
        {
            return null;
        }

        var notification = Str(hook, "notification_type");
        if (name == "Notification" && (notification is null || !Notifications.Contains(notification)))
        {
            return null;
        }

        return Write(w =>
        {
            w.WriteStartObject();
            w.WriteString("hook_event_name", name);
            w.WriteString("session_id", session);
            w.WriteString("cwd", cwd);
            if (name == "Notification")
            {
                w.WriteString("notification_type", notification);
            }

            w.WriteEndObject();
        });
    }

    // A string cut to InputCap; for Codex's `command` also its array form, a list of strings sent as is unless joined for display
    // (HookProtocol.Quote) it passes InputCap, then as the cut joined string. Anything else is left out. True when it was cut.
    private static bool WriteField(Utf8JsonWriter w, string agent, string key, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString()!;
            w.WriteString(key, Cut(text, InputCap));
            return text.Length > InputCap;
        }

        if (agent != "codex" || key != "command" || value.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var count = value.GetArrayLength();
        if (count is 0 or > MaxParts || value.EnumerateArray().Any(p => p.ValueKind != JsonValueKind.String))
        {
            return false;
        }

        var parts = value.EnumerateArray().Select(p => p.GetString()!).ToList();
        var joined = string.Join(' ', parts.Select(HookProtocol.Quote));
        if (joined.Length > InputCap)
        {
            w.WriteString(key, Cut(joined, InputCap));
            return true;
        }

        w.WriteStartArray(key);
        foreach (var part in parts)
        {
            w.WriteStringValue(part);
        }

        w.WriteEndArray();
        return false;
    }

    // { endpoint, token } from hook.json, or null. On Unix the file must have no group or other permission bits; since it could be
    // opened for reading, it is then this user's own (or this user is root): a 0600 file of another user cannot be opened.
    private static (string Endpoint, string Token)? ReadHookFile(string path)
    {
        try
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (!OperatingSystem.IsWindows())
            {
                const UnixFileMode others = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
                if ((File.GetUnixFileMode(handle) & others) != 0)
                {
                    return null;
                }
            }

            if (RandomAccess.GetLength(handle) > HookFileCap)
            {
                return null;
            }

            var buffer = new byte[HookFileCap];
            var n = RandomAccess.Read(handle, buffer, 0);
            using var doc = JsonDocument.Parse(buffer.AsMemory(0, n));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("v", out var v) || v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var version) || version != 1
                || Str(root, "token") is not { Length: >= 16 and <= 128 } token || !token.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')
                || Str(root, "endpoint") is not { } endpoint)
            {
                return null;
            }

            var ok = OperatingSystem.IsWindows() ? IsPipe(endpoint) : endpoint.StartsWith('/');
            return ok ? (endpoint, token) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsPipe(string endpoint)
    {
        var prefix = PipeRoot + HookEndpoint.PipePrefix;
        return endpoint.Length == prefix.Length + 16
            && endpoint.StartsWith(prefix, StringComparison.Ordinal)
            && endpoint[prefix.Length..].All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');
    }

    // Sends one line; for perm returns the decision ("allow", "deny", "defer") or null. Other kinds do not wait for the reply.
    private static string? Send(HookClientOptions opts, string endpoint, byte[] line, bool waitReply)
    {
        Stream stream;
        try
        {
            stream = (opts.Connect ?? ConnectPipe)(endpoint, ConnectWait);
        }
        catch (Exception)
        {
            return null;
        }

        using (stream)
        {
            try
            {
                var wait = waitReply ? opts.ReplyWait : ConnectWait;
                var deadline = Environment.TickCount64 + (long)wait.TotalMilliseconds;
                Task.Run(() =>
                {
                    stream.Write(line);
                    stream.Flush();
                }).Wait(wait);
                if (!waitReply)
                {
                    return null;
                }

                var reply = new byte[ReplyCap + 1];
                var have = 0;
                while (have < reply.Length)
                {
                    var left = deadline - Environment.TickCount64;
                    if (left <= 0)
                    {
                        return null;
                    }

                    var read = Task.Run(() => stream.Read(reply, have, reply.Length - have));
                    if (!read.Wait(TimeSpan.FromMilliseconds(left)) || read.Result == 0)
                    {
                        return null;
                    }

                    var start = have;
                    have += read.Result;
                    var nl = Array.IndexOf(reply, (byte)'\n', start, have - start);
                    if (nl >= 0)
                    {
                        return Decision(reply.AsMemory(0, nl));
                    }
                }

                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    private static string? Decision(ReadOnlyMemory<byte> line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("v", out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var version) && version == 1
                && Str(root, "decision") is { } d && d is HookProtocol.Allow or HookProtocol.Deny or HookProtocol.Defer
                    ? d
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // A current-user-only client: on Windows the pipe's owner must be this user; on Unix a rooted name is the socket path and the
    // peer's uid must be this process's.
    private static NamedPipeClientStream ConnectPipe(string endpoint, TimeSpan timeout)
    {
        var name = OperatingSystem.IsWindows() ? endpoint[PipeRoot.Length..] : endpoint;
        var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
        try
        {
            pipe.Connect(timeout);
            return pipe;
        }
        catch
        {
            pipe.Dispose();
            throw;
        }
    }

    // Stdin up to the cap; null when it is longer or unreadable.
    private static string? ReadCapped(TextReader reader)
    {
        try
        {
            var text = new StringBuilder();
            var buffer = new char[8192];
            int n;
            while ((n = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                text.Append(buffer, 0, n);
                if (text.Length > StdinCap)
                {
                    return null;
                }
            }

            return text.ToString();
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static JsonDocument? Parse(string text)
    {
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Str(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // Cut to n UTF-16 units without leaving half a surrogate pair.
    private static string? Cut(string? value, int n) =>
        value is null || value.Length <= n ? value : value[..(char.IsHighSurrogate(value[n - 1]) ? n - 1 : n)];

    private static byte[] Write(Action<Utf8JsonWriter> body)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            body(w);
        }

        return buffer.ToArray();
    }
}
