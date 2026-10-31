using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;

namespace Edgepad.Net;

/// <summary>
/// The arithmetic of EDGL1, all of it platform crypto: P-256 ECDH, HKDF and HMAC over SHA-256, AES-256-GCM. Every
/// derivation both ends must agree on lives here, once, so the responder, the test phone and the vectors the
/// Kotlin and Swift ports check themselves against all run the same code.
///
/// Every comparison of a tag, a commitment or a code goes through <see cref="CryptographicOperations.FixedTimeEquals"/>:
/// a compare that stops at the first wrong byte tells a patient attacker how many leading bytes were right.
/// </summary>
internal static class LinkCrypto
{
    public const int KeyLength = 32;
    public const int AeadTagLength = 16;
    public const int NonceLength = 12;
    public const int CodeDigits = 6;

    private static readonly byte[] TranscriptLabel = "EDGL1 th"u8.ToArray();
    private static readonly byte[] SasLabel = "EDGL1 sas"u8.ToArray();
    private static readonly byte[] CommitLabel = "EDGL1 commit"u8.ToArray();
    private static readonly byte[] PairLabel = "pair"u8.ToArray();

    // P-256's field prime and b (SEC 2, section 2.4.2); a is -3. The leading 0 keeps the parse positive.
    private static readonly BigInteger P = BigInteger.Parse(
        "0FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF", NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static readonly BigInteger B = BigInteger.Parse(
        "05AC635D8AA3A93E7B3EBBD55769886BC651D06B0CC53B0F63BCE3C3E27D2604B", NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    /// <summary>A fresh key pair, used for one connection and disposed with it.</summary>
    public static Ephemeral NewKeyPair() => new(ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256));

    /// <summary>A key pair from known values, for test vectors only: a fixed private key has no place on a link.</summary>
    public static Ephemeral Import(ECParameters parameters) => new(ECDiffieHellman.Create(parameters));

    /// <summary>
    /// The raw shared secret, the X coordinate of the shared point, as Android's <c>KeyAgreement "ECDH"</c> and
    /// CryptoKit also give it. The peer's key must be a 65-byte uncompressed point on the curve, checked here rather
    /// than left to the platform's import: Windows reports a point off the curve as PlatformNotSupportedException,
    /// Linux as CryptographicException, and the Kotlin port makes the same check by hand. The encoding cannot
    /// express the point at infinity. A bad key is a <see cref="CryptographicException"/> and the caller closes.
    /// The caller zeroes what this returns.
    /// </summary>
    public static byte[] Agree(Ephemeral own, ReadOnlySpan<byte> peerPublic)
    {
        if (peerPublic.Length != LinkWire.PublicKeyLength || peerPublic[0] != 0x04 || !OnCurve(peerPublic[1..33], peerPublic[33..]))
        {
            throw new CryptographicException("The peer's key is not an uncompressed P-256 point");
        }

        using var peer = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = peerPublic[1..33].ToArray(), Y = peerPublic[33..].ToArray() },
        });
        return own.Key.DeriveRawSecretAgreement(peer.PublicKey);
    }

    /// <summary>True when (x, y), big-endian, are both below p and satisfy y² = x³ − 3x + b (mod p).</summary>
    private static bool OnCurve(ReadOnlySpan<byte> xBytes, ReadOnlySpan<byte> yBytes)
    {
        var x = new BigInteger(xBytes, isUnsigned: true, isBigEndian: true);
        var y = new BigInteger(yBytes, isUnsigned: true, isBigEndian: true);
        if (x >= P || y >= P)
        {
            return false;
        }

        var right = BigInteger.Remainder((BigInteger.ModPow(x, 3, P) - (3 * x) + B) % P + P, P);
        return BigInteger.ModPow(y, 2, P) == right;
    }

    /// <summary>HKDF-Extract (RFC 5869) with SHA-256.</summary>
    public static byte[] Extract(ReadOnlySpan<byte> salt, ReadOnlySpan<byte> ikm)
    {
        var prk = new byte[KeyLength];
        HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt, prk);
        return prk;
    }

    /// <summary>HKDF-Expand (RFC 5869) with SHA-256.</summary>
    public static byte[] Expand(ReadOnlySpan<byte> prk, ReadOnlySpan<byte> info, int length)
    {
        var okm = new byte[length];
        HKDF.Expand(HashAlgorithmName.SHA256, prk, okm, info);
        return okm;
    }

    public static byte[] Hash(params ReadOnlySpan<byte[]> parts)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var part in parts)
        {
            hash.AppendData(part);
        }

        return hash.GetHashAndReset();
    }

    public static byte[] Hmac(ReadOnlySpan<byte> key, params ReadOnlySpan<byte[]> parts)
    {
        using var mac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key);
        foreach (var part in parts)
        {
            mac.AppendData(part);
        }

        return mac.GetHashAndReset();
    }

    /// <summary>What the phone binds itself to before it sees the laptop's key and nonce.</summary>
    public static byte[] Commit(byte[] phonePublic, byte[] phoneNonce) => Hash(CommitLabel, phonePublic, phoneNonce);

    /// <summary>
    /// Everything both sides said in a pairing, in one hash: any difference between their views changes the code and
    /// every key. The names are the fields as sent (<see cref="LinkWire.NameField"/>), so a relay cannot change the name
    /// the laptop shows the owner, or the phone shows its owner, without changing the code too.
    /// </summary>
    public static byte[] PairTranscript(byte[] phoneId, byte[] laptopId, byte[] phoneNameField, byte[] laptopNameField, byte[] phonePublic, byte[] phoneNonce, byte[] laptopPublic, byte[] laptopNonce) =>
        Hash(TranscriptLabel, [LinkWire.ModePair], phoneId, laptopId, phoneNameField, laptopNameField, phonePublic, phoneNonce, laptopPublic, laptopNonce);

    /// <summary>A resume's transcript: no names travel in a resume, so none are in it.</summary>
    public static byte[] ResumeTranscript(byte[] phoneId, byte[] laptopId, byte[] phonePublic, byte[] phoneNonce, byte[] laptopPublic, byte[] laptopNonce) =>
        Hash(TranscriptLabel, [LinkWire.ModeResume], phoneId, laptopId, phonePublic, phoneNonce, laptopPublic, laptopNonce);

    /// <summary>
    /// The six digits the phone shows and the laptop's owner types: the first four bytes of a hash of the transcript,
    /// big-endian, mod a million, zero padded. Anyone on the connection can work it out; what it proves is only that
    /// the two ends saw the same transcript, which a relay in the middle cannot arrange.
    /// </summary>
    public static string Sas(byte[] transcript)
    {
        var value = BinaryPrimitives.ReadUInt32BigEndian(Hash(SasLabel, transcript)) % 1_000_000;
        return value.ToString("D6", CultureInfo.InvariantCulture);
    }

    /// <summary>Whether the owner typed exactly the six digits of <paramref name="sas"/>, compared in constant time.</summary>
    public static bool CodeMatches(string typed, string sas) =>
        typed.Length == CodeDigits && typed.All(char.IsAsciiDigit)
        && CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(typed), System.Text.Encoding.ASCII.GetBytes(sas));

    public static bool TagsEqual(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b) => CryptographicOperations.FixedTimeEquals(a, b);

    public static byte[] PairPrk(byte[] transcript, ReadOnlySpan<byte> shared) => Extract(transcript, shared);

    /// <summary>A resume mixes the pairing key into a fresh exchange: forward secrecy from the one, the pairing from the other.</summary>
    public static byte[] ResumePrk(byte[] transcript, ReadOnlySpan<byte> shared, ReadOnlySpan<byte> pairingKey)
    {
        var ikm = new byte[shared.Length + pairingKey.Length];
        try
        {
            shared.CopyTo(ikm);
            pairingKey.CopyTo(ikm.AsSpan(shared.Length));
            return Extract(transcript, ikm);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ikm);
        }
    }

    /// <summary>The laptop's key confirmation. In a pairing it is sent only after the owner typed the phone's code.</summary>
    public static byte[] LaptopTag(byte[] prk, byte[] transcript, bool pairing) => ConfirmTag(prk, "EDGL1 confirm laptop"u8, transcript, pairing);

    public static byte[] PhoneTag(byte[] prk, byte[] transcript, bool pairing) => ConfirmTag(prk, "EDGL1 confirm phone"u8, transcript, pairing);

    /// <summary>The key both ends keep after a pairing, under the other's id, to resume with.</summary>
    public static byte[] PairingKey(byte[] prk) => Expand(prk, "EDGL1 pairing key"u8, KeyLength);

    /// <summary>The record keys for each direction, and the 8 bytes whose halves prefix each direction's nonces.</summary>
    public static (byte[] PhoneToLaptop, byte[] LaptopToPhone, byte[] Iv) SessionKeys(ReadOnlySpan<byte> prk) =>
        (Expand(prk, "EDGL1 key p2l"u8, KeyLength), Expand(prk, "EDGL1 key l2p"u8, KeyLength), Expand(prk, "EDGL1 iv"u8, 8));

    /// <summary>A record nonce: the direction's 4-byte prefix, then the record counter, big-endian.</summary>
    public static void Nonce(ReadOnlySpan<byte> prefix, ulong counter, Span<byte> nonce)
    {
        prefix.CopyTo(nonce);
        BinaryPrimitives.WriteUInt64BigEndian(nonce[4..], counter);
    }

    public static void Seal(AesGcm aes, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> plain, ReadOnlySpan<byte> aad, Span<byte> cipher, Span<byte> tag) =>
        aes.Encrypt(nonce, plain, cipher, tag, aad);

    /// <summary>Decrypts and checks the tag; a record that fails throws <see cref="AuthenticationTagMismatchException"/>.</summary>
    public static void Open(AesGcm aes, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> cipher, ReadOnlySpan<byte> tag, ReadOnlySpan<byte> aad, Span<byte> plain) =>
        aes.Decrypt(nonce, cipher, tag, plain, aad);

    private static byte[] ConfirmTag(byte[] prk, ReadOnlySpan<byte> label, byte[] transcript, bool pairing)
    {
        var key = Expand(prk, label, KeyLength);
        try
        {
            return pairing ? Hmac(key, PairLabel, transcript) : Hmac(key, transcript);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }
}

/// <summary>One side's key pair for one connection, and its public half as the 65-byte point the wire carries.</summary>
internal sealed class Ephemeral : IDisposable
{
    public Ephemeral(ECDiffieHellman key)
    {
        Key = key;
        var q = key.ExportParameters(false).Q;
        PublicKey = [0x04, .. q.X!, .. q.Y!];
    }

    public ECDiffieHellman Key { get; }

    public byte[] PublicKey { get; }

    public void Dispose() => Key.Dispose();
}
