using System.Security.Cryptography;
using Edgepad.Net;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// The link's primitives against published vectors, copied from the documents themselves (fetched 2026-10-29), not
/// from memory: RFC 5869 appendix A.1 for HKDF, RFC 5903 section 8.1 for P-256 ECDH, and Test Case 16 of McGrew and
/// Viega's "The Galois/Counter Mode of Operation (GCM)", the specification NIST hosts, for AES-256-GCM.
/// </summary>
public sealed class LinkCryptoTests
{
    // RFC 5903 section 8.1: the initiator's and the responder's private keys and public points.
    internal const string I = "C88F01F510D9AC3F70A292DAA2316DE544E9AAB8AFE84049C62A9C57862D1433";
    internal const string Gix = "DAD0B65394221CF9B051E1FECA5787D098DFE637FC90B9EF945D0C3772581180";
    internal const string Giy = "5271A0461CDB8252D61F1C456FA3E59AB1F45B33ACCF5F58389E0577B8990BB3";
    internal const string R = "C6EF9C5D78AE012A011164ACB397CE2088685D8F06BF9BE0B283AB46476BEE53";
    internal const string Grx = "D12DFB5289C8D4F81208B70270398C342296970A0BCCB74C736FC7554494BF63";
    internal const string Gry = "56FBF3CA366CC23E8157854C13C58D6AAC23F046ADA30F8353E74F33039872AB";
    private const string Girx = "D6840F6B42F6EDAFD13116E0E12565202FEF8E9ECE7DCE03812464D04B9442DE";

    internal static Ephemeral Fixed(string d, string x, string y) => LinkCrypto.Import(new ECParameters
    {
        Curve = ECCurve.NamedCurves.nistP256,
        D = Convert.FromHexString(d),
        Q = new ECPoint { X = Convert.FromHexString(x), Y = Convert.FromHexString(y) },
    });

    [Fact]
    public void HkdfMatchesRfc5869TestCase1()
    {
        var ikm = Convert.FromHexString("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b");
        var salt = Convert.FromHexString("000102030405060708090a0b0c");
        var info = Convert.FromHexString("f0f1f2f3f4f5f6f7f8f9");

        var prk = LinkCrypto.Extract(salt, ikm);

        Assert.Equal(Convert.FromHexString("077709362c2e32df0ddc3f0dc47bba6390b6c73bb50f9c3122ec844ad7c2b3e5"), prk);
        Assert.Equal(
            Convert.FromHexString("3cb25f25faacd57a90434f64d0362f2a2d2d0a90cf1a5a4c5db02d56ecc4c5bf34007208d5b887185865"),
            LinkCrypto.Expand(prk, info, 42));
    }

    [Fact]
    public void EcdhMatchesRfc5903Section81BothWays()
    {
        using var initiator = Fixed(I, Gix, Giy);
        using var responder = Fixed(R, Grx, Gry);

        Assert.Equal(Convert.FromHexString("04" + Gix + Giy), initiator.PublicKey);
        Assert.Equal(Convert.FromHexString(Girx), LinkCrypto.Agree(initiator, responder.PublicKey));
        Assert.Equal(Convert.FromHexString(Girx), LinkCrypto.Agree(responder, initiator.PublicKey));
    }

    [Fact]
    public void AesGcmMatchesGcmSpecTestCase16()
    {
        var key = Convert.FromHexString("feffe9928665731c6d6a8f9467308308feffe9928665731c6d6a8f9467308308");
        var plain = Convert.FromHexString(
            "d9313225f88406e5a55909c5aff5269a86a7a9531534f7da2e4c303d8a318a721c3c0c95956809532fcf0e2449a6b525b16aedf5aa0de657ba637b39");
        var aad = Convert.FromHexString("feedfacedeadbeeffeedfacedeadbeefabaddad2");
        var iv = Convert.FromHexString("cafebabefacedbaddecaf888");
        var expectedCipher = Convert.FromHexString(
            "522dc1f099567d07f47f37a32a84427d643a8cdcbfe5c0c97598a2bd2555d1aa8cb08e48590dbb3da7b08b1056828838c5f61e6393ba7a0abcc9f662");
        var expectedTag = Convert.FromHexString("76fc6ece0f4e1768cddf8853bb2d551b");

        using var aes = new AesGcm(key, LinkCrypto.AeadTagLength);
        var cipher = new byte[plain.Length];
        var tag = new byte[LinkCrypto.AeadTagLength];
        LinkCrypto.Seal(aes, iv, plain, aad, cipher, tag);
        Assert.Equal(expectedCipher, cipher);
        Assert.Equal(expectedTag, tag);

        var opened = new byte[plain.Length];
        LinkCrypto.Open(aes, iv, cipher, tag, aad, opened);
        Assert.Equal(plain, opened);

        tag[0] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => LinkCrypto.Open(aes, iv, cipher, tag, aad, opened));
    }

    [Fact]
    public void APointOffTheCurveIsRefused()
    {
        using var own = LinkCrypto.NewKeyPair();
        using var peer = LinkCrypto.NewKeyPair();
        byte[] offCurve = [.. peer.PublicKey];
        offCurve[^1] ^= 1;

        Assert.ThrowsAny<CryptographicException>(() => LinkCrypto.Agree(own, offCurve));
        Assert.ThrowsAny<CryptographicException>(() => LinkCrypto.Agree(own, new byte[LinkWire.PublicKeyLength]));
        Assert.ThrowsAny<CryptographicException>(() => LinkCrypto.Agree(own, peer.PublicKey.AsSpan(0, 33)));
    }

    [Fact]
    public void TheCodeIsSixDigitsAndOnlyThoseSixMatch()
    {
        var sas = LinkCrypto.Sas(new byte[LinkWire.HashLength]);

        Assert.Equal(LinkCrypto.CodeDigits, sas.Length);
        Assert.True(sas.All(char.IsAsciiDigit));
        Assert.True(LinkCrypto.CodeMatches(sas, sas));
        Assert.False(LinkCrypto.CodeMatches(sas[..5], sas));
        Assert.False(LinkCrypto.CodeMatches(sas + "0", sas));
        Assert.False(LinkCrypto.CodeMatches(sas[..5] + (sas[5] == '0' ? '1' : '0'), sas));
        Assert.False(LinkCrypto.CodeMatches("", sas));
    }

    [Fact]
    public void TheCodeTurnsOnTheNonceThePhoneRevealsAfterCommitting()
    {
        // What stops a relay: the phone is bound to its nonce before it sees the laptop's, and the code is a hash over
        // both. A relay cannot pick its nonce on one leg to make that leg's code equal the other's; it gets one try in
        // a million. Here, one revealed nonce changed and nothing else: the commitment no longer opens, and the code moves.
        byte[] phoneId = [.. Enumerable.Range(0, 16).Select(i => (byte)i)];
        byte[] laptopId = [.. Enumerable.Range(16, 16).Select(i => (byte)i)];
        using var phone = Fixed(I, Gix, Giy);
        using var laptop = Fixed(R, Grx, Gry);
        var committed = new byte[LinkWire.NonceLength];
        var other = new byte[LinkWire.NonceLength];
        other[0] = 1;
        var laptopNonce = new byte[LinkWire.NonceLength];

        Assert.False(LinkCrypto.TagsEqual(LinkCrypto.Commit(phone.PublicKey, committed), LinkCrypto.Commit(phone.PublicKey, other)));
        Assert.NotEqual(
            LinkCrypto.Sas(LinkCrypto.Transcript(LinkWire.ModePair, phoneId, laptopId, phone.PublicKey, committed, laptop.PublicKey, laptopNonce)),
            LinkCrypto.Sas(LinkCrypto.Transcript(LinkWire.ModePair, phoneId, laptopId, phone.PublicKey, other, laptop.PublicKey, laptopNonce)));
    }

    [Fact]
    public void PairingAndResumeConfirmationsDifferAndEachSideHasItsOwn()
    {
        var prk = new byte[LinkCrypto.KeyLength];
        var transcript = new byte[LinkWire.HashLength];

        var tags = new[]
        {
            LinkCrypto.LaptopTag(prk, transcript, pairing: true),
            LinkCrypto.PhoneTag(prk, transcript, pairing: true),
            LinkCrypto.LaptopTag(prk, transcript, pairing: false),
            LinkCrypto.PhoneTag(prk, transcript, pairing: false),
        };

        Assert.Equal(4, tags.Select(tag => Convert.ToHexString(tag)).Distinct().Count());
    }
}
