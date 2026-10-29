using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Threading.Channels;
using Edgepad.Controls;
using Edgepad.Net;
using Edgepad.Protocol;
using Edgepad.Trust;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// EDGL1 end to end: <see cref="LinkInitiator"/> as the phone on the test's thread, <see cref="LinkResponder"/> as the
/// laptop on another, joined by an in-memory <see cref="Duplex"/>. The owner at the laptop is <see cref="Owner"/>: it
/// types whatever the test says, given the code the phone showed, or cancels, or never answers.
/// </summary>
public sealed class LinkResponderTests : IDisposable
{
    private const string PhoneName = "Pixel";

    // a0a1a2...: a letter after every digit, so no six-digit code can turn up inside a logged address by chance.
    private static readonly byte[] PhoneId = [.. Enumerable.Range(0xA0, 16).Select(i => (byte)i)];
    private static readonly byte[] LaptopId = [.. Enumerable.Range(0x80, 16).Select(i => (byte)i)];
    private static readonly string Address = "link:" + Convert.ToHexStringLower(PhoneId);

    // Long enough for a busy CI runner, short enough that a test waiting on one is still quick.
    private static readonly TimeSpan MessageWait = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan NoAnswerWait = TimeSpan.FromMilliseconds(300);

    private readonly string dir = Path.Combine(Path.GetTempPath(), "edgepad-tests-" + Guid.NewGuid().ToString("N"));
    private readonly LinkIdentity laptop = new(LaptopId, "Laptop");
    private readonly PairedLinks paired;
    private readonly TrustStore trust;
    private readonly PairingLimiter limiter;
    private readonly Owner owner = new();
    private readonly ConcurrentQueue<string> logs = new();
    private readonly ConcurrentQueue<string> codesShown = new();
    private readonly CancellationTokenSource ended = new();

    // Milliseconds on the limiter's clock, moved by hand.
    private long time;

    public LinkResponderTests()
    {
        paired = new PairedLinks(Path.Combine(dir, "paired-links.txt"));
        trust = new TrustStore(Path.Combine(dir, "trusted-phone.txt"));
        limiter = new PairingLimiter(() => time);
    }

    /// <summary>The laptop, accepting on <paramref name="end"/>; when it gives no link it closes the end, as the listener will.</summary>
    private Task<LinkResult?> Laptop(Stream end, TimeSpan? ownerWait = null) => Task.Run(
        () =>
        {
            LinkResult? result = null;
            try
            {
                result = LinkResponder.Accept(end, laptop, paired, limiter, owner, trust, logs.Enqueue, end.Dispose, ended.Token, MessageWait, ownerWait ?? MessageWait);
                return result;
            }
            finally
            {
                if (result is null)
                {
                    end.Dispose();
                }
            }
        },
        TestContext.Current.CancellationToken);

    private void ShowOnPhone(string code)
    {
        codesShown.Enqueue(code);
        owner.PhoneShows(code);
    }

    private static string NotThe(string code) => code == "000000" ? "000001" : "000000";

    /// <summary>A whole pairing, the owner typing the phone's code; returns the phone's side.</summary>
    private async Task<(Initiated Phone, LinkResult Laptop)> PairAsync()
    {
        owner.Types = code => code;
        var (phoneEnd, laptopEnd) = Duplex.Create();
        var laptopSide = Laptop(laptopEnd);
        var phone = LinkInitiator.Pair(phoneEnd, PhoneId, PhoneName, ShowOnPhone);
        var result = await laptopSide;
        Assert.NotNull(result);
        return (phone, result);
    }

    private static void AssertTheyTalk(Stream phone, Stream laptop)
    {
        byte[] sent = [7, 8, 9];
        phone.Write(sent);
        phone.Flush();
        var got = new byte[3];
        laptop.ReadExactly(got);
        Assert.Equal(sent, got);

        laptop.WriteByte(42);
        Assert.Equal(42, phone.ReadByte());
    }

    private void AssertNotPaired()
    {
        Assert.False(paired.TryGet(Convert.ToHexStringLower(PhoneId), out _, out _));
        Assert.Empty(trust.TrustedPhones);
    }

    [Fact]
    public async Task InfoSaysWhoTheLaptopIsAndCloses()
    {
        var (phoneEnd, laptopEnd) = Duplex.Create();
        var laptopSide = Laptop(laptopEnd);

        var info = LinkInitiator.Info(phoneEnd);

        Assert.Null(await laptopSide);
        Assert.Equal(LaptopId, info.LaptopId);
        Assert.Equal(ProtocolConstants.Version, info.ProtocolVersion);
        Assert.Equal("Laptop", info.Name);
        Assert.Equal(0, owner.Shown);
    }

    [Fact]
    public async Task AnotherLinkVersionIsToldSoAndClosed()
    {
        var (phoneEnd, laptopEnd) = Duplex.Create();
        var laptopSide = Laptop(laptopEnd);

        LinkWire.Write(phoneEnd, [.. "EDGL"u8, 2, LinkWire.ModePair]);

        Assert.Equal(LinkWire.Status(LinkWire.StatusUnsupported), LinkWire.Read(phoneEnd));
        Assert.Null(await laptopSide);
    }

    [Fact]
    public async Task ThePhonesCodeTypedOnTheLaptopPairsAndTrustsIt()
    {
        var (phone, result) = await PairAsync();

        Assert.NotNull(phone.Secure);
        Assert.True(result.JustPaired);
        Assert.Equal(Address, result.Address);
        Assert.Equal(PhoneName, result.PhoneName);
        Assert.Equal(PhoneName, owner.LastPhoneName);
        Assert.Equal(1, owner.Withdrawn);
        AssertTheyTalk(phone.Secure, result.Secure);

        Assert.True(paired.TryGet(Convert.ToHexStringLower(PhoneId), out var key, out _));
        Assert.Equal(phone.PairingKey, key);
        Assert.True(trust.Admit(Address, () => throw new InvalidOperationException("a paired phone was asked about")));
    }

    [Fact]
    public async Task AWrongCodeIsRefusedCountedAndNeverConfirmed()
    {
        owner.Types = NotThe;
        var (phoneEnd, laptopEnd) = Duplex.Create();
        var laptopSide = Laptop(laptopEnd);

        var phone = LinkInitiator.Pair(phoneEnd, PhoneId, PhoneName, ShowOnPhone);

        Assert.Null(await laptopSide);
        Assert.Null(phone.Secure);
        AssertNotPaired();
        Assert.Contains(logs, line => line.Contains("not the one that phone shows", StringComparison.Ordinal));

        // It counted: four more and pairing is locked.
        for (var i = 0; i < 4; i++)
        {
            (phoneEnd, laptopEnd) = Duplex.Create();
            laptopSide = Laptop(laptopEnd);
            LinkInitiator.Pair(phoneEnd, PhoneId, PhoneName, ShowOnPhone);
            await laptopSide;
        }

        (phoneEnd, laptopEnd) = Duplex.Create();
        laptopSide = Laptop(laptopEnd);
        Assert.Equal(LinkWire.StatusBusy, LinkInitiator.Pair(phoneEnd, PhoneId, PhoneName, ShowOnPhone).Status);
        Assert.Null(await laptopSide);

        // And unlocks on the clock.
        time += 300_000;
        owner.Types = code => code;
        (phoneEnd, laptopEnd) = Duplex.Create();
        laptopSide = Laptop(laptopEnd);
        Assert.NotNull(LinkInitiator.Pair(phoneEnd, PhoneId, PhoneName, ShowOnPhone).Secure);
        Assert.NotNull(await laptopSide);
    }

    [Fact]
    public async Task APhoneThatRunsTheWholeExchangeWithoutTheOwnerTypingIsNeverPaired()
    {
        // The rogue on the same network: it works out its own code, as anyone on the connection can, and goes
        // straight on to confirm it. The laptop's owner never types anything, so the laptop never reads that
        // confirmation, never sends its own, and never trusts it.
        owner.Types = null;
        var (phoneEnd, laptopEnd) = Duplex.Create();
        var laptopSide = Laptop(laptopEnd, NoAnswerWait);

        using var own = LinkCrypto.NewKeyPair();
        var nonce = RandomNumberGenerator.GetBytes(LinkWire.NonceLength);
        LinkWire.Write(phoneEnd, LinkWire.EncodePairRequest(PhoneId, "Rogue", LinkCrypto.Commit(own.PublicKey, nonce)));
        var offer = LinkWire.DecodeReply(LinkWire.ModePair, LinkWire.Read(phoneEnd));
        LinkWire.Write(phoneEnd, LinkWire.Reveal(own.PublicKey, nonce));
        var transcript = LinkCrypto.PairTranscript(PhoneId, offer.LaptopId, LinkWire.NameField("Rogue"), offer.NameField, own.PublicKey, nonce, offer.PublicKey, offer.Nonce);
        var prk = LinkCrypto.PairPrk(transcript, LinkCrypto.Agree(own, offer.PublicKey));
        LinkWire.Write(phoneEnd, LinkWire.Tag(LinkCrypto.PhoneTag(prk, transcript, pairing: true)));

        Assert.Null(await laptopSide);
        Assert.Equal(1, owner.Shown);
        AssertNotPaired();
        Assert.Contains(logs, line => line.Contains("no code was typed", StringComparison.Ordinal));

        // Nothing came back that depends on the code: the laptop closed without a confirmation.
        Assert.Throws<EndOfStreamException>(() => LinkWire.Read(phoneEnd));
    }

    [Fact]
    public async Task ANameChangedOnTheWayIsNotPairedOnTheOwnersCode()
    {
        // A relay renames the phone, so the laptop asks about "Owner's phone" while the phone in the owner's hand
        // says "Pixel". The names are in the transcript as sent, so the code the phone shows is not the laptop's.
        owner.Types = code => code;
        var (phoneEnd, laptopEnd) = Duplex.Create();
        var laptopSide = Laptop(laptopEnd);

        using var own = LinkCrypto.NewKeyPair();
        var nonce = RandomNumberGenerator.GetBytes(LinkWire.NonceLength);
        LinkWire.Write(phoneEnd, LinkWire.EncodePairRequest(PhoneId, "Owner's phone", LinkCrypto.Commit(own.PublicKey, nonce)));
        var offer = LinkWire.DecodeReply(LinkWire.ModePair, LinkWire.Read(phoneEnd));
        LinkWire.Write(phoneEnd, LinkWire.Reveal(own.PublicKey, nonce));
        ShowOnPhone(LinkCrypto.Sas(LinkCrypto.PairTranscript(
            PhoneId, offer.LaptopId, LinkWire.NameField(PhoneName), offer.NameField, own.PublicKey, nonce, offer.PublicKey, offer.Nonce)));

        Assert.Null(await laptopSide);
        Assert.Equal("Owner's phone", owner.LastPhoneName);
        AssertNotPaired();
        Assert.Contains(logs, line => line.Contains("not the one that phone shows", StringComparison.Ordinal));
        Assert.Throws<EndOfStreamException>(() => LinkWire.Read(phoneEnd));
    }

    [Fact]
    public async Task AnHonestPhoneTheOwnerDoesNotAnswerIsNotPairedEither()
    {
        owner.Types = null;
        var (phoneEnd, laptopEnd) = Duplex.Create();
        var laptopSide = Laptop(laptopEnd, NoAnswerWait);

        var phone = LinkInitiator.Pair(phoneEnd, PhoneId, PhoneName, ShowOnPhone);

        Assert.Null(await laptopSide);
        Assert.Null(phone.Secure);
        AssertNotPaired();
    }

    [Fact]
    public async Task TheOwnerCancellingRefusesWithoutCounting()
    {
        owner.Cancels = true;
        for (var i = 0; i < 6; i++)
        {
            var (phoneEnd, laptopEnd) = Duplex.Create();
            var laptopSide = Laptop(laptopEnd);
            var phone = LinkInitiator.Pair(phoneEnd, PhoneId, PhoneName, ShowOnPhone);
            Assert.Equal(LinkWire.StatusOk, phone.Status);
            Assert.Null(phone.Secure);
            Assert.Null(await laptopSide);
        }

        AssertNotPaired();
        Assert.Equal(6, owner.Shown);
    }

    [Fact]
    public async Task AKeyThatDoesNotOpenTheCommitmentIsRefusedBeforeTheOwnerIsAsked()
    {
        owner.Types = code => code;
        var (phoneEnd, laptopEnd) = Duplex.Create();
        var laptopSide = Laptop(laptopEnd);

        using var own = LinkCrypto.NewKeyPair();
        var committed = new byte[LinkWire.NonceLength];
        var revealed = new byte[LinkWire.NonceLength];
        revealed[0] = 1;
        LinkWire.Write(phoneEnd, LinkWire.EncodePairRequest(PhoneId, PhoneName, LinkCrypto.Commit(own.PublicKey, committed)));
        LinkWire.Read(phoneEnd);
        LinkWire.Write(phoneEnd, LinkWire.Reveal(own.PublicKey, revealed));

        Assert.Null(await laptopSide);
        Assert.Equal(0, owner.Shown);
        AssertNotPaired();
        Assert.Contains(logs, line => line.Contains("not the one it committed to", StringComparison.Ordinal));
    }

    [Fact]
    public async Task APairedPhoneResumesWithoutTheOwner()
    {
        var (first, _) = await PairAsync();
        var shownBefore = owner.Shown;

        var (phoneEnd, laptopEnd) = Duplex.Create();
        var laptopSide = Laptop(laptopEnd);
        var phone = LinkInitiator.Resume(phoneEnd, PhoneId, LaptopId, first.PairingKey);
        var result = await laptopSide;

        Assert.NotNull(phone.Secure);
        Assert.NotNull(result);
        Assert.False(result.JustPaired);
        Assert.Equal(Address, result.Address);
        Assert.Equal(PhoneName, result.PhoneName);
        Assert.Equal(shownBefore, owner.Shown);
        AssertTheyTalk(phone.Secure, result.Secure);
    }

    [Fact]
    public async Task APhoneNotPairedIsToldToPairAgain()
    {
        var (phoneEnd, laptopEnd) = Duplex.Create();
        var laptopSide = Laptop(laptopEnd);

        var phone = LinkInitiator.Resume(phoneEnd, PhoneId, LaptopId, new byte[LinkCrypto.KeyLength]);

        Assert.Equal(LinkWire.StatusUnknownPhone, phone.Status);
        Assert.Null(await laptopSide);
    }

    [Fact]
    public async Task AResumeWithTheWrongKeyFailsBothConfirmations()
    {
        var (first, _) = await PairAsync();
        byte[] wrong = [.. first.PairingKey];
        wrong[0] ^= 1;

        var (phoneEnd, laptopEnd) = Duplex.Create();
        var laptopSide = Laptop(laptopEnd);
        var phone = LinkInitiator.Resume(phoneEnd, PhoneId, LaptopId, wrong, confirmAnyway: true);

        Assert.Null(phone.Secure);
        Assert.Null(await laptopSide);
        Assert.Contains(logs, line => line.Contains("does not hold the key", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AResumeWithAPointOffTheCurveIsRefused()
    {
        await PairAsync();

        var (phoneEnd, laptopEnd) = Duplex.Create();
        var laptopSide = Laptop(laptopEnd);
        LinkWire.Write(phoneEnd, LinkWire.EncodeResumeRequest(PhoneId, [0x04, .. new byte[64]], new byte[LinkWire.NonceLength]));

        Assert.Null(await laptopSide);
        Assert.Contains(logs, line => line.Contains("not a valid P-256 point", StringComparison.Ordinal));
        Assert.Throws<EndOfStreamException>(() => LinkWire.Read(phoneEnd));
    }

    [Fact]
    public async Task NoCodeOrKeyIsEverLogged()
    {
        var (first, _) = await PairAsync();
        owner.Types = NotThe;
        var (phoneEnd, laptopEnd) = Duplex.Create();
        var laptopSide = Laptop(laptopEnd);
        LinkInitiator.Pair(phoneEnd, PhoneId, PhoneName, ShowOnPhone);
        await laptopSide;

        Assert.NotEmpty(logs);
        Assert.Equal(2, codesShown.Count);
        string[] secrets =
        [
            .. codesShown,
            .. codesShown.Select(NotThe),
            Convert.ToHexString(first.PairingKey),
            Convert.ToHexStringLower(first.PairingKey),
            Convert.ToBase64String(first.PairingKey),
        ];
        Assert.DoesNotContain(logs, line => secrets.Any(secret => line.Contains(secret, StringComparison.Ordinal)));
    }

    public void Dispose()
    {
        ended.Dispose();
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// The owner at the laptop. Shown the window, it waits for the code the phone shows and types what
    /// <see cref="Types"/> makes of it; with <see cref="Cancels"/> it cancels at once; with neither it never answers.
    /// </summary>
    private sealed class Owner : IPairingPrompt
    {
        private readonly Channel<string> shownOnPhone = Channel.CreateUnbounded<string>();
        private int shown;
        private int withdrawn;

        public Func<string, string>? Types { get; set; }

        public bool Cancels { get; set; }

        public string? LastPhoneName { get; private set; }

        public int Shown => Volatile.Read(ref shown);

        public int Withdrawn => Volatile.Read(ref withdrawn);

        public void PhoneShows(string code) => _ = shownOnPhone.Writer.TryWrite(code);

        public IDisposable Show(string phoneName, Action<string> onTyped, Action onOwnerCancelled)
        {
            Interlocked.Increment(ref shown);
            LastPhoneName = phoneName;
            if (Cancels)
            {
                onOwnerCancelled();
            }
            else if (Types is { } types)
            {
                // Off the laptop's thread, as a window's click would be: the phone shows its code only after this returns.
                var token = TestContext.Current.CancellationToken;
                _ = Task.Run(async () => onTyped(types(await shownOnPhone.Reader.ReadAsync(token))), token);
            }

            return new Subscription(() => Interlocked.Increment(ref withdrawn));
        }
    }
}
