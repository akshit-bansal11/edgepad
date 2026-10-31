using System.Security.Cryptography;
using Edgepad.Protocol;
using Edgepad.Trust;

namespace Edgepad.Net;

/// <summary>A network link that came through: the encrypted stream to read frames from, and who is on the other end.</summary>
internal sealed record LinkResult(Stream Secure, string Address, string PhoneName, bool JustPaired);

/// <summary>
/// The laptop's half of pairing: a window that names the phone and asks the owner to type the code the phone shows.
/// It must not block. Exactly one of the callbacks may be called, from any thread: the digits typed, or the owner
/// cancelling. The question it returns is then told how the typed code went.
/// </summary>
internal interface IPairingPrompt
{
    IPairingQuestion Show(string phoneName, Action<string> onTyped, Action onOwnerCancelled);
}

/// <summary>
/// One pairing question on screen. After the owner types a code it is told either <see cref="Mismatched"/>, or
/// <see cref="Finished"/> (with <see cref="Matched"/> before it when the code matched), each at most once, from any
/// thread, never blocking. Disposing it takes down a question still asking for the code; once it has been told an
/// outcome, the window stays up to show it.
/// </summary>
internal interface IPairingQuestion : IDisposable
{
    /// <summary>The code matched and the phone was told so; now the phone's owner confirms on the phone.</summary>
    void Matched();

    /// <summary>The code typed is not the one the phone shows; nothing was paired.</summary>
    void Mismatched();

    /// <summary>The pairing ended after the code was typed: paired, or not (no confirmation, or a Forget).</summary>
    void Finished(bool paired);
}

/// <summary>What the pairing window says once the code is typed, the same on Windows and Linux.</summary>
internal static class PairingText
{
    public const string Mismatched = "That code did not match. Nothing was paired.";

    public const string NotFinished = "Pairing did not finish.";

    public static string Matched(string phoneName) => $"Code matched. Tap Yes on {phoneName} to finish.";

    public static string Paired(string phoneName) => $"Paired with {phoneName}.";
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
        TimeSpan? ownerTimeout = null,
        Func<bool>? refusing = null)
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
                // While the owner holds phones off (a Forget, the Security window), no pairing may even start.
                if (refusing?.Invoke() == true)
                {
                    LinkWire.Write(raw, LinkWire.Status(LinkWire.StatusBusy));
                    log($"Refused to pair {AddressOf(pair.PhoneId)}: the owner is changing security settings, or this laptop is forgetting its phones");
                    return null;
                }

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

        // Read before anything else: a Forget moves them, and a pairing that outlives one keeps and trusts nothing.
        var trustGeneration = trust.Generation;
        var pairedGeneration = paired.Generation;

        // Anything that ends this short of a pairing counts against the lock, except the owner's own word.
        var outcome = PairingOutcome.Failed;
        byte[]? prk = null;
        IPairingQuestion? question = null;

        // Set once the window has said the code matched: from then on it is told how the pairing ended, whatever ends it.
        var matched = false;
        var name = PhoneLabel.Shown(request.Name);

        bool ForgottenSince() => trust.Generation != trustGeneration || paired.Generation != pairedGeneration;

        LinkResult? Forgotten()
        {
            outcome = PairingOutcome.Abandoned;
            log($"Did not pair {address}: the trusted phones were forgotten while it was pairing");
            return null;
        }

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

            var asked = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            question = prompt.Show(name, digits => asked.TrySetResult(digits), () => asked.TrySetResult(null));
            var (answer, typed) = AwaitOwner(asked, ownerWait, ended);
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

            // The owner forgot the trusted phones while this one was pairing: the later word, and the owner's, so
            // it does not count against the lock.
            if (ForgottenSince())
            {
                question.Finished(paired: false);
                return Forgotten();
            }

            // The one check that needs the owner. Until it passes, nothing that depends on it is sent.
            if (!LinkCrypto.CodeMatches(typed, LinkCrypto.Sas(transcript)))
            {
                question.Mismatched();
                log($"Refused to pair {address}: the code typed on this laptop is not the one that phone shows");
                return null;
            }

            matched = true;
            LinkWire.Write(raw, LinkWire.Tag(LinkCrypto.LaptopTag(prk, transcript, pairing: true)));
            question.Matched();

            // The phone's owner confirms too, on the phone, that this laptop said the code matched: the only check
            // that tells the phone it reached this laptop and not something in between. That takes a person, so the
            // wait is the owner's, not a message's, and the listener's per-message read deadline must not cut it short.
            var confirmed = ReadOwnerLength(raw, ownerWait, log, giveUp);
            if (confirmed is null)
            {
                return null;
            }

            if (!LinkCrypto.TagsEqual(LinkWire.DecodeTag(confirmed), LinkCrypto.PhoneTag(prk, transcript, pairing: true)))
            {
                log($"Refused to pair {address}: it did not confirm the same keys");
                return null;
            }

            if (ForgottenSince())
            {
                return Forgotten();
            }

            var key = LinkCrypto.PairingKey(prk);
            try
            {
                if (!paired.Add(Convert.ToHexStringLower(request.PhoneId), key, request.Name, pairedGeneration))
                {
                    if (paired.Generation != pairedGeneration)
                    {
                        return Forgotten();
                    }

                    log($"Paired {address} for this connection only: the pairing could not be saved, so it will pair again next time");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }

            if (!trust.Grant(address, trustGeneration))
            {
                return Forgotten();
            }

            outcome = PairingOutcome.Paired;
            log($"Paired {address}");
            return new LinkResult(new SecureStream(raw, prk, isPhone: false), address, name, JustPaired: true);
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

            if (matched)
            {
                question?.Finished(outcome == PairingOutcome.Paired);
            }

            question?.Dispose();
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

            return new LinkResult(new SecureStream(raw, prk, isPhone: false), address, PhoneLabel.Shown(name), JustPaired: false);
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
    /// Waits for whichever answer to the code question comes first: the digits, a cancel, the timer, or the link
    /// ending. The answer is claimed once, so a click landing with the timer is either taken or ignored, never both.
    /// </summary>
    private static (OwnerAnswer Answer, string Typed) AwaitOwner(TaskCompletionSource<string?> answer, TimeSpan wait, CancellationToken ended)
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
    /// The phone's confirmation, read as <see cref="ReadWithin"/> does over the owner's wait, which is longer than
    /// the listener's per-message read timeout on the socket: that is lifted for this read, so only
    /// <paramref name="wait"/> bounds it, and put back after.
    /// </summary>
    private static byte[]? ReadOwnerLength(Stream raw, TimeSpan wait, Action<string> log, Action giveUp)
    {
        if (!raw.CanTimeout)
        {
            return ReadWithin(raw, wait, "confirmation", log, giveUp);
        }

        var bounded = raw.ReadTimeout;
        raw.ReadTimeout = Timeout.Infinite;
        try
        {
            return ReadWithin(raw, wait, "confirmation", log, giveUp);
        }
        finally
        {
            try
            {
                raw.ReadTimeout = bounded;
            }
            catch (ObjectDisposedException)
            {
                // Given up on and closed: nothing left to bound.
            }
        }
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
            try
            {
                body = LinkWire.Read(raw);
            }
            catch (Exception e) when ((e is IOException or ObjectDisposedException) && Volatile.Read(ref expired) != 0)
            {
                // giveUp closed the link under the read: the wait ran out, which is a null, not a failure to report.
                return null;
            }
        }

        return Interlocked.Exchange(ref expired, 1) != 0 ? null : body;
    }
}
