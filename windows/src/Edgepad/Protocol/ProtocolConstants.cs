namespace Edgepad.Protocol;

internal static class ProtocolConstants
{
    /// <summary>
    /// Bumped only when the wire format changes in a way an older build cannot survive. A phone on another
    /// version gets this laptop's version in HELLO_ACK and is then refused, so it can say which side needs
    /// updating. protocol/actions.txt holds the same number; a test checks it.
    /// <para>
    /// Still 3 after the refresh-rate dial and the macro buttons, which is deliberate. Both are new ids in
    /// tables that already existed: an unknown control, action or text kind is dropped and counted, never an
    /// error, so an older build meets them by ignoring them rather than by breaking. Only a new frame TYPE
    /// would need a bump, because an unknown type closes the connection. From 1.0 this number moves only in
    /// a major release, and nothing here earned one.
    /// </para>
    /// </summary>
    public const byte Version = 3;

    /// <summary>Sent in HELLO so a stray connection that is not Edgepad is refused before anything runs.</summary>
    public static ReadOnlySpan<byte> Magic => "EDGP"u8;

    /// <summary>The RFCOMM service class id both apps agree on. Must match the Android side.</summary>
    public static readonly Guid ServiceId = new("758bb618-7b72-4cd3-9aa2-c9b88e54d555");
}
