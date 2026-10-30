using System.Collections.Concurrent;
using Edgepad.Bluetooth;
using Edgepad.Dispatch;
using Edgepad.Net;
using Edgepad.Trust;

namespace Edgepad.Platform;

/// <summary>
/// Serves one phone at a time over whatever link it arrived on, with the Windows hand-off rules
/// (<see cref="Admission{T}"/>). <paramref name="onEnded"/> is told after each session has ended, so the caller can
/// say what is true now.
/// </summary>
internal sealed class SessionHost(
    TrustStore trust,
    PinStore pin,
    Func<string, CancellationToken, bool> askTrust,
    LinkIdentity identity,
    Action<string> onStatus,
    Action onEnded) : IDisposable
{
    // Joined at exit so each session's own cleanup, which releases held keys and buttons, gets to run.
    private static readonly TimeSpan ExitGrace = TimeSpan.FromSeconds(2);

    private readonly Admission<LinuxSession> admission = new();

    /// <summary>The connected phone's address, or null.</summary>
    public string? CurrentAddress => admission.CurrentAddress;

    /// <summary>Whether a hold or a quiet period refuses new connections right now; no pairing may start then.</summary>
    public bool IsRefusing => admission.IsRefusing;

    // How the owner reads each Wi-Fi or USB phone's address. Only phones a pairing or a resume let through get here,
    // so it grows with this laptop's own phones, not with whoever opens a socket.
    private readonly ConcurrentDictionary<string, string> shown = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// What the owner reads for <paramref name="address"/>: a Wi-Fi or USB phone's name and the way it came, since its
    /// address is a string of hex; a Bluetooth phone's address as it is.
    /// </summary>
    public string Shown(string address) => shown.TryGetValue(address, out var name) ? name : address;

    /// <summary>The status line for <paramref name="link"/>'s session, naming the phone as <see cref="Shown"/> does.</summary>
    private Action<string> StatusFor(IPhoneLink link)
    {
        if (link.Kind != LinkKind.Network)
        {
            return onStatus;
        }

        var address = link.Address;
        var name = $"{link.Label} · over Wi-Fi or USB";
        shown[address] = name;
        return status => onStatus(status.Replace(address, name, StringComparison.Ordinal));
    }

    /// <summary>
    /// Must not block: the session runs on a thread of its own. True when the link was taken; false when it was
    /// refused, and the link is then already released.
    /// </summary>
    public bool Accept(IPhoneLink link)
    {
        var address = link.Address;

        // A device this laptop already knows it will refuse is turned away before it costs a thread. Nothing that
        // gets past this touches the connected phone yet: that waits for the handshake's onAdmitted.
        var refusal = admission.Refusal(address);
        if (refusal is null && trust.Refuses(address))
        {
            refusal = $"Refused {address} without disturbing the connected phone: the owner is being asked about a phone "
                + "or just declined this one, or the trust file cannot be read";
        }

        if (refusal is not null)
        {
            Log.Write(refusal);
            link.Release();
            return false;
        }

        // Input state belongs to one connection: its own virtual keyboard and pad, destroyed when the session ends.
        // Until the session starts, nothing else disposes the platform: a session that never runs never reaches
        // the finally that releases its input, so its uinput keyboard and worker thread are this method's to end.
        var platform = new LinuxPlatform();
        LinuxSession session;
        try
        {
            session = new LinuxSession(
                link,
                trust,
                pin,
                askTrust,
                new Dispatcher(platform, platform),
                platform.Dispose,
                platform.ProbePad,
                platform.StartReporting,
                () => platform.Macros,
                identity,
                StatusFor(link),
                OnAdmitted,
                OnSessionEnded);
        }
        catch
        {
            platform.Dispose();
            throw;
        }

        if (!admission.AddCandidate(session, address))
        {
            Log.Write($"Refused {address}: Edgepad is quitting, or the owner is changing security settings");
            session.Dispose();
            platform.Dispose();
            return false;
        }

        session.Start();
        return true;
    }

    /// <summary>Ends every session of the phone at <paramref name="address"/>, the connected one and its candidates.</summary>
    public void Disconnect(string address) => End(admission.Sessions(address));

    /// <summary>
    /// Called by a session once trust and the PIN have said yes, just before it answers HELLO_ACK: the moment a
    /// new phone may replace the connected one.
    /// </summary>
    private bool OnAdmitted(LinuxSession session)
    {
        if (!admission.Admit(session, out var replaced, out var refusal))
        {
            Log.Write($"Refused {session.Address}: {refusal}");
            return false;
        }

        // Outside the admission lock: the replaced session's exit calls OnSessionEnded.
        if (replaced is not null)
        {
            Log.Write($"{session.Address} replaced the connection with {replaced.Address}");
            replaced.Dispose();
        }

        return true;
    }

    /// <summary>
    /// Hangs up on every phone and refuses new connections until <see cref="Resume"/>. For an owner dialog that
    /// changes security: a connected phone is a keyboard, and a trusted one reconnects within seconds.
    /// </summary>
    public void HoldOff() => End(admission.HoldOff());

    /// <summary>Ends one <see cref="HoldOff"/>.</summary>
    public void Resume() => admission.Resume();

    /// <summary>Hangs up on every phone and refuses new connections for <paramref name="quiet"/>.</summary>
    public void HangUp(TimeSpan quiet) => End(admission.HangUp(quiet));

    // Outside the admission lock: each session's exit calls OnSessionEnded.
    private static void End(List<LinuxSession> sessions)
    {
        foreach (var session in sessions)
        {
            session.Dispose();
        }
    }

    private void OnSessionEnded(LinuxSession session)
    {
        admission.Ended(session);
        onEnded();
    }

    public void Dispose()
    {
        // Outside any lock, because each session's exit calls OnSessionEnded. Held keys and buttons are released
        // in the session's own finally on its thread, and the process can exit before that runs.
        var ending = admission.Close();
        End(ending);
        foreach (var session in ending)
        {
            session.Join(ExitGrace);
        }
    }
}
