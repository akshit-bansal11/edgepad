namespace Edgepad.Trust;

/// <summary>
/// Trust on first use. Pairing already limits connections to bonded devices; this narrows it to the one
/// phone that connected first, so some other paired device cannot drive the laptop. The tray menu's
/// Forget clears it, and the next phone to connect becomes the trusted one.
/// </summary>
internal sealed class TrustStore(string path)
{
    private readonly Lock gate = new();

    public static TrustStore ForCurrentUser() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Edgepad", "trusted-phone.txt"));

    public string? Trusted
    {
        get
        {
            lock (gate)
            {
                return Read();
            }
        }
    }

    /// <summary>True when <paramref name="address"/> may connect. The first address ever offered is remembered.</summary>
    public bool Admit(string address)
    {
        lock (gate)
        {
            var trusted = Read();
            if (trusted is null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
                File.WriteAllText(path, address);
                return true;
            }

            return string.Equals(trusted, address, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void Forget()
    {
        lock (gate)
        {
            File.Delete(path);
        }
    }

    private string? Read() => File.Exists(path) && File.ReadAllText(path).Trim() is { Length: > 0 } address ? address : null;
}
