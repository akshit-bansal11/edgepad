using System.Security.Cryptography;
using Edgepad.Trust;

namespace Edgepad.Net;

/// <summary>
/// Who this laptop is on the network link: 16 random bytes made once per install, and the name it shows. The id is
/// what a phone files its pairing key under, so it must outlive restarts; it is no secret, mDNS announces it.
/// </summary>
internal sealed class LinkIdentity(byte[] id, string name)
{
    public byte[] Id => id;

    public string Name => name;

    public string IdHex => Convert.ToHexStringLower(id);

    /// <summary>The payload of TEXT DEVICE: "id/os/name", the id being the one the link's INFO reply gives.</summary>
    public string DeviceText(string os) => $"{IdHex}/{os}/{name}";

    public static LinkIdentity ForCurrentUser(string directory) =>
        Load(Path.Combine(directory, "link-id.txt"), Environment.MachineName);

    /// <summary>
    /// The id kept at <paramref name="path"/>, or a new one written there when there is none or it is not one this
    /// app wrote. A file that is there and cannot be read is left alone and a new id is used for this run only:
    /// overwriting it would cost every paired phone its pairing for a read that may work next time.
    /// </summary>
    public static LinkIdentity Load(string path, string name)
    {
        try
        {
            var text = File.ReadAllText(path).Trim();
            if (text.Length == LinkWire.IdLength * 2 && text.All(char.IsAsciiHexDigit))
            {
                return new LinkIdentity(Convert.FromHexString(text), name);
            }
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            // Never made yet: made below.
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"This laptop's link id could not be read from {path}, so a new one is used until it can: {e.Message}");
            return new LinkIdentity(RandomNumberGenerator.GetBytes(LinkWire.IdLength), name);
        }

        var identity = new LinkIdentity(RandomNumberGenerator.GetBytes(LinkWire.IdLength), name);
        try
        {
            PrivateStorage.Replace(path, temp => File.WriteAllText(temp, identity.IdHex + "\n"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"This laptop's link id could not be saved to {path}, so phones paired now must pair again after a restart: {e.Message}");
        }

        return identity;
    }
}
