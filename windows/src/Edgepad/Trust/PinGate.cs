using Edgepad.Protocol;

namespace Edgepad.Trust;

/// <summary>
/// The PIN question between the owner's trust and HELLO_ACK, at security levels 2 and 3. It knows nothing of
/// sockets: it reads and sends whole frames through delegates and ends a silent wait through <c>giveUp</c>, which
/// is what lets the same code serve any laptop, and lets a test drive it frame by frame.
///
/// Nothing here writes a digit anywhere. The answer travels as a <see cref="Pin"/>, which prints as stars, and every
/// log line names only the phone.
/// </summary>
internal static class PinGate
{
    /// <summary>How long a phone has to answer each PIN_REQUEST: the same minute the owner gets for the trust question.</summary>
    public static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Sends the lock-out reply and returns true when <paramref name="address"/> is locked out, before the owner is
    /// asked anything: a phone that has just used up its tries must not get to put the trust question on the screen
    /// again. Only where a PIN is required, so level 1 never sends a PIN frame, even with failures still counted
    /// from before the level was lowered.
    /// </summary>
    public static bool LockedOut(string address, PinStore pin, Action<Frame> send, Action<string> log)
    {
        if (!pin.Required(address) || pin.LockedFor(address) is not { } wait)
        {
            return false;
        }

        var seconds = Seconds(wait);
        send(new PinRequest(0, seconds));
        log($"Refused {address}: locked out after wrong PINs for {seconds} s more");
        return true;
    }

    /// <summary>
    /// Asks <paramref name="address"/> for the PIN when its level requires it, until it is right, the phone is locked
    /// out, or the answer cannot be judged. True when the phone may go on to HELLO_ACK; false when the caller must
    /// close the link without one. A frame other than PIN_ANSWER while waiting throws
    /// <see cref="InvalidDataException"/>, a protocol error like any other.
    /// </summary>
    public static bool Run(
        string address,
        PinStore pin,
        Func<Frame> read,
        Action<Frame> send,
        Action<string> log,
        Action giveUp,
        TimeSpan? timeout = null)
    {
        if (!pin.Required(address))
        {
            return true;
        }

        // Checked again here, not only before the trust question: another connection from the same address may
        // have used up the tries while the owner was answering.
        if (LockedOut(address, pin, send, log))
        {
            return false;
        }

        var wait = timeout ?? AnswerTimeout;
        send(new PinRequest((byte)pin.AttemptsLeft(address), 0));
        while (true)
        {
            Frame frame;

            // The read has no timeout of its own, so a phone that never answered would hold this thread and its
            // socket for as long as the link stayed up. Armed for each question, so a phone gets a full minute for
            // every try rather than a minute for all five.
            using (new System.Threading.Timer(
                _ =>
                {
                    log($"Refused {address}: no PIN within {(int)wait.TotalSeconds} s");
                    giveUp();
                },
                null,
                wait,
                Timeout.InfiniteTimeSpan))
            {
                frame = read();
            }

            if (frame is not PinAnswer answer)
            {
                throw new InvalidDataException($"Expected PIN_ANSWER, got {frame.GetType().Name}");
            }

            switch (pin.Check(address, answer.Digits))
            {
                case PinResult.Accepted:
                    log($"PIN accepted for {address}");
                    return true;
                case PinResult.Wrong wrong:
                    log($"PIN rejected for {address}");
                    send(new PinRequest((byte)wrong.AttemptsLeft, 0));
                    break;
                case PinResult.Locked locked:
                    send(new PinRequest(0, (ushort)Math.Min(locked.Seconds, ushort.MaxValue)));
                    log($"PIN rejected for {address}: locked out for {locked.Seconds} s");
                    return false;
                default:
                    // Unreadable settings, no PIN, or the PIN or level changed while this answer was being hashed.
                    // Not the phone's fault, so not counted against it; but nothing proved it may come in either,
                    // and it is closed without HELLO_ACK to connect again under whatever is now in force.
                    log($"Refused {address}: its PIN could not be checked against the current security settings");
                    return false;
            }
        }
    }

    // Rounded up and kept in the frame's u16, so a phone told to wait is never let back early.
    private static ushort Seconds(TimeSpan wait) =>
        (ushort)Math.Clamp((long)Math.Ceiling(wait.TotalSeconds), 1, ushort.MaxValue);
}
