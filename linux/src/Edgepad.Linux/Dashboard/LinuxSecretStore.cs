using System.Text;
using Edgepad.Platform;
using Edgepad.Trust;
using Tmds.DBus.Protocol;

namespace Edgepad.Dashboard;

/// <summary>The few Secret Service calls the store makes; a seam so tests can stand in for the session bus.</summary>
internal interface ISecretService
{
    /// <summary>A plain (unencrypted transport, the bus is the user's own) session's object path; null when the service is not there.</summary>
    string? OpenSession();

    /// <summary>True when the last <see cref="OpenSession"/> failed because no Secret Service is on the bus; false for a timeout or any other failure.</summary>
    bool IsAbsent { get; }

    /// <summary>The object path of the "default" collection; null when there is none or the call failed.</summary>
    string? DefaultCollection();

    /// <summary>The items whose attributes match, split as the service does into unlocked and locked; null when the call failed.</summary>
    (string[] Unlocked, string[] Locked)? Search(IReadOnlyDictionary<string, string> attributes);

    /// <summary>The secret's bytes; null when the call failed.</summary>
    byte[]? GetSecret(string item, string session);

    /// <summary>Creates or replaces the item. False when the call failed or the service wants a prompt (a locked collection).</summary>
    bool Create(string collection, string session, string label, IReadOnlyDictionary<string, string> attributes, byte[] secret);

    /// <summary>False when the call failed or the service wants a prompt.</summary>
    bool Delete(string item);
}

/// <summary>
/// <see cref="ISecretStore"/> on Linux: the desktop's Secret Service (GNOME Keyring, KWallet, KeePassXC) over the session bus,
/// items tagged <c>application=edgepad</c>, <c>name=&lt;name&gt;</c>. When there is no such service, or its collection is locked
/// and would need a prompt, the secret goes to <c>$XDG_CONFIG_HOME/edgepad/secrets/&lt;name&gt;</c> (folder 0700, file 0600), which is
/// private to the user but not encrypted at rest; <see cref="IsEncrypted"/> then turns false so the Integrations window can say so.
/// Nothing throws; all calls are serialised and block for up to the bus's deadline, so none belongs on the UI thread.
/// </summary>
internal sealed class LinuxSecretStore(ISecretService service, string folder) : ISecretStore, IDisposable
{
    private const string Application = "edgepad";
    private const string Header = "edgepad-secret 1";
    private const int MaxNameLength = 64;
    private const int MaxFileBytes = 64 * 1024;
    private const UnixFileMode PrivateFolder = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode LooserThanPrivate = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    private readonly Lock gate = new();

    // Under gate. Names whose keyring item could not be deleted yet. Kept in memory: a restart forgets it, and the token a
    // locked keyring still holds is then read again (and refused again if it was revoked).
    private readonly HashSet<string> pending = [];
    private string? session;
    private volatile bool encrypted = true;

    public LinuxSecretStore()
        : this(new DbusSecretService(Bus.OnSession()), Path.Combine(Xdg.ConfigFolder, "secrets"))
    {
    }

    /// <summary>False once a secret was saved to, or read from, the plain file instead of the Secret Service.</summary>
    public bool IsEncrypted => encrypted;

    public string? Get(string name)
    {
        if (!ValidName(name))
        {
            return null;
        }

        lock (gate)
        {
            // Deleted but still in a locked keyring: try again, and read nothing until it is gone.
            if (pending.Contains(name))
            {
                if (RemoveFromKeyring(name))
                {
                    pending.Remove(name);
                }

                return null;
            }

            // A plain file copy is the newer one whenever it exists: it is written only after the keyring refused a save, and
            // every save the keyring takes deletes it. So it wins over a keyring item, and moves into the keyring (then goes)
            // as soon as the keyring takes it.
            var fromFile = ReadFile(name);
            if (fromFile is not null)
            {
                if (Session() is { } open && service.DefaultCollection() is { } collection && collection != "/"
                    && service.Create(collection, open, $"Edgepad {name}", Attributes(name), Encoding.UTF8.GetBytes(fromFile)))
                {
                    encrypted = true;
                    DeleteFile(name);
                }
                else
                {
                    encrypted = false;
                }

                return fromFile;
            }

            if (Session() is { } s && service.Search(Attributes(name)) is { Unlocked: { Length: > 0 } unlocked })
            {
                if (service.GetSecret(unlocked[0], s) is { } bytes)
                {
                    return Decode(bytes);
                }

                session = null;
            }

            return null;
        }
    }

    public bool Set(string name, string value)
    {
        if (!ValidName(name))
        {
            return false;
        }

        lock (gate)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            if (Session() is { } s && service.DefaultCollection() is { } collection && collection != "/")
            {
                if (service.Create(collection, s, $"Edgepad {name}", Attributes(name), bytes))
                {
                    encrypted = true;
                    DeleteFile(name);
                    pending.Remove(name);
                    return true;
                }

                session = null;
            }

            encrypted = false;
            var wrote = WriteFile(name, value);
            if (wrote)
            {
                pending.Remove(name);
            }

            return wrote;
        }
    }

    public void Delete(string name)
    {
        if (!ValidName(name))
        {
            return;
        }

        lock (gate)
        {
            DeleteFile(name);

            // A locked item makes the service ask for a prompt and delete nothing. The name is then pending: Get never returns
            // it and tries again, so a token that was signed out or refused is not read back (and sent) from the keyring.
            if (RemoveFromKeyring(name))
            {
                pending.Remove(name);
            }
            else if (pending.Add(name))
            {
                Log.Write($"The secret {name} could not be removed from the keyring (locked or busy); it is hidden and removal is retried.");
            }
        }
    }

    public void Dispose() => (service as IDisposable)?.Dispose();

    // Under gate. True when no keyring item is left under the name (also when there is no keyring at all); false when one is, or
    // when that could not be found out (a timed-out call, a service still starting).
    private bool RemoveFromKeyring(string name)
    {
        if (Session() is null)
        {
            return service.IsAbsent;
        }

        if (service.Search(Attributes(name)) is not { } found)
        {
            return false;
        }

        var removed = true;
        foreach (var item in found.Unlocked.Concat(found.Locked))
        {
            removed &= service.Delete(item);
        }

        return removed;
    }

    /// <summary>Letters, digits, dot, dash, underscore, not starting with a dot: a name can never leave the secrets folder.</summary>
    internal static bool ValidName(string name) =>
        name.Length is >= 1 and <= MaxNameLength && name[0] != '.' && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');

    private static Dictionary<string, string> Attributes(string name) =>
        new(StringComparer.Ordinal) { ["application"] = Application, ["name"] = name };

    private static string? Decode(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private string? Session() => session ??= service.OpenSession();

    private string FilePath(string name) => Path.Combine(folder, name);

    private string? ReadFile(string name)
    {
        var path = FilePath(name);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            if ((!OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & LooserThanPrivate) != 0) || new FileInfo(path).Length > MaxFileBytes)
            {
                Log.Write($"The saved secret {name} is not private to this user, so it is ignored.");
                return null;
            }

            var lines = File.ReadAllText(path).Split('\n');
            // An empty second line decodes to "", which is no secret either.
            return lines.Length >= 2 && lines[0] == Header && lines[1].Trim().Length > 0 ? Decode(Convert.FromBase64String(lines[1].Trim())) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            return null;
        }
    }

    private bool WriteFile(string name, string value)
    {
        try
        {
            PrivateStorage.CreateDirectory(folder);

            // CreateDirectory leaves an existing folder's mode alone; this one must be the user's only.
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(folder, PrivateFolder);
            }

            var text = $"{Header}\n{Convert.ToBase64String(Encoding.UTF8.GetBytes(value))}\n";
            PrivateStorage.Replace(FilePath(name), temp => File.WriteAllText(temp, text, new UTF8Encoding(false)));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"The secret {name} could not be saved: {e.GetType().Name}");
            return false;
        }
    }

    private void DeleteFile(string name)
    {
        try
        {
            var path = FilePath(name);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"The saved secret {name} could not be removed: {e.GetType().Name}");
        }
    }
}

/// <summary>org.freedesktop.Secret.Service on the session bus. Every call goes through <see cref="Bus"/>: a 2 s deadline, never a throw.</summary>
internal sealed class DbusSecretService(Bus bus) : ISecretService, IDisposable
{
    private const string Destination = "org.freedesktop.secrets";
    private const string ServicePath = "/org/freedesktop/secrets";
    private const string ServiceInterface = "org.freedesktop.Secret.Service";
    private const string NoPrompt = "/";

    private volatile bool absent;

    public bool IsAbsent => absent;

    public string? OpenSession()
    {
        var ok = OpenSessionCall(out var path, out var refusal);
        absent = !ok && refusal is "org.freedesktop.DBus.Error.ServiceUnknown" or "org.freedesktop.DBus.Error.NameHasNoOwner";
        return ok ? path : null;
    }

    private bool OpenSessionCall(out string? path, out string? refusal) =>
        bus.Call(
            c =>
            {
                using var writer = c.GetMessageWriter();
                writer.WriteMethodCallHeader(destination: Destination, path: ServicePath, @interface: ServiceInterface, member: "OpenSession", signature: "sv");
                writer.WriteString("plain");
                writer.WriteVariantString(string.Empty);
                return writer.CreateMessage();
            },
            (Message reply, object? _) =>
            {
                var r = reply.GetBodyReader();
                r.ReadVariantValue();
                return r.ReadObjectPathAsString();
            },
            out path,
            out refusal);

    public string? DefaultCollection() =>
        bus.Call(
            c =>
            {
                using var writer = c.GetMessageWriter();
                writer.WriteMethodCallHeader(destination: Destination, path: ServicePath, @interface: ServiceInterface, member: "ReadAlias", signature: "s");
                writer.WriteString("default");
                return writer.CreateMessage();
            },
            (Message reply, object? _) => reply.GetBodyReader().ReadObjectPathAsString(),
            out var path)
            ? path
            : null;

    public (string[] Unlocked, string[] Locked)? Search(IReadOnlyDictionary<string, string> attributes) =>
        bus.Call(
            c =>
            {
                using var writer = c.GetMessageWriter();
                writer.WriteMethodCallHeader(destination: Destination, path: ServicePath, @interface: ServiceInterface, member: "SearchItems", signature: "a{ss}");

                // MessageWriter is a ref struct and a using variable cannot be passed by ref, so each dictionary is written in place.
                var dict = writer.WriteDictionaryStart();
                foreach (var (key, value) in attributes)
                {
                    writer.WriteDictionaryEntryStart();
                    writer.WriteString(key);
                    writer.WriteString(value);
                }

                writer.WriteDictionaryEnd(dict);
                return writer.CreateMessage();
            },
            (Message reply, object? _) =>
            {
                var r = reply.GetBodyReader();
                var unlocked = r.ReadArrayOfObjectPath().Select(p => p.ToString()).ToArray();
                var locked = r.ReadArrayOfObjectPath().Select(p => p.ToString()).ToArray();
                return (unlocked, locked);
            },
            out var found)
            ? found
            : null;

    public byte[]? GetSecret(string item, string session) =>
        bus.Call(
            c =>
            {
                using var writer = c.GetMessageWriter();
                writer.WriteMethodCallHeader(destination: Destination, path: item, @interface: "org.freedesktop.Secret.Item", member: "GetSecret", signature: "o");
                writer.WriteObjectPath(session);
                return writer.CreateMessage();
            },
            (Message reply, object? _) =>
            {
                // (oayays): session, parameters (empty for plain), value, content type.
                var r = reply.GetBodyReader();
                r.AlignStruct();
                r.ReadObjectPathAsString();
                ReadBytes(ref r);
                var value = ReadBytes(ref r);
                r.ReadString();
                return value;
            },
            out var secret)
            ? secret
            : null;

    public bool Create(string collection, string session, string label, IReadOnlyDictionary<string, string> attributes, byte[] secret) =>
        bus.Call(
            c =>
            {
                using var writer = c.GetMessageWriter();
                writer.WriteMethodCallHeader(
                    destination: Destination,
                    path: collection,
                    @interface: "org.freedesktop.Secret.Collection",
                    member: "CreateItem",
                    signature: "a{sv}(oayays)b");
                var properties = writer.WriteDictionaryStart();
                writer.WriteDictionaryEntryStart();
                writer.WriteString("org.freedesktop.Secret.Item.Label");
                writer.WriteVariantString(label);
                writer.WriteDictionaryEntryStart();
                writer.WriteString("org.freedesktop.Secret.Item.Attributes");
                writer.WriteSignature("a{ss}");
                var dict = writer.WriteDictionaryStart();
                foreach (var (key, value) in attributes)
                {
                    writer.WriteDictionaryEntryStart();
                    writer.WriteString(key);
                    writer.WriteString(value);
                }

                writer.WriteDictionaryEnd(dict);
                writer.WriteDictionaryEnd(properties);
                writer.WriteStructureStart();
                writer.WriteObjectPath(session);
                var parameters = writer.WriteArrayStart(DBusType.Byte);
                writer.WriteArrayEnd(parameters);
                var bytes = writer.WriteArrayStart(DBusType.Byte);
                foreach (var b in secret)
                {
                    writer.WriteByte(b);
                }

                writer.WriteArrayEnd(bytes);
                writer.WriteString("text/plain; charset=utf8");
                writer.WriteBool(true);
                return writer.CreateMessage();
            },
            (Message reply, object? _) =>
            {
                var r = reply.GetBodyReader();
                r.ReadObjectPathAsString();
                return r.ReadObjectPathAsString();
            },
            out var prompt)
        && prompt == NoPrompt;

    public bool Delete(string item) =>
        bus.Call(
            c =>
            {
                using var writer = c.GetMessageWriter();
                writer.WriteMethodCallHeader(destination: Destination, path: item, @interface: "org.freedesktop.Secret.Item", member: "Delete");
                return writer.CreateMessage();
            },
            (Message reply, object? _) => reply.GetBodyReader().ReadObjectPathAsString(),
            out var prompt)
        && prompt == NoPrompt;

    public void Dispose() => bus.Dispose();

    private static byte[] ReadBytes(ref Reader r)
    {
        var bytes = new List<byte>();
        var end = r.ReadArrayStart(DBusType.Byte);
        while (r.HasNext(end))
        {
            bytes.Add(r.ReadByte());
        }

        return [.. bytes];
    }
}
