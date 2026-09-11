namespace Edgepad.Protocol;

internal static class ProtocolConstants
{
    /// <summary>
    /// 2 since 0.3.0, which added TEXT timelines. A phone on another version gets this laptop's version in
    /// HELLO_ACK and is then refused, so it can say which side needs updating.
    /// </summary>
    public const byte Version = 2;

    /// <summary>Sent in HELLO so a stray connection that is not Edgepad is refused before anything runs.</summary>
    public static ReadOnlySpan<byte> Magic => "EDGP"u8;

    /// <summary>The RFCOMM service class id both apps agree on. Must match the Android side.</summary>
    public static readonly Guid ServiceId = new("758bb618-7b72-4cd3-9aa2-c9b88e54d555");
}
