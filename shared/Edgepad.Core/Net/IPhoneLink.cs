namespace Edgepad.Net;

/// <summary>How a phone reached this laptop.</summary>
internal enum LinkKind
{
    Bluetooth,
    Network,
}

/// <summary>
/// One phone's connection, whatever carries it: the byte streams a session reads and writes frames on, and the
/// address trust and the hand-off rules know the phone by. A session closes it, then releases it.
/// </summary>
internal interface IPhoneLink
{
    /// <summary>The phone's address, as trust, the log and the status line name it.</summary>
    string Address { get; }

    /// <summary>What to call this link where the owner reads it.</summary>
    string Label { get; }

    LinkKind Kind { get; }

    Stream Input { get; }

    Stream Output { get; }

    /// <summary>
    /// Closes the connection, which aborts a read or write blocked on it. Safe to call from any thread, and more
    /// than once.
    /// </summary>
    void Close();

    /// <summary>
    /// Disposes the streams and whatever they leave open, once, after <see cref="Close"/>: a stream that throws
    /// on the way never leaves the connection's handle open. The session calls it under its send lock, so never
    /// under a writer's feet; a link refused before any session is released by whoever refused it.
    /// </summary>
    void Release();
}
