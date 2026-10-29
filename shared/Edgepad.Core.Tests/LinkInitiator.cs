using System.Security.Cryptography;
using Edgepad.Net;

namespace Edgepad.Tests;

/// <summary>What a phone came away with: the laptop's status, and on success the encrypted stream and, after a pairing, the key to resume with.</summary>
internal sealed record Initiated(byte Status, Stream? Secure, byte[] PairingKey);

/// <summary>
/// The phone's half of EDGL1, test-only, step for step as docs/PROTOCOL.md and <see cref="LinkResponder"/> have it.
/// It is the readable reference the Kotlin port follows, so it takes no shortcut the phone could not take.
///
/// A pairing: commit to a fresh key, read the laptop's key, reveal, show the code (the owner types it on the laptop),
/// then check the laptop's confirmation before sending the phone's own. A laptop that closes, or confirms the wrong
/// keys, leaves the phone with no stream: the owner did not type this phone's code there.
/// </summary>
internal static class LinkInitiator
{
    public static LinkReply Info(Stream link)
    {
        LinkWire.Write(link, LinkWire.EncodeInfoRequest());
        return LinkWire.DecodeReply(LinkWire.ModeInfo, LinkWire.Read(link));
    }

    public static Initiated Pair(Stream link, byte[] phoneId, string name, Action<string> showCode)
    {
        using var own = LinkCrypto.NewKeyPair();
        var nonce = RandomNumberGenerator.GetBytes(LinkWire.NonceLength);
        LinkWire.Write(link, LinkWire.EncodePairRequest(phoneId, name, LinkCrypto.Commit(own.PublicKey, nonce)));
        var offer = LinkWire.DecodeReply(LinkWire.ModePair, LinkWire.Read(link));
        if (offer.Status != LinkWire.StatusOk)
        {
            return new Initiated(offer.Status, null, []);
        }

        LinkWire.Write(link, LinkWire.Reveal(own.PublicKey, nonce));
        var transcript = LinkCrypto.Transcript(LinkWire.ModePair, phoneId, offer.LaptopId, own.PublicKey, nonce, offer.PublicKey, offer.Nonce);
        var shared = LinkCrypto.Agree(own, offer.PublicKey);
        var prk = LinkCrypto.PairPrk(transcript, shared);
        CryptographicOperations.ZeroMemory(shared);
        try
        {
            showCode(LinkCrypto.Sas(transcript));

            byte[] tag;
            try
            {
                tag = LinkWire.DecodeTag(LinkWire.Read(link));
            }
            catch (EndOfStreamException)
            {
                return new Initiated(LinkWire.StatusOk, null, []);
            }

            if (!LinkCrypto.TagsEqual(tag, LinkCrypto.LaptopTag(prk, transcript, pairing: true)))
            {
                return new Initiated(LinkWire.StatusOk, null, []);
            }

            LinkWire.Write(link, LinkWire.Tag(LinkCrypto.PhoneTag(prk, transcript, pairing: true)));
            return new Initiated(LinkWire.StatusOk, new SecureStream(link, prk, isPhone: true), LinkCrypto.PairingKey(prk));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(prk);
        }
    }

    /// <param name="confirmAnyway">
    /// Sends the phone's confirmation even when the laptop's did not check out, which a real phone never does: it lets
    /// a test show the laptop refusing a phone with the wrong key on its own account.
    /// </param>
    public static Initiated Resume(Stream link, byte[] phoneId, byte[] laptopId, byte[] pairingKey, bool confirmAnyway = false)
    {
        using var own = LinkCrypto.NewKeyPair();
        var nonce = RandomNumberGenerator.GetBytes(LinkWire.NonceLength);
        LinkWire.Write(link, LinkWire.EncodeResumeRequest(phoneId, own.PublicKey, nonce));
        var reply = LinkWire.DecodeReply(LinkWire.ModeResume, LinkWire.Read(link));
        if (reply.Status != LinkWire.StatusOk)
        {
            return new Initiated(reply.Status, null, []);
        }

        var transcript = LinkCrypto.Transcript(LinkWire.ModeResume, phoneId, laptopId, own.PublicKey, nonce, reply.PublicKey, reply.Nonce);
        var shared = LinkCrypto.Agree(own, reply.PublicKey);
        var prk = LinkCrypto.ResumePrk(transcript, shared, pairingKey);
        CryptographicOperations.ZeroMemory(shared);
        try
        {
            var laptopConfirmed = LinkCrypto.TagsEqual(reply.Tag, LinkCrypto.LaptopTag(prk, transcript, pairing: false));
            if (laptopConfirmed || confirmAnyway)
            {
                LinkWire.Write(link, LinkWire.Tag(LinkCrypto.PhoneTag(prk, transcript, pairing: false)));
            }

            return new Initiated(LinkWire.StatusOk, laptopConfirmed ? new SecureStream(link, prk, isPhone: true) : null, []);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(prk);
        }
    }
}
