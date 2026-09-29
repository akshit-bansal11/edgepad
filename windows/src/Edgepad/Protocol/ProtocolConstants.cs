namespace Edgepad.Protocol;

internal static class ProtocolConstants
{
    /// <summary>
    /// Bumped only when the wire format changes in a way an older build cannot survive. A phone on another
    /// version gets this laptop's version in HELLO_ACK and is then refused, so it can say which side needs
    /// updating. protocol/actions.txt holds the same number; a test checks it. When and why it has moved, and
    /// why only a new frame type moves it, is under Versions in docs/PROTOCOL.md, kept there once for both apps.
    /// </summary>
    public const byte Version = 4;

    /// <summary>Sent in HELLO so a stray connection that is not Edgepad is refused before anything runs.</summary>
    public static ReadOnlySpan<byte> Magic => "EDGP"u8;

    /// <summary>The RFCOMM service class id both apps agree on. Must match the Android side.</summary>
    public static readonly Guid ServiceId = new("758bb618-7b72-4cd3-9aa2-c9b88e54d555");
}
