using System.Security.Cryptography;
using Edgepad.Protocol;
using Edgepad.Trust;

namespace Edgepad.Net;

/// <summary>A network link that came through: the encrypted stream to read frames from, and who is on the other end.</summary>
internal sealed record LinkResult(Stream Secure, string Address, string PhoneName, bool JustPaired);

/// <summary>
/// The laptop's half of pairing: a window that names the phone and asks the owner to type the code the phone shows.
/// It must not block. Exactly one of the callbacks may be called, from any thread: the digits typed, or the owner
/// cancelling. Disposing the handle withdraws the window, answered or not.
/// </summary>
internal interface IPairingPrompt
{
    IDisposable Show(string phoneName, Action<string> onTyped, Action onOwnerCancelled);
}

/// <summary>
/// Everything a laptop does with a network connection before its first frame: EDGL1, in the cleartext messages of
/// <see cref="LinkWire"/>, ending in an encrypted <see cref="SecureStream"/> or in nothing.
///
/// INFO answers who this laptop is and closes. PAIR runs an ephemeral P-256 exchange in which the phone commits to
/// its key before it sees the laptop's, so a relay in the middle cannot steer both legs to one code; the phone then
/// shows six digits from the transcript, and the owner types them here. The laptop proves it matched before the
/// phone proves anything, and only after the phone's own proof is the phone trusted and its pairing key kept.
/// Nothing in a pairing is decided by the phone alone: any device on the network can run the exchange and work out
/// its own code, but only the owner at this laptop can type it. RESUME mixes that key into a fresh exchange,
/// and both ends prove they hold it.
///
/// Every wait for the phone ends through <c>giveUp</c>, which must close the link so the blocked read ends, as in
/// <see cref="Handshake"/>. Null, or any exception, means the caller closes <c>raw</c>. Nothing secret is logged:
/// not the code, not a key, only the phone's link address.
/// </summary>
internal static class LinkResponder
{
    /// <summary>How long the phone may take over each message.</summary>
    public static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(10);

    /// <summary>How long the owner has to type the code.</summary>
    public static readonly TimeSpan OwnerTimeout = TimeSpan.FromSeconds(60);

    private enum OwnerAnswer
    {
        Typed,
        Cancelled,
        TimedOut,
        Ended,
    }

    /// <summary>The address a network phone goes by in the trust file, the PIN gate and the log.</summary>
    public static string AddressOf(byte[] phoneId) => "link:" + Convert.ToHexStringLower(phoneId);

    public static LinkResult? Accept(
        Stream raw,
        LinkIdentity self,
        PairedLinks paired,
        PairingLimiter limiter,
        IPairingPrompt prompt,
        TrustStore trust,
        Action<string> log,
        Action giveUp,
        CancellationToken ended,
        TimeSpan? messageTimeout = null,
        TimeSpan? ownerTimeout = null)
    {
        var wait = messageTimeout ?? MessageTimeout;
        var body = ReadWithin(raw, wait, "request", log, giveUp);
        if (body is null)
        {
            return null;
        }

        switch (LinkWire.DecodeRequest(body))
        {
            case UnsupportedRequest unsupported:
                LinkWire.Write(raw, LinkWire.Status(LinkWire.StatusUnsupported));
                log($"Refused a network phone on link version {unsupported.Version}; this laptop speaks {LinkWire.LinkVersion}");
                return null;

            case InfoRequest:
                LinkWire.Write(raw, LinkWire.InfoReply(self.Id, ProtocolConstants.Version, self.Name));
                return null;

            case PairRequest pair:
                return Pair(raw, pair, self, paired, limiter, prompt, trust, log, giveUp, wait, ownerTimeout ?? OwnerTimeout, ended);

            case ResumeRequest resume:
                return Resume(raw, resume, self, paired, log, giveUp, wait);

            default:
                return null;
        }
    }

    private static LinkResult? Pair(
        Stream raw,
        PairRequest request,
        LinkIdentity self,
        PairedLinks paired,
        PairingLimiter limiter,
        IPairingPrompt prompt,
        TrustStore trust,
        Action<string> log,
        Action giveUp,
        TimeSpan wait,
        TimeSpan ownerWait,
        CancellationToken ended)
    {
        var address = AddressOf(request.PhoneId);
        if (!limiter.TryBegin())
        {
            LinkWire.Write(raw, LinkWire.Status(LinkWire.StatusBusy));
            log($"Refused to pair {address}: another pairing is under way, or pairing is locked after failed attempts");
            return null;
        }

        // Anything that ends this short of a pairing counts against the lock, except the owner's own word.
        var outcome = PairingOutcome.Failed;
        byte[]? prk = null;
        try
        {
            using var own = LinkCrypto.NewKeyPair();
            var nonce = RandomNumberGenerator.GetBytes(LinkWire.NonceLength);
            LinkWire.Write(raw, LinkWire.PairOffer(self.Id, self.Name, own.PublicKey, nonce));

            var revealed = ReadWithin(raw, wait, "key", log, giveUp);
            if (revealed is null)
            {
                return null;
            }

            var (phonePublic, phoneNonce) = LinkWire.DecodeReveal(revealed);
            if (!LinkCrypto.TagsEqual(LinkCrypto.Commit(phonePublic, phoneNonce), request.Commit))
            {
                log($"Refused to pair {address}: its key is not the one it committed to");
                return null;
            }

            var transcript = LinkCrypto.PairTranscript(request.PhoneId, self.Id, request.NameField, LinkWire.NameField(self.Name), phonePublic, phoneNonce, own.PublicKey, nonce);
            var shared = LinkCrypto.Agree(own, phonePublic);
            try
            {
                prk = LinkCrypto.PairPrk(transcript, shared);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(shared);
            }

            var (answer, typed) = AskOwner(prompt, request.Name, ownerWait, ended);
            switch (answer)
            {
                case OwnerAnswer.Cancelled:
                    outcome = PairingOutcome.Abandoned;
                    log($"The owner cancelled pairing {address}");
                    return null;
                case OwnerAnswer.Ended:
                    outcome = PairingOutcome.Abandoned;
                    log($"Stopped pairing {address}: the network link was closed");
                    return null;
                case OwnerAnswer.TimedOut:
                    log($"Refused to pair {address}: no code was typed within {(int)ownerWait.TotalSeconds} s");
                    return null;
                default:
                    break;
            }

            // The one check that needs the owner. Until it passes, nothing that depends on it is sent.
            if (!LinkCrypto.CodeMatches(typed, LinkCrypto.Sas(transcript)))
            {
                log($"Refused to pair {address}: the code typed on this laptop is not the one that phone shows");
                return null;
            }

            LinkWire.Write(raw, LinkWire.Tag(LinkCrypto.LaptopTag(prk, transcript, pairing: true)));
            var confirmed = ReadWithin(raw, wait, "confirmation", log, giveUp);
            if (confirmed is null)
            {
                return null;
            }

            if (!LinkCrypto.TagsEqual(LinkWire.DecodeTag(confirmed), LinkCrypto.PhoneTag(prk, transcript, pairing: true)))
            {
                log($"Refused to pair {address}: it did not confirm the same keys");
                return null;
            }

            var key = LinkCrypto.PairingKey(prk);
            try
            {
                if (!paired.Add(Convert.ToHexStringLower(request.PhoneId), key, request.Name))
                {
                    log($"Paired {address} for this connection only: the pairing could not be saved, so it will pair again next time");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }

            trust.Grant(address);
            outcome = PairingOutcome.Paired;
            log($"Paired {address}");
            return new LinkResult(new SecureStream(raw, prk, isPhone: false), address, request.Name, JustPaired: true);
        }
        catch (CryptographicException)
        {
            log($"Refused to pair {address}: its key is not a valid P-256 point");
            return null;
        }
        finally
        {
            if (prk is not null)
            {
                CryptographicOperations.ZeroMemory(prk);
            }

            limiter.End(outcome);
        }
    }

    private static LinkResult? Resume(
        Stream raw,
        ResumeRequest request,
        LinkIdentity self,
        PairedLinks paired,
        Action<string> log,
        Action giveUp,
        TimeSpan wait)
    {
        var address = AddressOf(request.PhoneId);
        if (!paired.TryGet(Convert.ToHexStringLower(request.PhoneId), out var key, out var name))
        {
            LinkWire.Write(raw, LinkWire.Status(LinkWire.StatusUnknownPhone));
            log($"Refused {address}: it is not paired with this laptop, or the pairings cannot be read");
            return null;
        }

        byte[]? prk = null;
        try
        {
            using var own = LinkCrypto.NewKeyPair();
            var nonce = RandomNumberGenerator.GetBytes(LinkWire.NonceLength);
            var transcript = LinkCrypto.ResumeTranscript(request.PhoneId, self.Id, request.PublicKey, request.Nonce, own.PublicKey, nonce);
            var shared = LinkCrypto.Agree(own, request.PublicKey);
            try
            {
                prk = LinkCrypto.ResumePrk(transcript, shared, key);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(shared);
            }

            LinkWire.Write(raw, LinkWire.ResumeReply(own.PublicKey, nonce, LinkCrypto.LaptopTag(prk, transcript, pairing: false)));
            var confirmed = ReadWithin(raw, wait, "confirmation", log, giveUp);
            if (confirmed is null)
            {
                return null;
            }

            if (!LinkCrypto.TagsEqual(LinkWire.DecodeTag(confirmed), LinkCrypto.PhoneTag(prk, transcript, pairing: false)))
            {
                log($"Refused {address}: it does not hold the key this laptop paired it with");
                return null;
            }

            return new LinkResult(new SecureStream(raw, prk, isPhone: false), address, name, JustPaired: false);
        }
        catch (CryptographicException)
        {
            log($"Refused {address}: its key is not a valid P-256 point");
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (prk is not null)
            {
                CryptographicOperations.ZeroMemory(prk);
            }
        }
    }

    /// <summary>
    /// Puts the code question to the owner and waits for whichever comes first: the digits, a cancel, the timer, or
    /// the link ending. The answer is claimed once, so a click landing with the timer is either taken or ignored,
    /// never both.
    /// </summary>
    private static (OwnerAnswer Answer, string Typed) AskOwner(IPairingPrompt prompt, string phoneName, TimeSpan wait, CancellationToken ended)
    {
        var answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (prompt.Show(phoneName, digits => answer.TrySetResult(digits), () => answer.TrySetResult(null)))
        {
            try
            {
                _ = answer.Task.Wait(wait, ended);
            }
            catch (OperationCanceledException)
            {
                // The link ended under the question; settled below.
            }

            _ = answer.TrySetCanceled(CancellationToken.None);
        }

        if (ended.IsCancellationRequested)
        {
            return (OwnerAnswer.Ended, "");
        }

        if (answer.Task.IsCanceled)
        {
            return (OwnerAnswer.TimedOut, "");
        }

        return answer.Task.Result is { } digits ? (OwnerAnswer.Typed, digits) : (OwnerAnswer.Cancelled, "");
    }

    /// <summary>
    /// The phone's next message, or null when <paramref name="wait"/> ran out first and <paramref name="giveUp"/> was
    /// called. The same claim as <see cref="Handshake"/>'s: a message landing right at the deadline is not used.
    /// </summary>
    private static byte[]? ReadWithin(Stream raw, TimeSpan wait, string what, Action<string> log, Action giveUp)
    {
        var expired = 0;
        byte[] body;
        using (new System.Threading.Timer(
            _ =>
            {
                if (Interlocked.Exchange(ref expired, 1) != 0)
                {
                    return;
                }

                log($"Gave up on a network phone: no {what} within {(int)wait.TotalSeconds} s");
                giveUp();
            },
            null,
            wait,
            Timeout.InfiniteTimeSpan))
        {
            body = LinkWire.Read(raw);
        }

        return Interlocked.Exchange(ref expired, 1) != 0 ? null : body;
    }
}
