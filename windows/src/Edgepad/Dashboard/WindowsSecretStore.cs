using System.Runtime.InteropServices;
using System.Text;
using Edgepad.Trust;

namespace Edgepad.Dashboard;

/// <summary>
/// Secrets kept with DPAPI in this Windows user's scope: each one encrypted by <c>CryptProtectData</c> (no UI, a fixed entropy
/// so another app calling DPAPI for this user cannot decrypt it by accident) and written whole to
/// <c>%LOCALAPPDATA%\Edgepad\secrets\&lt;name&gt;.bin</c>. Only this user on this machine can read a file back. Nothing here
/// throws into the caller: a missing, foreign or damaged file reads as null, a failed save as false.
/// </summary>
internal sealed partial class WindowsSecretStore : ISecretStore
{
    private const uint UiForbidden = 0x1; // CRYPTPROTECT_UI_FORBIDDEN
    private const int MaxName = 64;
    private const int MaxFile = 64 * 1024;

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Edgepad secret store v1");

    private readonly string folder;

    /// <param name="folder">Where the files go; tests pass their own. Default <c>%LOCALAPPDATA%\Edgepad\secrets</c>.</param>
    public WindowsSecretStore(string? folder = null) =>
        this.folder = folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Edgepad", "secrets");

    public string? Get(string name)
    {
        if (FileFor(name) is not { } path)
        {
            return null;
        }

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is 0 or > MaxFile)
            {
                return null;
            }

            return Unprotect(File.ReadAllBytes(path)) is { } plain ? Decode(plain) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public bool Set(string name, string value)
    {
        if (FileFor(name) is not { } path)
        {
            return false;
        }

        var plain = Encoding.UTF8.GetBytes(value);
        var sealedBytes = Protect(plain);
        Array.Clear(plain);
        if (sealedBytes is null)
        {
            return false;
        }

        try
        {
            PrivateStorage.Replace(path, temp => File.WriteAllBytes(temp, sealedBytes));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"secret not saved: {e.GetType().Name}");
            return false;
        }
    }

    public void Delete(string name)
    {
        if (FileFor(name) is not { } path)
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"secret not deleted: {e.GetType().Name}");
        }
    }

    /// <summary>A name is 1..64 of letters, digits, '.', '_' and '-', and not only dots; anything else has no file.</summary>
    internal static bool IsName(string? name) =>
        name is { Length: > 0 and <= MaxName }
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')
        && name.Trim('.').Length > 0;

    internal string? FileFor(string name) => IsName(name) ? Path.Combine(folder, name + ".bin") : null;

    /// <summary>DPAPI-encrypts <paramref name="plain"/> for this user; null when DPAPI refused.</summary>
    internal static unsafe byte[]? Protect(byte[] plain)
    {
        fixed (byte* data = plain)
        fixed (byte* entropy = Entropy)
        {
            var input = new Blob((uint)plain.Length, data);
            var salt = new Blob((uint)Entropy.Length, entropy);
            return CryptProtectData(&input, 0, &salt, 0, 0, UiForbidden, out var output) ? TakeAndFree(output) : null;
        }
    }

    /// <summary>The plain bytes of a DPAPI blob made by <see cref="Protect"/> for this user; null for anything else.</summary>
    internal static unsafe byte[]? Unprotect(byte[] sealedBytes)
    {
        fixed (byte* data = sealedBytes)
        fixed (byte* entropy = Entropy)
        {
            var input = new Blob((uint)sealedBytes.Length, data);
            var salt = new Blob((uint)Entropy.Length, entropy);
            return CryptUnprotectData(&input, 0, &salt, 0, 0, UiForbidden, out var output) ? TakeAndFree(output) : null;
        }
    }

    private static string? Decode(byte[] plain)
    {
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(plain);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
        finally
        {
            Array.Clear(plain);
        }
    }

    private static unsafe byte[] TakeAndFree(Blob output)
    {
        try
        {
            return new ReadOnlySpan<byte>(output.Data, (int)output.Length).ToArray();
        }
        finally
        {
            if (output.Data != null)
            {
                new Span<byte>(output.Data, (int)output.Length).Clear();
                _ = LocalFree((nint)output.Data);
            }
        }
    }

    // DATA_BLOB (dpapi.h).
    [StructLayout(LayoutKind.Sequential)]
    private readonly unsafe struct Blob(uint length, byte* data)
    {
        public readonly uint Length = length;
        public readonly byte* Data = data;
    }

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool CryptProtectData(Blob* dataIn, nint description, Blob* entropy, nint reserved, nint prompt, uint flags, out Blob dataOut);

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool CryptUnprotectData(Blob* dataIn, nint description, Blob* entropy, nint reserved, nint prompt, uint flags, out Blob dataOut);

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint LocalFree(nint memory);
}
