namespace Edgepad.Protocol;

internal static class ProtocolConstants
{
    /// <summary>
    /// Bumped only when the wire format changes in a way an older build cannot survive. A phone on another
    /// version gets this laptop's version in HELLO_ACK and is then refused, so it can say which side needs
    /// updating. protocol/actions.txt holds the same number; a test checks it.
    /// <para>
    /// 4 in 3.0.0, for the gamepad. PAD_STATE is a new frame TYPE, 0x23, and an unknown type closes the
    /// connection rather than being dropped and counted, so this is the one kind of change that has to move
    /// the number. A v3 laptop meeting a v4 phone's PAD_STATE would hang up mid-session instead of ignoring
    /// a feature it does not have; the handshake refusing first is what turns that into a message.
    /// </para>
    /// <para>
    /// The rest of the gamepad did not need it and is the rule working: PAD_ATTACH and PAD_DETACH are new
    /// action ids and PAD_STATUS a new text kind, all dropped and counted by a build that predates them. It
    /// stood at 3 from 0.6.0 through the refresh-rate dial, the macro grid and the macro icons for the same
    /// reason. From 1.0 this number moves only in a major release, and this is one.
    /// </para>
    /// </summary>
    public const byte Version = 4;

    /// <summary>Sent in HELLO so a stray connection that is not Edgepad is refused before anything runs.</summary>
    public static ReadOnlySpan<byte> Magic => "EDGP"u8;

    /// <summary>The RFCOMM service class id both apps agree on. Must match the Android side.</summary>
    public static readonly Guid ServiceId = new("758bb618-7b72-4cd3-9aa2-c9b88e54d555");
}
