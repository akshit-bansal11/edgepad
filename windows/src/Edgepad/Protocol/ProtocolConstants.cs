namespace Edgepad.Protocol;

internal static class ProtocolConstants
{
    public const byte Version = 1;

    /// <summary>Sent in HELLO so a stray connection that is not Edgepad is refused before anything runs.</summary>
    public static ReadOnlySpan<byte> Magic => "EDGP"u8;

    /// <summary>The RFCOMM service class id both apps agree on. Must match the Android side.</summary>
    public static readonly Guid ServiceId = new("758bb618-7b72-4cd3-9aa2-c9b88e54d555");
}
