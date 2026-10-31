using Edgepad.Net;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// The hand-off rules, on a clock moved by hand: one connected phone, candidates that touch it only once admitted,
/// and 30 s of refusal for the phone another one took over from.
/// </summary>
public sealed class AdmissionTests
{
    private const string PhoneA = "AA:AA:AA:AA:AA:AA";
    private const string PhoneB = "BB:BB:BB:BB:BB:BB";
    private const string PhoneC = "CC:CC:CC:CC:CC:CC";
    private const long Quiet = 30_000;

    private readonly Admission<Session> admission;
    private long time = 1_000;

    public AdmissionTests() => admission = new Admission<Session>(() => time);

    [Fact]
    public void IsRefusingWhileHeldOffOrQuiet()
    {
        Assert.False(admission.IsRefusing);
        _ = admission.HoldOff();
        Assert.True(admission.IsRefusing);
        admission.Resume();
        Assert.False(admission.IsRefusing);

        _ = admission.HangUp(TimeSpan.FromSeconds(5));
        Assert.True(admission.IsRefusing);
        time += 5_000;
        Assert.False(admission.IsRefusing);
    }

    [Fact]
    public void ACandidateLeavesTheConnectedPhoneAloneUntilAdmitted()
    {
        var a = Connect(PhoneA);
        var b = new Session();
        Assert.Null(admission.Refusal(PhoneB));
        Assert.True(admission.AddCandidate(b, PhoneB));
        Assert.Equal(PhoneA, admission.CurrentAddress);

        Assert.True(admission.Admit(b, out var replaced, out _));
        Assert.Same(a, replaced);
        Assert.Equal(PhoneB, admission.CurrentAddress);
    }

    [Fact]
    public void TheDisplacedPhoneIsRefusedFor30Seconds()
    {
        Connect(PhoneA);
        Connect(PhoneB);

        Assert.NotNull(admission.Refusal(PhoneA));
        time += Quiet - 1;
        Assert.NotNull(admission.Refusal(PhoneA));
        time += 1;
        Assert.Null(admission.Refusal(PhoneA));
    }

    [Fact]
    public void TheRefusalIgnoresCase()
    {
        Connect(PhoneA);
        Connect(PhoneB);

        Assert.NotNull(admission.Refusal(PhoneA.ToLowerInvariant()));
    }

    [Fact]
    public void TheSamePhoneReconnectingIsNotAHandOff()
    {
        var first = Connect(PhoneA);
        var second = new Session();
        admission.AddCandidate(second, PhoneA);

        Assert.True(admission.Admit(second, out var replaced, out _));
        Assert.Same(first, replaced);
        Assert.Null(admission.Refusal(PhoneA));
    }

    [Fact]
    public void AConnectionTheDisplacedPhoneOpenedBeforeIsNotAdmitted()
    {
        Connect(PhoneA);
        var late = new Session();
        Assert.True(admission.AddCandidate(late, PhoneA));
        Connect(PhoneB);

        Assert.False(admission.Admit(late, out var replaced, out var refusal));
        Assert.Null(replaced);
        Assert.Contains("took over", refusal, StringComparison.Ordinal);
        Assert.Equal(PhoneB, admission.CurrentAddress);
    }

    [Fact]
    public void EveryDisplacedPhoneIsRememberedNotOnlyTheLast()
    {
        Connect(PhoneA);
        Connect(PhoneB);
        Connect(PhoneC);

        Assert.NotNull(admission.Refusal(PhoneA));
        Assert.NotNull(admission.Refusal(PhoneB));
    }

    [Fact]
    public void ACandidateThatEndedIsNotAdmitted()
    {
        var a = new Session();
        admission.AddCandidate(a, PhoneA);
        Assert.False(admission.Ended(a));

        Assert.False(admission.Admit(a, out _, out _));
        Assert.Null(admission.CurrentAddress);
    }

    [Fact]
    public void OnlyTheConnectedPhoneEndingClearsIt()
    {
        var a = Connect(PhoneA);
        var stale = new Session();
        admission.AddCandidate(stale, PhoneB);

        Assert.False(admission.Ended(stale));
        Assert.Equal(PhoneA, admission.CurrentAddress);
        Assert.True(admission.Ended(a));
        Assert.Null(admission.CurrentAddress);
    }

    [Fact]
    public void SessionsFindsTheConnectedPhoneAndItsCandidates()
    {
        var a = Connect(PhoneA);
        var again = new Session();
        var other = new Session();
        admission.AddCandidate(again, PhoneA);
        admission.AddCandidate(other, PhoneB);

        var found = admission.Sessions(PhoneA);
        Assert.Equal(2, found.Count);
        Assert.Contains(a, found);
        Assert.Contains(again, found);
    }

    [Fact]
    public void CloseHandsBackEveryoneAndAdmitsNoOne()
    {
        var a = Connect(PhoneA);
        var b = new Session();
        admission.AddCandidate(b, PhoneB);

        var all = admission.Close();
        Assert.Equal(2, all.Count);
        Assert.Contains(a, all);
        Assert.Contains(b, all);
        Assert.False(admission.Admit(b, out _, out _));
        Assert.False(admission.AddCandidate(new Session(), PhoneC));
        Assert.NotNull(admission.Refusal(PhoneC));
    }

    [Fact]
    public void HoldOffHandsBackEveryoneAndRefusesUntilTheLastResume()
    {
        var a = Connect(PhoneA);
        var b = new Session();
        admission.AddCandidate(b, PhoneB);

        var ending = admission.HoldOff();
        Assert.Equal(2, ending.Count);
        Assert.Contains(a, ending);
        Assert.Contains(b, ending);
        Assert.False(admission.Admit(b, out _, out _));

        admission.HoldOff();
        Assert.NotNull(admission.Refusal(PhoneC));
        admission.Resume();
        Assert.NotNull(admission.Refusal(PhoneC));
        admission.Resume();
        Assert.Null(admission.Refusal(PhoneC));
    }

    [Fact]
    public void ExtraResumesNeverGoBelowZero()
    {
        admission.Resume();
        admission.HoldOff();
        Assert.NotNull(admission.Refusal(PhoneA));
        admission.Resume();
        Assert.Null(admission.Refusal(PhoneA));
    }

    [Fact]
    public void HangUpRefusesForTheQuietPeriodAndNeverShortensIt()
    {
        Connect(PhoneA);
        admission.HangUp(TimeSpan.FromSeconds(30));
        admission.HangUp(TimeSpan.FromSeconds(1));

        time += Quiet - 1;
        Assert.NotNull(admission.Refusal(PhoneB));
        time += 1;
        Assert.Null(admission.Refusal(PhoneB));
    }

    [Fact]
    public void ResumeDoesNotEndAQuietPeriod()
    {
        admission.HoldOff();
        admission.HangUp(TimeSpan.FromSeconds(30));
        admission.Resume();

        Assert.NotNull(admission.Refusal(PhoneB));
    }

    [Fact]
    public void AConnectionAcceptedJustBeforeAHoldIsNotAddedDuringIt()
    {
        Assert.Null(admission.Refusal(PhoneB));
        admission.HoldOff();

        Assert.False(admission.AddCandidate(new Session(), PhoneB));
        admission.Resume();
        Assert.True(admission.AddCandidate(new Session(), PhoneB));
    }

    private Session Connect(string address)
    {
        var session = new Session();
        Assert.True(admission.AddCandidate(session, address));
        Assert.True(admission.Admit(session, out _, out _));
        return session;
    }

    /// <summary>A stand-in session: the table only ever compares them by reference.</summary>
    private sealed class Session;
}
