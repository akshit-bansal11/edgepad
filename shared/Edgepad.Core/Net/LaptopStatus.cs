namespace Edgepad.Net;

/// <summary>What the laptop's connection is doing. The code that decides anything reads this, never the words.</summary>
internal enum StatusKind
{
    Starting,
    Waiting,
    Connected,

    /// <summary>The handshake's own report of a protocol mismatch; <see cref="LaptopStatus.For"/> names the phone and turns it into <see cref="Mismatch"/>.</summary>
    RefusedProtocol,
    Mismatch,
    RefusedLockedOut,
    RefusedUntrusted,
    RefusedPin,
    BluetoothOff,
    BluetoothRetrying,
    BluetoothUnavailable,
    Pairing,
    Paired,
    Asking,
    QuietUntil,
}

/// <summary>How a phone reached the laptop.</summary>
internal enum PhoneWay
{
    Bluetooth,

    /// <summary>A network link does not say which of the two it is, so neither is claimed.</summary>
    WifiOrUsb,
}

/// <summary>
/// What the laptop apps tell the owner about the connection, so Windows and Linux say the same thing. It carries the
/// state and its data; <see cref="Text"/> is the words, made only when something is shown, in the UI language.
/// <see cref="Detail"/> is the phone's name (or address), or the time for <see cref="StatusKind.QuietUntil"/>.
/// </summary>
internal sealed record LaptopStatus(StatusKind Kind, string Detail = "", PhoneWay Way = PhoneWay.Bluetooth, int PhoneVersion = 0, int LaptopVersion = 0)
{
    public static readonly LaptopStatus Starting = new(StatusKind.Starting);
    public static readonly LaptopStatus Waiting = new(StatusKind.Waiting);
    public static readonly LaptopStatus RefusedLockedOut = new(StatusKind.RefusedLockedOut);
    public static readonly LaptopStatus RefusedUntrusted = new(StatusKind.RefusedUntrusted);
    public static readonly LaptopStatus RefusedPin = new(StatusKind.RefusedPin);
    public static readonly LaptopStatus BluetoothOff = new(StatusKind.BluetoothOff);
    public static readonly LaptopStatus BluetoothRetrying = new(StatusKind.BluetoothRetrying);
    public static readonly LaptopStatus BluetoothUnavailable = new(StatusKind.BluetoothUnavailable);

    /// <summary>"Connected to Pixel · Bluetooth"; an address stands in for <paramref name="name"/> until the phone has one.</summary>
    public static LaptopStatus Connected(string name, PhoneWay way) => new(StatusKind.Connected, name, way);

    /// <summary>
    /// The shared handshake's report of a mismatch (the log keeps the numbers). A higher number is a newer app, so
    /// each side can tell which half to update without the wire carrying more than it does.
    /// </summary>
    public static LaptopStatus RefusedProtocol(int phoneVersion, int laptopVersion) =>
        new(StatusKind.RefusedProtocol, PhoneVersion: phoneVersion, LaptopVersion: laptopVersion);

    public static LaptopStatus Pairing(string phone) => new(StatusKind.Pairing, phone);

    public static LaptopStatus Paired(string phone) => new(StatusKind.Paired, phone);

    public static LaptopStatus Asking(string phone) => new(StatusKind.Asking, phone);

    /// <summary>Said in place of "waiting" while connections are refused on purpose; <paramref name="time"/> is already formatted for the owner.</summary>
    public static LaptopStatus QuietUntil(string time) => new(StatusKind.QuietUntil, time);

    /// <summary>
    /// This status as the owner reads it for the phone <paramref name="name"/> that came <paramref name="way"/>: a
    /// connection names the phone, and a protocol mismatch says which half to update. Any other status is returned as it is.
    /// </summary>
    public LaptopStatus For(string name, PhoneWay way) => Kind switch
    {
        StatusKind.Connected => Connected(name, way),
        StatusKind.RefusedProtocol when PhoneVersion != LaptopVersion => this with { Kind = StatusKind.Mismatch, Detail = name },
        _ => this,
    };

    /// <summary>"Pixel · Bluetooth": the phone and the way it came.</summary>
    public string PhoneLine => $"{Detail} · {(Way == PhoneWay.Bluetooth ? CoreStrings.Way_Bluetooth : CoreStrings.Way_WifiOrUsb)}";

    /// <summary>The status in words, in the language the UI is in.</summary>
    public string Text => Kind switch
    {
        StatusKind.Starting => CoreStrings.Status_Starting,
        StatusKind.Waiting => CoreStrings.Status_Waiting,
        StatusKind.Connected => CoreStrings.Status_Connected.Fill(PhoneLine),
        StatusKind.RefusedProtocol => CoreStrings.Status_RefusedProtocol.Fill(PhoneVersion, LaptopVersion),
        StatusKind.Mismatch => (PhoneVersion < LaptopVersion ? CoreStrings.Status_MismatchPhoneOlder : CoreStrings.Status_MismatchLaptopOlder).Fill(Detail),
        StatusKind.RefusedLockedOut => CoreStrings.Status_RefusedLockedOut,
        StatusKind.RefusedUntrusted => CoreStrings.Status_RefusedUntrusted,
        StatusKind.RefusedPin => CoreStrings.Status_RefusedPin,
        StatusKind.BluetoothOff => CoreStrings.Status_BluetoothOff,
        StatusKind.BluetoothRetrying => CoreStrings.Status_BluetoothRetrying,
        StatusKind.BluetoothUnavailable => CoreStrings.Status_BluetoothUnavailable,
        StatusKind.Pairing => CoreStrings.Status_Pairing.Fill(Detail),
        StatusKind.Paired => CoreStrings.Status_Paired.Fill(Detail),
        StatusKind.Asking => CoreStrings.Status_Asking.Fill(Detail),
        StatusKind.QuietUntil => CoreStrings.Status_QuietUntil.Fill(Detail),
        _ => throw new InvalidOperationException($"No text for status {Kind}"),
    };

    /// <summary>
    /// True for a status that reports something wrong rather than the plain state: a refusal, a mismatch, a radio that
    /// is off. A tray keeps these on screen longer than the next "waiting" would let it.
    /// </summary>
    public bool IsProblem => Kind is StatusKind.RefusedProtocol or StatusKind.Mismatch or StatusKind.RefusedLockedOut
        or StatusKind.RefusedUntrusted or StatusKind.RefusedPin or StatusKind.BluetoothOff or StatusKind.BluetoothRetrying
        or StatusKind.BluetoothUnavailable;

    /// <summary>
    /// True for a radio that is off or failing. Unlike a refusal this is a live condition, not a past event: the server
    /// posts "waiting" the moment the radio recovers, so a tray must not hold it past that.
    /// </summary>
    public bool IsRadioCondition => Kind is StatusKind.BluetoothOff or StatusKind.BluetoothRetrying or StatusKind.BluetoothUnavailable;

    /// <summary>True for the status of a live phone.</summary>
    public bool IsConnected => Kind == StatusKind.Connected;

    /// <summary>The one refusal the owner cannot see coming, so the tray also raises a notice for it.</summary>
    public bool IsMismatch => Kind == StatusKind.Mismatch;
}
