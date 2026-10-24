using Edgepad.Protocol;
using Edgepad.Trust;

namespace Edgepad.Net;

/// <summary>
/// Everything between a phone's first byte and HELLO_ACK, in the one order that keeps it safe: the version, the
/// locked-out check, the owner's trust (asked through askTrust about a phone not trusted yet), the PIN at levels 2
/// and 3, the server's word that this phone may take over (onAdmitted), and only then HELLO_ACK. It blocks on the
/// session's read thread, and ends a silent wait through <c>giveUp</c>, which must close the link so the blocked
/// read ends.
///
/// True means HELLO_ACK went out and the session is live. False, or any exception, means the caller closes the
/// link: nothing that comes after HELLO_ACK is reachable without it.
/// </summary>
internal static class Handshake
{
    /// <summary>
    /// How long an open socket may go without a HELLO. The read has no timeout of its own, so a paired device
    /// that connects and says nothing held this thread, and its socket, for as long as the link stayed up.
    /// </summary>
    public static readonly TimeSpan HelloTimeout = TimeSpan.FromSeconds(10);

    public static bool Run(
        FrameStream frames,
        string address,
        TrustStore trust,
        PinStore pin,
        Func<string, bool> askTrust,
        Func<bool> onAdmitted,
        Action<string> onStatus,
        Action<string> log,
        Action giveUp,
        TimeSpan? helloTimeout = null,
        TimeSpan? pinTimeout = null)
    {
        var helloWait = helloTimeout ?? HelloTimeout;
        Frame first;
        using (new System.Threading.Timer(
            _ =>
            {
                log($"Refused {address}: no HELLO within {(int)helloWait.TotalSeconds} s");

                // Closing the socket is what ends the blocked read; the read loop then finishes the session as usual.
                giveUp();
            },
            null,
            helloWait,
            Timeout.InfiniteTimeSpan))
        {
            first = frames.ReadFrame();
        }

        if (first is not Hello hello)
        {
            log($"Refused {address}: no valid HELLO");
            return false;
        }

        if (hello.Version != ProtocolConstants.Version)
        {
            // Answered with this laptop's version, so the phone can say which side needs updating.
            frames.Send(new HelloAck(ProtocolConstants.Version));
            log($"Refused {address}: it speaks protocol {hello.Version}, this laptop {ProtocolConstants.Version}");
            onStatus($"Refused a phone on protocol {hello.Version}; this laptop is on {ProtocolConstants.Version}");
            return false;
        }

        // A phone locked out by wrong PINs is told so and closed before the owner is asked anything, so
        // guessing cannot also be a way to keep the trust question on the owner's screen.
        if (PinGate.LockedOut(address, pin, frames.Send, log))
        {
            onStatus("Refused a phone locked out after wrong PINs");
            return false;
        }

        // HELLO_ACK waits on the owner's answer, and a refusal closes the socket without one: the same
        // bytes a phone it did not trust has always been sent, so no phone needs to learn anything new.
        if (!trust.Admit(address, () => askTrust(address)))
        {
            var trusted = string.Join(", ", trust.TrustedPhones);
            log($"Refused {address}: this laptop trusts {(trusted.Length > 0 ? trusted : "no phone yet")}");
            onStatus("Refused a phone it does not trust");
            return false;
        }

        // The PIN at levels 2 and 3, and still before onAdmitted: a phone that has not given it must never be
        // able to end the connected phone's session. Level 1 sends nothing here, as before there was a PIN.
        if (!PinGate.Run(address, pin, frames.ReadFrame, frames.Send, log, giveUp, pinTimeout))
        {
            onStatus("Refused a phone that did not give the right PIN");
            return false;
        }

        // Only now may this phone take over from one already connected: before trust said yes, any bonded
        // device that merely connected could end the owner's session at will.
        if (!onAdmitted())
        {
            log($"Refused {address}: trusted, but not taken now: phones are being forgotten, Edgepad is quitting, or another phone just took over");
            return false;
        }

        frames.Send(new HelloAck(ProtocolConstants.Version));
        log($"Connected {address}");
        onStatus($"Connected to {address}");
        return true;
    }
}
