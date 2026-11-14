using Edgepad.Net;

namespace Edgepad.Ui;

/// <summary>
/// The shared <see cref="StatusMemory"/> plus the one thing only Windows does: while connections are refused on
/// purpose after a forget, the quiet text is said in place of "waiting" (a held problem still wins over it).
/// </summary>
internal sealed class TrayStatus
{
    private readonly StatusMemory memory = new();
    private LaptopStatus? quiet;
    private long quietUntil;

    /// <summary>The text to show for <paramref name="status"/> at <paramref name="now"/> (milliseconds on a forward clock).</summary>
    public LaptopStatus Resolve(LaptopStatus status, long now)
    {
        var shown = memory.Resolve(status, now);
        if (shown.IsConnected)
        {
            quiet = null;
        }

        return shown.Kind == StatusKind.Waiting && quiet is not null && now < quietUntil ? quiet : shown;
    }

    /// <summary>True once per cause per hour for a protocol mismatch.</summary>
    public bool ShouldAnnounce(LaptopStatus status, long now) => memory.ShouldAnnounce(status, now);

    /// <summary>Says <paramref name="text"/> in place of "waiting" until <paramref name="until"/>: connections are being refused on purpose.</summary>
    public void Quiet(LaptopStatus text, long until)
    {
        quiet = text;
        quietUntil = until;
    }

    /// <summary>Forgets what was held, after the owner has dealt with it (a forget, a reset).</summary>
    public void Clear()
    {
        memory.Clear();
        quiet = null;
    }
}

/// <summary>What the Wi-Fi and USB row has to say when something is wrong; null while all is well.</summary>
internal static class NetworkStatus
{
    public static string? Problem(bool listening, bool announced) =>
        !listening
            ? Strings.Network_CouldNotListen
            : !announced ? Strings.Network_PhonesMayNotFind : null;
}
