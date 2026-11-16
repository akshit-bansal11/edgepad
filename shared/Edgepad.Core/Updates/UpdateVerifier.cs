using System.Security.Cryptography;

namespace Edgepad.Updates;

/// <summary>
/// Decides whether a downloaded release file may be installed. The order the caller must follow, and each step
/// stops everything after it when it fails, so that any failure leaves nothing installed:
/// 1. fetch SHA256SUMS.txt (<see cref="UpdateCheck.SumsUrl"/>); 2. fetch its signature (<see cref="UpdateCheck.SigUrl"/>);
/// 3. <see cref="VerifySums"/> against <see cref="UpdateKey"/>; 4. <see cref="TryHashFor"/> the exact asset name of the
/// offered version; 5. <see cref="UpdateDownload.DownloadAsync"/> to a ".download" file; 6. <see cref="HashesMatch"/>
/// the file's hash with the listed one; 7. only then move the file into place. Naming <c>Edgepad-{offered}.*</c> in
/// step 4 is what stops a replayed, older signed release from being installed as a different version.
/// </summary>
internal static class UpdateVerifier
{
    private const int HashHexLength = 64;
    private const string P256Oid = "1.2.840.10045.3.1.7";

    /// <summary>True only when <paramref name="derSignature"/> is a valid ECDSA P-256 / SHA-256 signature of <paramref name="sums"/>. Any exception is false.</summary>
    public static bool VerifySums(ReadOnlySpan<byte> sums, ReadOnlySpan<byte> derSignature, ReadOnlySpan<byte> spki)
    {
        try
        {
            if (sums.IsEmpty || derSignature.IsEmpty || spki.IsEmpty)
            {
                return false;
            }

            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(spki, out var read);
            if (read != spki.Length)
            {
                return false;
            }

            var curve = key.ExportParameters(false).Curve;
            if (!curve.IsNamed || curve.Oid.Value != P256Oid)
            {
                return false;
            }

            return key.VerifyData(sums, derSignature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (Exception e)
        {
            Log.Write($"The update signature could not be checked: {e.GetType().Name}");
            return false;
        }
    }

    /// <summary>
    /// The SHA-256 the list <paramref name="sums"/> gives <paramref name="fileName"/>. Lines are sha256sum's:
    /// 64 lowercase hex digits, then "  name" or " *name". A line in any other shape, a name holding / or \,
    /// or the asked-for name listed twice makes the whole list unusable.
    /// </summary>
    public static bool TryHashFor(string sums, string fileName, out byte[] hash)
    {
        hash = [];
        if (fileName.Length == 0 || fileName.AsSpan().ContainsAny('/', '\\'))
        {
            return false;
        }

        byte[]? found = null;
        foreach (var raw in sums.Split('\n'))
        {
            var line = raw.EndsWith('\r') ? raw[..^1] : raw;
            if (line.Length == 0)
            {
                continue;
            }

            if (!TryParseLine(line, out var lineHash, out var name))
            {
                return false;
            }

            if (name == fileName)
            {
                if (found is not null)
                {
                    return false;
                }

                found = lineHash;
            }
        }

        if (found is null)
        {
            return false;
        }

        hash = found;
        return true;
    }

    public static byte[] HashFile(Stream file) => SHA256.HashData(file);

    /// <summary>Equal hashes, compared in constant time.</summary>
    public static bool HashesMatch(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b) =>
        a.Length == SHA256.HashSizeInBytes && CryptographicOperations.FixedTimeEquals(a, b);

    private static bool TryParseLine(string line, out byte[] hash, out string name)
    {
        hash = [];
        name = "";
        if (line.Length <= HashHexLength + 2 || line[HashHexLength] != ' ' || line[HashHexLength + 1] is not (' ' or '*'))
        {
            return false;
        }

        var hex = line.AsSpan(0, HashHexLength);
        foreach (var c in hex)
        {
            if (c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
            {
                return false;
            }
        }

        name = line[(HashHexLength + 2)..];
        if (name.AsSpan().ContainsAny('/', '\\'))
        {
            return false;
        }

        hash = Convert.FromHexString(hex);
        return true;
    }
}
