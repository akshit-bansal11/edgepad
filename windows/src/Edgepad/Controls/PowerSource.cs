using System.Globalization;
using System.Runtime.InteropServices;
using Edgepad.Power;
using Edgepad.Protocol;
using NAudio.CoreAudioApi;

namespace Edgepad.Controls;

/// <summary>
/// What TEXT BATTERIES and TEXT OUTPUT report on Windows: this laptop's battery, the batteries of connected
/// Bluetooth devices that tell Windows their level, and the default playback device. Every read is fresh, and
/// nothing a read throws escapes: it is logged once and the device is left out.
/// </summary>
internal sealed partial class PowerSource : IDisposable
{
    // Endpoint form factors (mmdeviceapi.h EndpointFormFactor).
    private const int Speakers = 1;
    private const int Headphones = 3;
    private const int Headset = 5;
    private const int DigitalAudioDisplayDevice = 9;

    private const uint FilterEnumerator = 0x1; // CM_GETIDLIST_FILTER_ENUMERATOR
    private const uint FilterPresent = 0x100; // CM_GETIDLIST_FILTER_PRESENT
    private const int Success = 0; // CR_SUCCESS
    private const uint PropertyTypeByte = 0x3; // DEVPROP_TYPE_BYTE

    // Not in the SDK headers. A byte 0-100, on the BTHENUM service node (the Hands-Free one) of a connected device.
    private static readonly DevPropKey BatteryKey = new(new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), 2);

    // DEVPKEY_Bluetooth_DeviceAddress: twelve hex digits, most significant byte first, as BTH_ADDR prints with X12.
    private static readonly DevPropKey AddressKey = new(new Guid("2BD67D8B-8BEB-48D5-87E0-6CDA3428040A"), 1);

    // Created on first use, so constructing one touches no COM.
    private MMDeviceEnumerator? devices;
    private string? logged;

    /// <summary>The laptop first, when it has a battery, then connected Bluetooth devices that report a level.</summary>
    public IReadOnlyList<BatteryEntry> ReadBatteries()
    {
        List<BatteryEntry> entries = [];
        try
        {
            if (Laptop() is { } laptop)
            {
                entries.Add(laptop);
            }
        }
        catch (Exception e)
        {
            LogOnce($"Laptop battery unreadable: {e.GetType().Name}: {e.Message}");
        }

        try
        {
            var levels = BluetoothLevels();
            foreach (var device in ConnectedBluetooth())
            {
                if (levels.TryGetValue(device.Address, out var level))
                {
                    entries.Add(new BatteryEntry(PeripheralKind(device.ClassOfDevice), level, false, device.Name));
                }
            }
        }
        catch (Exception e)
        {
            LogOnce($"Bluetooth batteries unreadable: {e.GetType().Name}: {e.Message}");
        }

        return entries;
    }

    /// <summary>The default playback device's kind and name, or null when there is none.</summary>
    public (string Kind, string Name)? ReadOutput()
    {
        try
        {
            devices ??= new MMDeviceEnumerator();
            using var device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var store = device.Properties;
            var formFactor = store.Contains(PropertyKeys.PKEY_AudioEndpoint_FormFactor)
                ? Convert.ToInt32(store[PropertyKeys.PKEY_AudioEndpoint_FormFactor].Value, CultureInfo.InvariantCulture)
                : -1;
            var endpointName = store.Contains(PropertyKeys.PKEY_Device_DeviceDesc)
                ? store[PropertyKeys.PKEY_Device_DeviceDesc].Value as string ?? ""
                : "";
            var deviceName = device.DeviceFriendlyName ?? "";

            // The endpoint store does not name its bus; a Bluetooth endpoint's device carries the paired device's name.
            var bluetooth = false;
            try
            {
                bluetooth = deviceName.Length > 0 && ConnectedBluetooth().Any(d => d.Name == deviceName);
            }
            catch (Exception e)
            {
                LogOnce($"Bluetooth devices unreadable: {e.GetType().Name}: {e.Message}");
            }

            return (OutputKindOf(formFactor), OutputName(bluetooth, deviceName, endpointName));
        }
        catch (COMException)
        {
            // No playback device at all.
            return null;
        }
        catch (Exception e)
        {
            LogOnce($"Audio output unreadable: {e.GetType().Name}: {e.Message}");
            return null;
        }
    }

    internal static string OutputKindOf(int formFactor) => formFactor switch
    {
        Speakers => OutputKind.Speaker,
        Headphones or Headset => OutputKind.Headphones,
        DigitalAudioDisplayDevice => OutputKind.Monitor,
        _ => OutputKind.Other,
    };

    /// <summary>
    /// A Bluetooth output goes by the device's own name ("AirPods Pro"), whose endpoint is only "Headphones". Any
    /// other goes by its endpoint name: "Speakers" for the built-in ones, the monitor's own name for HDMI, where
    /// the device name would be the graphics card's audio.
    /// </summary>
    internal static string OutputName(bool bluetooth, string deviceName, string endpointName) =>
        bluetooth || endpointName.Length == 0 ? deviceName : endpointName;

    /// <summary>The Bluetooth class of device: major class in bits 8-12, minor in bits 2-7.</summary>
    internal static string PeripheralKind(uint classOfDevice)
    {
        var major = (classOfDevice >> 8) & 0x1F;
        var minor = (classOfDevice >> 2) & 0x3F;
        return major switch
        {
            // Audio/Video: wearable headset, hands-free, headphones; loudspeaker.
            0x04 when minor is 0x01 or 0x02 or 0x06 => BatteryKind.Headphones,
            0x04 when minor is 0x05 => BatteryKind.Speaker,
            // Peripheral: bits 6-7 say keyboard (1), pointing device (2) or both (3).
            0x05 when (minor >> 4) is 1 or 3 => BatteryKind.Keyboard,
            0x05 when (minor >> 4) is 2 => BatteryKind.Mouse,
            _ => BatteryKind.Other,
        };
    }

    private static BatteryEntry? Laptop()
    {
        var status = SystemInformation.PowerStatus;
        // Unknown (255) carries the NoSystemBattery bit too, and an unknown level reads as 2.55.
        if (status.BatteryChargeStatus.HasFlag(BatteryChargeStatus.NoSystemBattery) || status.BatteryLifePercent is < 0f or > 1f)
        {
            return null;
        }

        return new BatteryEntry(
            BatteryKind.Laptop, (int)Math.Round(status.BatteryLifePercent * 100), status.PowerLineStatus == PowerLineStatus.Online, "");
    }

    /// <summary>Connected classic Bluetooth devices. No inquiry is made, so this answers from what Windows already knows.</summary>
    private static unsafe List<(string Address, string Name, uint ClassOfDevice)> ConnectedBluetooth()
    {
        List<(string, string, uint)> found = [];
        var search = new SearchParams { Size = (uint)sizeof(SearchParams), ReturnConnected = 1 };
        var info = new DeviceInfo { Size = (uint)sizeof(DeviceInfo) };
        var find = BluetoothFindFirstDevice(ref search, ref info);
        if (find == 0)
        {
            // ERROR_NO_MORE_ITEMS, or no radio.
            return found;
        }

        try
        {
            do
            {
                if (info.Connected != 0)
                {
                    found.Add((info.Address.ToString("X12", CultureInfo.InvariantCulture), new string((char*)info.Name), info.ClassOfDevice));
                }

                info = new DeviceInfo { Size = (uint)sizeof(DeviceInfo) };
            }
            while (BluetoothFindNextDevice(find, ref info));
        }
        finally
        {
            BluetoothFindDeviceClose(find);
        }

        return found;
    }

    /// <summary>Battery levels by device address, from the present BTHENUM nodes that carry one.</summary>
    private static unsafe Dictionary<string, int> BluetoothLevels()
    {
        Dictionary<string, int> levels = new(StringComparer.OrdinalIgnoreCase);
        const uint flags = FilterEnumerator | FilterPresent;
        if (CM_Get_Device_ID_List_SizeW(out var length, "BTHENUM", flags) != Success || length == 0)
        {
            return levels;
        }

        var ids = new char[length];
        fixed (char* buffer = ids)
        {
            if (CM_Get_Device_ID_ListW("BTHENUM", buffer, length, flags) != Success)
            {
                return levels;
            }
        }

        var address = stackalloc char[16];
        foreach (var id in new string(ids).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (CM_Locate_DevNodeW(out var node, id, 0) != Success)
            {
                continue;
            }

            byte level;
            var size = 1u;
            if (CM_Get_DevNode_PropertyW(node, in BatteryKey, out var type, &level, ref size, 0) != Success
                || type != PropertyTypeByte || size != 1)
            {
                continue;
            }

            new Span<char>(address, 16).Clear();
            size = 15 * sizeof(char); // the sixteenth stays the terminating zero
            if (CM_Get_DevNode_PropertyW(node, in AddressKey, out _, (byte*)address, ref size, 0) == Success)
            {
                levels[new string(address)] = Math.Clamp((int)level, 0, 100);
            }
        }

        return levels;
    }

    private void LogOnce(string message)
    {
        if (Interlocked.Exchange(ref logged, message) != message)
        {
            Log.Write(message);
        }
    }

    public void Dispose() => devices?.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct DevPropKey(Guid FormatId, uint PropertyId);

    // BLUETOOTH_DEVICE_SEARCH_PARAMS (bluetoothapis.h).
    [StructLayout(LayoutKind.Sequential)]
    private struct SearchParams
    {
        public uint Size;
        public int ReturnAuthenticated;
        public int ReturnRemembered;
        public int ReturnUnknown;
        public int ReturnConnected;
        public int IssueInquiry;
        public byte TimeoutMultiplier;
        public nint Radio;
    }

    // BLUETOOTH_DEVICE_INFO (bluetoothapis.h): 560 bytes, the address 8-aligned.
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct DeviceInfo
    {
        public uint Size;
        public ulong Address;
        public uint ClassOfDevice;
        public int Connected;
        public int Remembered;
        public int Authenticated;
        public fixed ushort LastSeen[8];
        public fixed ushort LastUsed[8];
        public fixed ushort Name[248];
    }

    [LibraryImport("bluetoothapis.dll")]
    private static partial nint BluetoothFindFirstDevice(ref SearchParams search, ref DeviceInfo info);

    [LibraryImport("bluetoothapis.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BluetoothFindNextDevice(nint find, ref DeviceInfo info);

    [LibraryImport("bluetoothapis.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BluetoothFindDeviceClose(nint find);

    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CM_Get_Device_ID_List_SizeW(out uint length, string? filter, uint flags);

    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int CM_Get_Device_ID_ListW(string? filter, char* buffer, uint length, uint flags);

    [LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CM_Locate_DevNodeW(out uint node, string id, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    private static unsafe partial int CM_Get_DevNode_PropertyW(
        uint node, in DevPropKey key, out uint type, byte* buffer, ref uint size, uint flags);
}
