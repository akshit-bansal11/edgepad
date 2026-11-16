namespace Edgepad.Updates;

/// <summary>
/// The public half of the key that signs every release's SHA256SUMS.txt: a base64 SubjectPublicKeyInfo of a NIST P-256
/// key, the same line as protocol/update-key.txt (release.yml refuses a tag whose signing key differs). Were it
/// empty, no update would be installed and the release page opened instead. A public key is not a secret, but it
/// is never logged either, so a log cannot be mistaken for it.
/// </summary>
internal static class UpdateKey
{
    public const string PublicKeySpkiBase64 =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEKIO+dzmbn/k8aLGN5vAKTvguNumWGY8btuwcpUEMCCMa+GPz9jPPHs3QBi7dOPVZRCyCOrTmtibnj2Ex5K3dIA==";

    public static bool IsSet => PublicKeySpkiBase64.Length > 0;

    /// <summary>The key's bytes; empty when it is unset or is not base64, which makes every verification fail.</summary>
    public static byte[] Spki()
    {
        try
        {
            return Convert.FromBase64String(PublicKeySpkiBase64);
        }
        catch (FormatException)
        {
            return [];
        }
    }
}
