namespace Edgepad.Net;

/// <summary>How a phone reached this laptop.</summary>
internal enum LinkKind
{
    Bluetooth,
    Network,
}

/// <summary>
/// One phone's connection, whatever carries it: the byte streams a session reads and writes frames on, and the
/// address trust and the hand-off rules know the phone by. A session closes it before it disposes the streams.
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
}
