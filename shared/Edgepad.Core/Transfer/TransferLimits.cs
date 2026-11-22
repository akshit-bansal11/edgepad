namespace Edgepad.Transfer;

/// <summary>
/// The wire status of an XFER_STATUS frame, in the order protocol 6 numbers them. The last two (0xF0 and 0xF1) are this
/// process's own answers from <see cref="TransferSender.Send"/>, never put on the wire; they sit far above the wire range so a
/// new wire status can never collide with them, and a frame that carries one is dropped as unknown.
/// </summary>
internal enum XferCode : byte
{
    /// <summary>Receiver to sender: go ahead, starting at the frame's offset.</summary>
    Ready = 0,

    /// <summary>Receiver to sender: the contiguous bytes held so far.</summary>
    Progress = 1,
    Saved = 2,
    BadHash = 3,
    Rejected = 4,
    WriteFailed = 5,
    Cancelled = 6,
    Busy = 7,

    /// <summary>Either side: this end holds the transfer; the offset is the bytes it has sent or holds.</summary>
    Paused = 8,

    /// <summary>Either side: this end lets go of its hold. The receiver follows it with READY at the bytes it holds.</summary>
    Resume = 9,

    /// <summary>Local only: no answer came in time.</summary>
    TimedOut = 0xF0,

    /// <summary>Local only: the session ended under the transfer.</summary>
    SessionEnded = 0xF1,
}

/// <summary>The numbers both ends of a transfer hold to. D-XFER in the 5.0.0 plan.</summary>
internal static class TransferLimits
{
    /// <summary>The most one file may be: 2 GiB.</summary>
    public const ulong MaxBytes = 2UL * 1024 * 1024 * 1024;

    /// <summary>Free space that must remain after the file, so a transfer cannot fill the disk.</summary>
    public const long FreeSpaceMargin = 64L * 1024 * 1024;

    /// <summary>The receiver answers PROGRESS each time this many more bytes are held.</summary>
    public const ulong ProgressEveryBytes = 256 * 1024;

    /// <summary>A partial file older than this is deleted when the receiver is created.</summary>
    public static readonly TimeSpan PartialKept = TimeSpan.FromDays(7);

    /// <summary>How long a sender waits for READY, and for the last answer after END.</summary>
    public static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(30);

    /// <summary>A receiver lets go of a transfer that has had no chunk for this long; the part stays for a resume.</summary>
    public static readonly TimeSpan IdleDrop = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A transfer held this long (by either side) ends as CANCELLED, the part kept. While held, the answer timeout and
    /// <see cref="IdleDrop"/> do not run.
    /// </summary>
    public static readonly TimeSpan PauseCap = TimeSpan.FromMinutes(10);

    /// <summary>The most of a file name kept, in UTF-8 bytes.</summary>
    public const int MaxNameBytes = 120;

    /// <summary>The longest extension kept when a name is cut, in UTF-8 bytes.</summary>
    public const int MaxExtensionBytes = 20;

    public const string PartialExtension = ".part";
}
